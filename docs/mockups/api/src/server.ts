import express, { type NextFunction, type Request, type Response } from "express";
import { format, isValid, kind, normalize } from "./cuit.js";
import { assignableRoles, can, permissionsOf, ROLE_LABEL, type OrgRole } from "./permissions.js";
import { applyScenario, findScenario, resetAll, scenarios } from "./scenarios.js";
import {
  audit,
  createSession,
  db,
  defaultOrgFor,
  effectiveStatus,
  findInvitationByToken,
  findOrgByCuit,
  findSession,
  findUserByEmail,
  findUserById,
  LOGIN_BLOCK_MS,
  MAX_LOGIN_FAILURES,
  membershipIn,
  membershipsOf,
  membersOf,
  newId,
  newToken,
  now,
  orgById,
  platformOf,
  pushMail,
  seed,
  SIMULATED_RECOVERY_CODES,
  SIMULATED_TOTP_CODE,
  SIMULATED_TOTP_SECRET,
  stepUpFresh,
  TOKEN_TTL_MS,
  WEB_ORIGIN,
  type Invitation,
  type Org,
  type Session,
  type User,
} from "./store.js";

const PORT = 3001;
const SESSION_COOKIE = "sid";
const PAGE_SIZE = 5;

seed();

const app = express();
app.use(express.json());

// --- helpers ---------------------------------------------------------------

function fail(res: Response, status: number, code: string, message: string, extra: object = {}): void {
  res.status(status).json({ error: { code, message, ...extra } });
}

/** Express 5 tipa los params como string | string[]. Acá siempre es uno solo. */
function param(req: Request, name: string): string {
  const value = req.params[name];
  return Array.isArray(value) ? (value[0] ?? "") : (value ?? "");
}

function readCookie(req: Request, name: string): string | null {
  const header = req.headers.cookie;
  if (!header) return null;
  for (const part of header.split(";")) {
    const [key, ...rest] = part.trim().split("=");
    if (key === name) return decodeURIComponent(rest.join("="));
  }
  return null;
}

type AuthedRequest = Request & { session: Session; user: User };

function requireSession(req: Request, res: Response, next: NextFunction): void {
  const sid = readCookie(req, SESSION_COOKIE);
  const session = sid ? findSession(sid) : undefined;
  const user = session ? findUserById(session.userId) : undefined;
  if (!session || !user) {
    fail(res, 401, "unauthenticated", "Tu sesión no está activa. Ingresá de nuevo para continuar.");
    return;
  }
  (req as AuthedRequest).session = session;
  (req as AuthedRequest).user = user;
  next();
}

/** Fallas armadas desde el panel de demostración. No afectan a /api/dev. */
app.use("/api", (req, res, next) => {
  if (req.path.startsWith("/dev")) {
    next();
    return;
  }
  if (db.failNext === "network") {
    db.failNext = null;
    fail(res, 500, "server_error", "Falla simulada del servidor.");
    return;
  }
  if (db.failNext === "notfound") {
    db.failNext = null;
    fail(res, 404, "not_found", "No encontramos ese recurso.");
    return;
  }
  next();
});

// --- DTOs ------------------------------------------------------------------

function orgSummary(org: Org, role: OrgRole) {
  return { id: org.id, name: org.name, type: org.type, status: org.status, role, roleLabel: ROLE_LABEL[role] };
}

function activeOrgDto(org: Org, role: OrgRole) {
  return {
    id: org.id,
    name: org.name,
    type: org.type,
    cuit: format(org.cuit),
    status: org.status,
    suspendedReason: org.suspendedReason,
    role,
    roleLabel: ROLE_LABEL[role],
    permissions: permissionsOf(role),
    version: org.version,
  };
}

function pendingInvitationsFor(email: string) {
  return db.invitations
    .filter((i) => i.email === email && i.scope === "org" && effectiveStatus(i) === "pending")
    .map((i) => ({ token: i.token, orgName: orgById(i.orgId)?.name ?? "—", role: i.role as OrgRole }));
}

function meDto(user: User, session: Session) {
  const memberships = membershipsOf(user.id);
  const orgs = memberships
    .map((m) => ({ org: orgById(m.orgId), role: m.role }))
    .filter((x): x is { org: Org; role: OrgRole } => Boolean(x.org))
    .map((x) => orgSummary(x.org, x.role));

  const activeMembership = memberships.find((m) => m.orgId === session.activeOrgId);
  const activeOrg = activeMembership ? orgById(activeMembership.orgId) : undefined;
  const platform = platformOf(user.id);

  return {
    user: { id: user.id, name: user.name, email: user.email, confirmed: user.confirmed },
    activeOrg: activeOrg && activeMembership ? activeOrgDto(activeOrg, activeMembership.role) : null,
    orgs,
    pendingInvitations: pendingInvitationsFor(user.email),
    platform: platform
      ? {
          role: platform.role,
          activated: Boolean(platform.activatedAt),
          mfaEnrolled: user.mfa.enrolled,
          recoveryCodesSaved: user.mfa.recoveryCodesSaved,
          stepUpFresh: stepUpFresh(session),
        }
      : null,
  };
}

function invitationDto(invitation: Invitation) {
  const status = effectiveStatus(invitation);
  const org = orgById(invitation.orgId);
  const invitedBy = invitation.invitedByUserId ? findUserById(invitation.invitedByUserId) : undefined;
  return {
    token: invitation.token,
    scope: invitation.scope,
    email: invitation.email,
    role: invitation.role,
    roleLabel: ROLE_LABEL[invitation.role as OrgRole] ?? "Administradora",
    status,
    orgName: org?.name ?? (invitation.scope === "platform" ? "Platform" : "—"),
    invitedByName: invitedBy?.name ?? null,
    expiresAt: invitation.expiresAt,
  };
}

// --- salud -----------------------------------------------------------------

app.get("/api/health", (_req, res) => {
  res.json({ ok: true });
});

// --- registro / confirmación / sesión (Recorrido A) ------------------------

app.post("/api/auth/register", (req, res) => {
  const body = (req.body ?? {}) as Record<string, unknown>;
  const cuitInput = String(body.cuit ?? "");
  const name = String(body.name ?? "").trim();
  const email = String(body.email ?? "").trim().toLowerCase();
  const password = String(body.password ?? "");

  const cuit = normalize(cuitInput);
  const type = cuit ? kind(cuit) : null;
  if (!cuit || !type || !isValid(cuit)) {
    fail(res, 400, "invalid_cuit", "El CUIT no es válido.");
    return;
  }
  if (!name || !email || !password) {
    fail(res, 400, "missing_fields", "Completá nombre, correo y contraseña.");
    return;
  }
  if (password.length < 4) {
    fail(res, 400, "weak_password", "La contraseña tiene que tener al menos 4 caracteres.");
    return;
  }
  if (findOrgByCuit(cuit)) {
    fail(res, 409, "cuit_taken", "Ese CUIT ya está registrado.");
    return;
  }

  const existing = findUserByEmail(email);
  if (existing) {
    // Respuesta neutral: no revelamos que la identidad existe.
    pushMail({
      to: existing.email,
      subject: "Ya tenés cuenta, ingresá",
      body: `Hola, ${existing.name}. Alguien intentó registrarse con este correo. Si fuiste vos, ingresá con tu contraseña.`,
      link: `${WEB_ORIGIN}/login`,
      kind: "registro",
    });
    res.status(202).end();
    return;
  }

  const user: User = {
    id: newId(),
    email,
    password,
    name,
    confirmed: false,
    mfa: { enrolled: false, secret: null, recoveryCodesSaved: false, verifiedAt: null },
    createdAt: now(),
  };
  const org: Org = {
    id: newId(),
    type,
    name,
    cuit,
    status: "active",
    suspendedReason: null,
    version: 1,
    createdAt: now(),
  };
  db.users.push(user);
  db.orgs.push(org);
  db.memberships.push({ userId: user.id, orgId: org.id, role: "owner" });
  sendConfirmationMail(user);
  res.status(202).end();
});

function sendConfirmationMail(user: User): void {
  const token = newToken();
  db.confirmTokens.push({
    token,
    userId: user.id,
    expiresAt: new Date(Date.now() + TOKEN_TTL_MS).toISOString(),
    usedAt: null,
  });
  pushMail({
    to: user.email,
    subject: "Confirmá tu correo",
    body: `Hola, ${user.name}. Para activar tu cuenta confirmá este correo. El enlace vence en 24 horas.`,
    link: `${WEB_ORIGIN}/confirmar?token=${token}`,
    kind: "confirmacion",
  });
}

app.post("/api/auth/confirm", (req, res) => {
  const token = String((req.body ?? {}).token ?? "");
  if (!token) {
    fail(res, 400, "missing_token", "El enlace no trae ningún código de confirmación.");
    return;
  }
  const record = db.confirmTokens.find((t) => t.token === token);
  if (!record) {
    fail(res, 400, "invalid_token", "El enlace no es válido o venció.");
    return;
  }
  if (record.usedAt) {
    res.status(204).end();
    return;
  }
  if (new Date(record.expiresAt).getTime() < Date.now()) {
    fail(res, 400, "expired_token", "El enlace venció. Pedí uno nuevo desde el ingreso.");
    return;
  }
  const user = findUserById(record.userId);
  if (!user) {
    fail(res, 400, "invalid_token", "El enlace no es válido o venció.");
    return;
  }
  record.usedAt = now();
  user.confirmed = true;
  res.status(204).end();
});

function attemptsFor(email: string) {
  let record = db.attempts.find((a) => a.email === email);
  if (!record) {
    record = { email, failures: 0, blockedUntil: null };
    db.attempts.push(record);
  }
  return record;
}

app.post("/api/auth/login", (req, res) => {
  const body = (req.body ?? {}) as Record<string, unknown>;
  const email = String(body.email ?? "").trim().toLowerCase();
  const password = String(body.password ?? "");
  const record = attemptsFor(email);

  if (record.blockedUntil && new Date(record.blockedUntil).getTime() > Date.now()) {
    const retryAfter = Math.ceil((new Date(record.blockedUntil).getTime() - Date.now()) / 1000);
    fail(res, 429, "too_many_attempts", `Demasiados intentos. Probá de nuevo en ${retryAfter} segundos.`, { retryAfter });
    return;
  }

  const user = findUserByEmail(email);
  if (!user || user.password !== password) {
    record.failures += 1;
    if (record.failures >= MAX_LOGIN_FAILURES) {
      record.blockedUntil = new Date(Date.now() + LOGIN_BLOCK_MS).toISOString();
      record.failures = 0;
    }
    fail(res, 401, "invalid_credentials", "El correo o la contraseña no coinciden.");
    return;
  }
  if (!user.confirmed) {
    fail(res, 403, "unconfirmed", "Todavía no confirmaste tu correo. Buscá el mensaje de confirmación.");
    return;
  }

  record.failures = 0;
  record.blockedUntil = null;
  const session = createSession(user.id, defaultOrgFor(user.id));
  res.cookie(SESSION_COOKIE, session.id, { httpOnly: true, sameSite: "lax", path: "/" });
  res.status(204).end();
});

app.post("/api/auth/logout", (req, res) => {
  const sid = readCookie(req, SESSION_COOKIE);
  if (sid) {
    const index = db.sessions.findIndex((s) => s.id === sid);
    if (index >= 0) db.sessions.splice(index, 1);
  }
  res.clearCookie(SESSION_COOKIE, { path: "/" });
  res.status(204).end();
});

// --- identidad y contexto (Recorrido B) ------------------------------------

app.get("/api/me", requireSession, (req, res) => {
  const { user, session } = req as AuthedRequest;
  res.json(meDto(user, session));
});

app.put("/api/me/org", requireSession, (req, res) => {
  const { user, session } = req as AuthedRequest;
  const orgId = String((req.body ?? {}).orgId ?? "");
  if (!membershipIn(user.id, orgId)) {
    fail(res, 403, "forbidden", "No pertenecés a esa organización.");
    return;
  }
  // Cambiar de organización nunca cambia la identidad: la sesión es la misma.
  session.activeOrgId = orgId;
  res.json(meDto(user, session));
});

app.post("/api/orgs", requireSession, (req, res) => {
  const { user, session } = req as AuthedRequest;
  const body = (req.body ?? {}) as Record<string, unknown>;
  const name = String(body.name ?? "").trim();
  const cuit = normalize(String(body.cuit ?? ""));
  const type = cuit ? kind(cuit) : null;

  if (!cuit || !type || !isValid(cuit)) {
    fail(res, 400, "invalid_cuit", "El CUIT no es válido.");
    return;
  }
  if (!name) {
    fail(res, 400, "missing_fields", "Completá el nombre o razón social.");
    return;
  }
  if (findOrgByCuit(cuit)) {
    fail(res, 409, "cuit_taken", "Ese CUIT ya está registrado.");
    return;
  }

  const org: Org = {
    id: newId(),
    type,
    name,
    cuit,
    status: "active",
    suspendedReason: null,
    version: 1,
    createdAt: now(),
  };
  db.orgs.push(org);
  db.memberships.push({ userId: user.id, orgId: org.id, role: "owner" });
  session.activeOrgId = org.id;
  audit(user.email, "org.create", org.name, "Alta de organización desde el producto", "ok");
  res.status(201).json(meDto(user, session));
});

// --- integrantes e invitaciones (Recorrido C) ------------------------------

function requireOrgAccess(req: Request, res: Response): { user: User; session: Session; org: Org; role: OrgRole } | null {
  const { user, session } = req as AuthedRequest;
  const org = orgById(param(req, "orgId"));
  if (!org) {
    fail(res, 404, "not_found", "No encontramos esa organización.");
    return null;
  }
  const membership = membershipIn(user.id, org.id);
  if (!membership) {
    fail(res, 403, "forbidden", "No pertenecés a esa organización.");
    return null;
  }
  return { user, session, org, role: membership.role };
}

app.get("/api/orgs/:orgId/members", requireSession, (req, res) => {
  const ctx = requireOrgAccess(req, res);
  if (!ctx) return;
  const members = membersOf(ctx.org.id)
    .map((m) => ({ membership: m, user: findUserById(m.userId) }))
    .filter((x): x is { membership: (typeof x)["membership"]; user: User } => Boolean(x.user))
    .map((x) => ({
      id: x.user.id,
      name: x.user.name,
      email: x.user.email,
      role: x.membership.role,
      roleLabel: ROLE_LABEL[x.membership.role],
    }));
  res.json({ members });
});

app.get("/api/orgs/:orgId/invitations", requireSession, (req, res) => {
  const ctx = requireOrgAccess(req, res);
  if (!ctx) return;
  if (!can(ctx.role, "members.invite")) {
    fail(res, 403, "forbidden", `En ${ctx.org.name} tu rol es ${ROLE_LABEL[ctx.role]} y no incluye invitar integrantes.`);
    return;
  }
  const invitations = db.invitations
    .filter((i) => i.scope === "org" && i.orgId === ctx.org.id)
    .map((i) => ({ id: i.id, email: i.email, role: i.role, status: effectiveStatus(i), expiresAt: i.expiresAt, token: i.token }));
  res.json({ invitations, assignableRoles: assignableRoles(ctx.role) });
});

app.post("/api/orgs/:orgId/invitations", requireSession, (req, res) => {
  const ctx = requireOrgAccess(req, res);
  if (!ctx) return;
  if (!can(ctx.role, "members.invite")) {
    audit(ctx.user.email, "members.invite", ctx.org.name, "Intento sin permiso", "denegado");
    fail(res, 403, "forbidden", `En ${ctx.org.name} tu rol es ${ROLE_LABEL[ctx.role]} y no incluye invitar integrantes.`);
    return;
  }
  if (ctx.org.status === "suspended") {
    fail(res, 409, "org_suspended", "La organización está suspendida: no se pueden emitir invitaciones.");
    return;
  }

  const body = (req.body ?? {}) as Record<string, unknown>;
  const email = String(body.email ?? "").trim().toLowerCase();
  const role = String(body.role ?? "member") as OrgRole;

  if (!email || !email.includes("@")) {
    fail(res, 400, "invalid_email", "Escribí una dirección de correo válida.");
    return;
  }
  if (!assignableRoles(ctx.role).includes(role)) {
    fail(res, 403, "role_not_allowed", "No podés invitar con un rol de mayor autoridad que el tuyo.");
    return;
  }
  const invitee = findUserByEmail(email);
  if (invitee && membershipIn(invitee.id, ctx.org.id)) {
    fail(res, 409, "already_member", "Esa persona ya integra la organización.");
    return;
  }
  const duplicate = db.invitations.find(
    (i) => i.scope === "org" && i.orgId === ctx.org.id && i.email === email && effectiveStatus(i) === "pending",
  );
  if (duplicate) {
    fail(res, 409, "already_invited", "Ya hay una invitación pendiente para esa dirección.");
    return;
  }

  const invitation: Invitation = {
    id: newId(),
    token: newToken(),
    scope: "org",
    orgId: ctx.org.id,
    email,
    role,
    status: "pending",
    expiresAt: new Date(Date.now() + TOKEN_TTL_MS * 2).toISOString(),
    createdAt: now(),
    invitedByUserId: ctx.user.id,
  };
  db.invitations.push(invitation);
  pushMail({
    to: email,
    subject: `Te invitaron a ${ctx.org.name}`,
    body: `${ctx.user.name} te invitó a sumarte a ${ctx.org.name} como ${ROLE_LABEL[role]}.`,
    link: `${WEB_ORIGIN}/invitacion?token=${invitation.token}`,
    kind: "invitacion",
  });
  audit(ctx.user.email, "members.invite", `${email} → ${ctx.org.name}`, `Rol ${ROLE_LABEL[role]}`, "ok");
  res.status(201).json(invitationDto(invitation));
});

app.post("/api/orgs/:orgId/invitations/:invitationId/cancel", requireSession, (req, res) => {
  const ctx = requireOrgAccess(req, res);
  if (!ctx) return;
  if (!can(ctx.role, "members.invite")) {
    fail(res, 403, "forbidden", "Tu rol no permite gestionar invitaciones.");
    return;
  }
  const invitation = db.invitations.find((i) => i.id === param(req, "invitationId") && i.orgId === ctx.org.id);
  if (!invitation) {
    fail(res, 404, "not_found", "No encontramos esa invitación.");
    return;
  }
  if (effectiveStatus(invitation) !== "pending") {
    fail(res, 409, "not_pending", "Esa invitación ya no está pendiente.");
    return;
  }
  invitation.status = "cancelled";
  audit(ctx.user.email, "members.invite.cancel", invitation.email, `Invitación a ${ctx.org.name} cancelada`, "ok");
  res.json(invitationDto(invitation));
});

/** Público: la pantalla de invitación se abre sin sesión. */
app.get("/api/invitations/:token", (req, res) => {
  const invitation = findInvitationByToken(param(req, "token"));
  if (!invitation) {
    fail(res, 404, "invalid_invitation", "El enlace de invitación no es válido.");
    return;
  }
  const sid = readCookie(req, SESSION_COOKIE);
  const session = sid ? findSession(sid) : undefined;
  const viewer = session ? findUserById(session.userId) : undefined;
  const invitee = findUserByEmail(invitation.email);
  const status = effectiveStatus(invitation);

  let next: "register" | "confirm" | "login" | "accept" | "other-identity" | "none" = "none";
  if (status === "pending") {
    if (!viewer) next = !invitee ? "register" : !invitee.confirmed ? "confirm" : "login";
    else if (viewer.email !== invitation.email) next = "other-identity";
    else if (!viewer.confirmed) next = "confirm";
    else next = "accept";
  }

  res.json({
    ...invitationDto(invitation),
    next,
    viewerEmail: viewer?.email ?? null,
    alreadyMember: Boolean(invitee && invitation.orgId && membershipIn(invitee.id, invitation.orgId)),
  });
});

/**
 * Alta de una identidad invitada. No crea organización: la membresía llega al
 * aceptar la invitación, y recién después de confirmar el correo e ingresar.
 */
app.post("/api/invitations/:token/register", (req, res) => {
  const invitation = findInvitationByToken(param(req, "token"));
  if (!invitation) {
    fail(res, 404, "invalid_invitation", "El enlace de invitación no es válido.");
    return;
  }
  if (effectiveStatus(invitation) !== "pending") {
    fail(res, 409, "not_pending", "Esta invitación ya no está pendiente.");
    return;
  }
  const body = (req.body ?? {}) as Record<string, unknown>;
  const name = String(body.name ?? "").trim();
  const password = String(body.password ?? "");
  if (!name || password.length < 4) {
    fail(res, 400, "missing_fields", "Completá tu nombre y una contraseña de al menos 4 caracteres.");
    return;
  }
  if (findUserByEmail(invitation.email)) {
    // Neutral: no confirmamos ni negamos la existencia de la identidad.
    res.status(202).end();
    return;
  }
  const user: User = {
    id: newId(),
    email: invitation.email,
    password,
    name,
    confirmed: false,
    mfa: { enrolled: false, secret: null, recoveryCodesSaved: false, verifiedAt: null },
    createdAt: now(),
  };
  db.users.push(user);
  sendConfirmationMail(user);
  res.status(202).end();
});

app.post("/api/invitations/:token/accept", requireSession, (req, res) => {
  const { user, session } = req as AuthedRequest;
  const invitation = findInvitationByToken(param(req, "token"));
  if (!invitation) {
    fail(res, 404, "invalid_invitation", "El enlace de invitación no es válido.");
    return;
  }
  if (user.email !== invitation.email) {
    fail(res, 403, "other_identity", `La invitación es para ${invitation.email}. Cerrá sesión e ingresá con esa identidad.`);
    return;
  }
  if (!user.confirmed) {
    fail(res, 403, "unconfirmed", "Confirmá tu correo antes de aceptar la invitación.");
    return;
  }

  const status = effectiveStatus(invitation);
  const org = orgById(invitation.orgId);

  if (status === "accepted") {
    // Idempotente: no duplicamos membresía ni volvemos a auditar.
    res.json({ alreadyAccepted: true, me: meDto(user, session) });
    return;
  }
  if (status === "expired") {
    fail(res, 409, "expired_invitation", "La invitación venció. Pedile a quien te invitó que emita una nueva.");
    return;
  }
  if (status === "cancelled") {
    fail(res, 409, "cancelled_invitation", "La invitación fue cancelada por la organización.");
    return;
  }
  if (!org) {
    fail(res, 404, "not_found", "La organización de la invitación ya no existe.");
    return;
  }

  invitation.status = "accepted";
  if (!membershipIn(user.id, org.id)) {
    db.memberships.push({ userId: user.id, orgId: org.id, role: invitation.role as OrgRole });
  }
  session.activeOrgId = org.id;
  audit(user.email, "members.invite.accept", org.name, `Alta como ${ROLE_LABEL[invitation.role as OrgRole]}`, "ok");
  res.json({ alreadyAccepted: false, me: meDto(user, session) });
});

// --- Platform (Recorrido D) ------------------------------------------------

interface PlatformCtx {
  user: User;
  session: Session;
  role: "owner" | "admin";
  activated: boolean;
}

function requirePlatform(req: Request, res: Response): PlatformCtx | null {
  const { user, session } = req as AuthedRequest;
  const membership = platformOf(user.id);
  if (!membership) {
    fail(res, 403, "not_platform", "Tu identidad no tiene acceso a Platform.");
    return null;
  }
  return { user, session, role: membership.role, activated: Boolean(membership.activatedAt) };
}

function requireActivePlatform(req: Request, res: Response): PlatformCtx | null {
  const ctx = requirePlatform(req, res);
  if (!ctx) return null;
  if (!ctx.user.mfa.enrolled || !ctx.activated) {
    fail(res, 403, "mfa_required", "Completá la secuencia de MFA simulada para activar tu acceso a Platform.");
    return null;
  }
  return ctx;
}

function requireStepUp(res: Response, ctx: PlatformCtx): boolean {
  if (stepUpFresh(ctx.session)) return true;
  fail(res, 403, "step_up_required", "Esta operación necesita revalidar MFA. La última verificación ya venció.");
  return false;
}

app.get("/api/platform/me", requireSession, (req, res) => {
  const ctx = requirePlatform(req, res);
  if (!ctx) return;
  res.json({
    role: ctx.role,
    activated: ctx.activated,
    mfa: {
      enrolled: ctx.user.mfa.enrolled,
      recoveryCodesSaved: ctx.user.mfa.recoveryCodesSaved,
      verifiedAt: ctx.user.mfa.verifiedAt,
    },
    stepUpFresh: stepUpFresh(ctx.session),
  });
});

app.post("/api/platform/mfa/enroll", requireSession, (req, res) => {
  const ctx = requirePlatform(req, res);
  if (!ctx) return;
  ctx.user.mfa.secret = SIMULATED_TOTP_SECRET;
  res.json({ secret: SIMULATED_TOTP_SECRET, simulatedCode: SIMULATED_TOTP_CODE });
});

app.post("/api/platform/mfa/verify", requireSession, (req, res) => {
  const ctx = requirePlatform(req, res);
  if (!ctx) return;
  const code = String((req.body ?? {}).code ?? "").trim();
  if (code !== SIMULATED_TOTP_CODE) {
    fail(res, 400, "invalid_code", "El código no coincide. En la simulación el código es 123456.");
    return;
  }
  ctx.user.mfa.enrolled = true;
  ctx.user.mfa.verifiedAt = now();
  ctx.session.stepUpAt = now();
  res.json({ recoveryCodes: SIMULATED_RECOVERY_CODES });
});

app.post("/api/platform/mfa/recovery-codes/ack", requireSession, (req, res) => {
  const ctx = requirePlatform(req, res);
  if (!ctx) return;
  if (!ctx.user.mfa.enrolled) {
    fail(res, 409, "mfa_not_verified", "Primero verificá el código simulado.");
    return;
  }
  ctx.user.mfa.recoveryCodesSaved = true;
  const membership = platformOf(ctx.user.id);
  if (membership && !membership.activatedAt) {
    membership.activatedAt = now();
    audit(ctx.user.email, "platform.activate", ctx.user.email, "Membresía Platform activada tras MFA simulada", "ok");
  }
  res.json({ activated: true });
});

app.post("/api/platform/step-up", requireSession, (req, res) => {
  const ctx = requirePlatform(req, res);
  if (!ctx) return;
  const code = String((req.body ?? {}).code ?? "").trim();
  if (!ctx.user.mfa.enrolled) {
    fail(res, 409, "mfa_not_enrolled", "Todavía no completaste el enrolamiento simulado.");
    return;
  }
  if (code !== SIMULATED_TOTP_CODE) {
    fail(res, 400, "invalid_code", "El código no coincide. En la simulación el código es 123456.");
    return;
  }
  ctx.session.stepUpAt = now();
  ctx.user.mfa.verifiedAt = now();
  res.json({ stepUpFresh: true });
});

function paginate<T>(rows: T[], cursorRaw: unknown) {
  const cursor = Number.parseInt(String(cursorRaw ?? "0"), 10);
  const start = Number.isFinite(cursor) && cursor > 0 ? cursor : 0;
  const page = rows.slice(start, start + PAGE_SIZE);
  const nextCursor = start + PAGE_SIZE < rows.length ? String(start + PAGE_SIZE) : null;
  return { page, nextCursor, total: rows.length };
}

app.get("/api/platform/orgs", requireSession, (req, res) => {
  const ctx = requireActivePlatform(req, res);
  if (!ctx) return;
  // Allowlist: nunca CUIT ni datos de negocio.
  const rows = db.orgs.map((o) => ({
    id: o.id,
    name: o.name,
    status: o.status,
    suspendedReason: o.suspendedReason,
    membersCount: membersOf(o.id).length,
    createdAt: o.createdAt,
    version: o.version,
  }));
  res.json(paginate(rows, req.query.cursor));
});

app.get("/api/platform/identities", requireSession, (req, res) => {
  const ctx = requireActivePlatform(req, res);
  if (!ctx) return;
  const rows = db.users.map((u) => ({
    id: u.id,
    name: u.name,
    email: u.email,
    confirmed: u.confirmed,
    orgsCount: membershipsOf(u.id).length,
    createdAt: u.createdAt,
  }));
  res.json(paginate(rows, req.query.cursor));
});

app.get("/api/platform/admins", requireSession, (req, res) => {
  const ctx = requireActivePlatform(req, res);
  if (!ctx) return;
  const rows = db.platform
    .map((p) => ({ p, u: findUserById(p.userId) }))
    .filter((x): x is { p: (typeof x)["p"]; u: User } => Boolean(x.u))
    .map((x) => ({
      id: x.u.id,
      name: x.u.name,
      email: x.u.email,
      role: x.p.role,
      activated: Boolean(x.p.activatedAt),
      mfaEnrolled: x.u.mfa.enrolled,
    }));
  res.json(paginate(rows, req.query.cursor));
});

app.get("/api/platform/audit", requireSession, (req, res) => {
  const ctx = requireActivePlatform(req, res);
  if (!ctx) return;
  res.json(paginate(db.audit, req.query.cursor));
});

app.post("/api/platform/orgs/:orgId/suspend", requireSession, (req, res) => {
  const ctx = requireActivePlatform(req, res);
  if (!ctx) return;
  if (!requireStepUp(res, ctx)) return;
  const org = orgById(param(req, "orgId"));
  if (!org) {
    fail(res, 404, "not_found", "No encontramos esa organización.");
    return;
  }
  const reason = String((req.body ?? {}).reason ?? "").trim();
  const version = Number((req.body ?? {}).version ?? -1);
  if (!reason) {
    fail(res, 400, "missing_reason", "Escribí la razón de la suspensión.");
    return;
  }
  if (db.failNext === "conflict" || version !== org.version) {
    db.failNext = null;
    fail(res, 409, "conflict", "Otra persona modificó esta organización mientras mirabas la pantalla. Recargá y reintentá.");
    return;
  }
  if (org.status === "suspended") {
    fail(res, 409, "already_suspended", "La organización ya estaba suspendida.");
    return;
  }
  org.status = "suspended";
  org.suspendedReason = reason;
  org.version += 1;
  audit(ctx.user.email, "org.suspend", org.name, reason, "ok");
  res.json({ id: org.id, status: org.status, version: org.version, suspendedReason: org.suspendedReason });
});

app.post("/api/platform/orgs/:orgId/reactivate", requireSession, (req, res) => {
  const ctx = requireActivePlatform(req, res);
  if (!ctx) return;
  if (!requireStepUp(res, ctx)) return;
  const org = orgById(param(req, "orgId"));
  if (!org) {
    fail(res, 404, "not_found", "No encontramos esa organización.");
    return;
  }
  const version = Number((req.body ?? {}).version ?? -1);
  if (db.failNext === "conflict" || version !== org.version) {
    db.failNext = null;
    fail(res, 409, "conflict", "Otra persona modificó esta organización mientras mirabas la pantalla. Recargá y reintentá.");
    return;
  }
  if (org.status === "active") {
    fail(res, 409, "already_active", "La organización ya estaba activa.");
    return;
  }
  org.status = "active";
  org.suspendedReason = null;
  org.version += 1;
  audit(ctx.user.email, "org.reactivate", org.name, "Reactivación desde Platform", "ok");
  res.json({ id: org.id, status: org.status, version: org.version, suspendedReason: null });
});

app.post("/api/platform/admins", requireSession, (req, res) => {
  const ctx = requireActivePlatform(req, res);
  if (!ctx) return;
  if (!requireStepUp(res, ctx)) return;
  const email = String((req.body ?? {}).email ?? "").trim().toLowerCase();
  if (!email || !email.includes("@")) {
    fail(res, 400, "invalid_email", "Escribí una dirección de correo válida.");
    return;
  }
  const existing = findUserByEmail(email);
  if (existing && platformOf(existing.id)) {
    fail(res, 409, "already_admin", "Esa identidad ya administra Platform.");
    return;
  }
  const invitation: Invitation = {
    id: newId(),
    token: newToken(),
    scope: "platform",
    orgId: null,
    email,
    role: "admin",
    status: "pending",
    expiresAt: new Date(Date.now() + TOKEN_TTL_MS).toISOString(),
    createdAt: now(),
    invitedByUserId: ctx.user.id,
  };
  db.invitations.push(invitation);
  pushMail({
    to: email,
    subject: "Invitación para administrar Platform",
    body: `${ctx.user.name} te invitó a administrar Platform. Vas a tener que completar la secuencia de MFA simulada.`,
    link: `${WEB_ORIGIN}/platform/invitacion?token=${invitation.token}`,
    kind: "invitacion-platform",
  });
  audit(ctx.user.email, "platform.admin.invite", email, "Invitación de administrador emitida", "ok");
  res.status(201).json(invitationDto(invitation));
});

app.delete("/api/platform/admins/:userId", requireSession, (req, res) => {
  const ctx = requireActivePlatform(req, res);
  if (!ctx) return;
  if (!requireStepUp(res, ctx)) return;
  const target = db.platform.find((p) => p.userId === param(req, "userId"));
  const targetUser = target ? findUserById(target.userId) : undefined;
  if (!target || !targetUser) {
    fail(res, 404, "not_found", "No encontramos ese administrador.");
    return;
  }
  const owners = db.platform.filter((p) => p.role === "owner");
  if (target.role === "owner" && owners.length === 1) {
    audit(ctx.user.email, "platform.admin.revoke", targetUser.email, "Último titular: revocación impedida", "denegado");
    fail(
      res,
      409,
      "last_owner",
      "Es la única titular de Platform. Designá otra titular antes de revocarla: si no, nadie podría administrar Platform.",
    );
    return;
  }
  db.platform.splice(db.platform.indexOf(target), 1);
  audit(ctx.user.email, "platform.admin.revoke", targetUser.email, "Acceso a Platform revocado", "ok");
  res.status(204).end();
});

/** Invitación bootstrap: recuperación neutral, no revela identidades ni deja elegir email. */
app.post("/api/platform/bootstrap/recover", (_req, res) => {
  audit("—", "platform.bootstrap.recover", "—", "Pedido de recuperación bootstrap (respuesta neutral)", "ok");
  res.status(202).json({
    message:
      "Si el enlace correspondía a una invitación vigente, quien administra Platform va a recibir el aviso. No informamos si existe o no una identidad asociada.",
  });
});

app.post("/api/platform/invitations/:token/accept", requireSession, (req, res) => {
  const { user, session } = req as AuthedRequest;
  const invitation = findInvitationByToken(param(req, "token"));
  if (!invitation || invitation.scope !== "platform") {
    fail(res, 404, "invalid_invitation", "El enlace de invitación no es válido.");
    return;
  }
  if (user.email !== invitation.email) {
    fail(res, 403, "other_identity", `La invitación es para ${invitation.email}. Cerrá sesión e ingresá con esa identidad.`);
    return;
  }
  const status = effectiveStatus(invitation);
  if (status === "expired") {
    fail(res, 409, "expired_invitation", "La invitación bootstrap venció. Pedí una nueva desde la pantalla de recuperación.");
    return;
  }
  if (status === "cancelled") {
    fail(res, 409, "cancelled_invitation", "La invitación fue cancelada.");
    return;
  }
  invitation.status = "accepted";
  if (!platformOf(user.id)) db.platform.push({ userId: user.id, role: "admin", activatedAt: null });
  audit(user.email, "platform.admin.accept", user.email, "Invitación Platform aceptada, falta MFA", "ok");
  res.json({ me: meDto(user, session) });
});

// --- panel de demostración -------------------------------------------------

app.get("/api/dev/scenarios", (_req, res) => {
  res.json({ scenarios });
});

app.post("/api/dev/scenario", (req, res) => {
  const id = String((req.body ?? {}).id ?? "");
  const def = findScenario(id);
  if (!def) {
    fail(res, 404, "unknown_scenario", "Ese escenario no existe.");
    return;
  }
  const session = applyScenario(def);
  if (session) res.cookie(SESSION_COOKIE, session.id, { httpOnly: true, sameSite: "lax", path: "/" });
  else res.clearCookie(SESSION_COOKIE, { path: "/" });
  res.json({ id: def.id, start: def.start, hint: def.hint, name: def.name });
});

app.post("/api/dev/reset", (_req, res) => {
  resetAll();
  res.clearCookie(SESSION_COOKIE, { path: "/" });
  res.status(204).end();
});

app.get("/api/dev/mails", (_req, res) => {
  res.json({ mails: db.mails });
});

app.post("/api/dev/fail-next", (req, res) => {
  const mode = (req.body ?? {}).mode ?? null;
  db.failNext = mode === "network" || mode === "conflict" || mode === "notfound" ? mode : null;
  res.json({ failNext: db.failNext });
});

app.post("/api/dev/revoke-session", (req, res) => {
  const sid = readCookie(req, SESSION_COOKIE);
  const session = sid ? db.sessions.find((s) => s.id === sid) : undefined;
  if (session) session.revoked = true;
  res.status(204).end();
});

app.post("/api/dev/expire-step-up", (req, res) => {
  const sid = readCookie(req, SESSION_COOKIE);
  const session = sid ? db.sessions.find((s) => s.id === sid) : undefined;
  if (session) session.stepUpAt = new Date(Date.now() - 60 * 60_000).toISOString();
  res.status(204).end();
});

app.use((_req, res) => {
  fail(res, 404, "not_found", "No existe ese recurso.");
});

app.listen(PORT, () => {
  console.log(`API en http://localhost:${PORT}`);
});
