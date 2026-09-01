import express, { type NextFunction, type Request, type Response } from "express";
import cookieParser from "cookie-parser";
import { randomUUID } from "node:crypto";
import { formatCuit, validateCuit } from "./cuit.js";
import {
  createAccount,
  findUserByCuit,
  findUserByEmail,
  isoIn,
  issueConfirmation,
  membershipsOf,
  newToken,
  normalizeEmail,
  nowIso,
  resetStore,
  sendMail,
  store,
  verifyPassword,
  type Session,
  type Tenant,
  type User,
} from "./store.js";

const PORT = Number(process.env.PORT ?? 4000);
const WEB_ORIGIN = process.env.WEB_ORIGIN ?? "http://localhost:5173";
const SESSION_COOKIE = "sid";
const SESSION_TTL_MS = 12 * 60 * 60 * 1000;

const app = express();
app.use(express.json());
app.use(cookieParser());

// --- Problem Details (RFC 9457) -------------------------------------------

interface Problem {
  status: number;
  code: string;
  title: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

function problem(res: Response, p: Problem): void {
  res
    .status(p.status)
    .type("application/problem+json")
    .json({
      type: "about:blank",
      title: p.title,
      status: p.status,
      code: p.code,
      detail: p.detail,
      errors: p.errors,
      traceId: randomUUID(),
    });
}

// --- Session helpers --------------------------------------------------------

function currentSession(req: Request): Session | null {
  const sid = req.cookies?.[SESSION_COOKIE];
  if (!sid) return null;
  const session = store.sessions.get(sid);
  if (!session) return null;
  if (new Date(session.expiresAt).getTime() < Date.now()) {
    store.sessions.delete(sid);
    return null;
  }
  return session;
}

function requireSession(req: Request, res: Response, next: NextFunction): void {
  const session = currentSession(req);
  if (!session) {
    problem(res, { status: 401, code: "unauthenticated", title: "No hay una sesión activa" });
    return;
  }
  (req as Request & { session: Session }).session = session;
  next();
}

function sessionOf(req: Request): Session {
  return (req as Request & { session: Session }).session;
}

function tenantDto(t: Tenant, roleName: string) {
  return { id: t.id, type: t.type, name: t.name, cuit: formatCuit(t.cuit), role: roleName };
}

function contextDto(user: User, session: Session) {
  const memberships = membershipsOf(user.id);
  const available = memberships
    .map((m) => ({ m, t: store.tenants.get(m.tenantId)! }))
    .sort((a, b) => (a.t.type === b.t.type ? a.t.name.localeCompare(b.t.name) : a.t.type === "Personal" ? -1 : 1));
  const active = available.find((x) => x.t.id === session.activeTenantId) ?? null;
  return {
    user: {
      id: user.id,
      displayName: user.displayName,
      email: user.email,
      cuit: formatCuit(user.cuit),
      cuitKind: user.cuitKind,
      emailConfirmed: user.emailConfirmed,
    },
    activeTenant: active ? tenantDto(active.t, active.m.roleName) : null,
    availableTenants: available.map((x) => tenantDto(x.t, x.m.roleName)),
    permissions: active ? active.m.permissions : [],
    session: { expiresAt: session.expiresAt, requiresTwoFactor: false },
  };
}

function confirmationUrl(token: string): string {
  return `${WEB_ORIGIN}/confirmar?token=${encodeURIComponent(token)}`;
}

function sendConfirmationMail(user: User): void {
  const record = issueConfirmation(user);
  sendMail({
    to: user.email,
    subject: "Confirmá tu correo para empezar a usar Base",
    preview: `Hola, ${user.displayName.split(" ")[0]}. Para activar tu cuenta necesitamos que confirmes este correo.`,
    kind: "confirm",
    actionUrl: confirmationUrl(record.token),
  });
}

// --- Identity -------------------------------------------------------------

app.post("/api/identity/register", (req, res) => {
  const body = (req.body ?? {}) as Record<string, unknown>;
  const cuitRaw = String(body.cuit ?? "");
  const displayName = String(body.displayName ?? "").trim();
  const email = String(body.email ?? "").trim();
  const password = String(body.password ?? "");

  const errors: Record<string, string[]> = {};
  const cuit = validateCuit(cuitRaw);
  if (!cuit.ok) {
    errors.cuit = [
      cuit.reason === "length"
        ? "El CUIT tiene que tener 11 dígitos."
        : cuit.reason === "prefix"
          ? "No reconocemos ese tipo de CUIT."
          : "El CUIT no es válido. Revisá los números.",
    ];
  }
  if (displayName.length < 3) {
    errors.displayName = ["Ingresá un nombre de al menos 3 caracteres."];
  }
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
    errors.email = ["Ingresá un correo válido."];
  }
  if (password.length < 8) {
    errors.password = ["La contraseña tiene que tener al menos 8 caracteres."];
  }
  if (cuit.ok && findUserByCuit(cuit.digits)) {
    const existing = findUserByCuit(cuit.digits)!;
    if (existing.email !== normalizeEmail(email)) {
      errors.cuit = ["Ese CUIT ya está registrado con otro correo."];
    }
  }
  if (Object.keys(errors).length > 0) {
    problem(res, {
      status: 400,
      code: "validation_failed",
      title: "Revisá los datos ingresados",
      errors,
    });
    return;
  }
  if (!cuit.ok) return;

  const existing = findUserByEmail(email);
  if (existing) {
    // Neutral response: no reveal of whether the email exists.
    sendMail({
      to: existing.email,
      subject: "Ya tenés una cuenta en Base",
      preview: "Alguien intentó registrarse con este correo. Si fuiste vos, iniciá sesión con tu contraseña.",
      kind: "already_registered",
      actionUrl: `${WEB_ORIGIN}/iniciar-sesion`,
    });
    res.status(202).end();
    return;
  }

  const user = createAccount({ cuit: cuit.digits, cuitKind: cuit.kind, displayName, email, password });
  sendConfirmationMail(user);
  res.status(202).end();
});

app.post("/api/identity/resend-confirmation", (req, res) => {
  const email = String((req.body ?? {}).email ?? "");
  const user = findUserByEmail(email);
  if (user && !user.emailConfirmed) sendConfirmationMail(user);
  res.status(202).end();
});

app.post("/api/identity/confirm-email", (req, res) => {
  const token = String((req.body ?? {}).token ?? "");
  const record = store.confirmations.get(token);
  if (!record) {
    problem(res, { status: 400, code: "invalid_token", title: "El enlace no es válido" });
    return;
  }
  const user = store.users.get(record.userId);
  if (!user) {
    problem(res, { status: 400, code: "invalid_token", title: "El enlace no es válido" });
    return;
  }
  if (record.usedAt && user.emailConfirmed) {
    // Idempotent: already confirmed with this token.
    res.status(204).end();
    return;
  }
  if (record.usedAt || new Date(record.expiresAt).getTime() < Date.now()) {
    problem(res, { status: 400, code: "expired_token", title: "El enlace venció" });
    return;
  }
  record.usedAt = nowIso();
  user.emailConfirmed = true;
  for (const m of membershipsOf(user.id)) {
    const t = store.tenants.get(m.tenantId);
    if (t) t.state = "Active";
  }
  sendMail({
    to: user.email,
    subject: "Tu cuenta ya está activa",
    preview: "Gracias por confirmar tu correo. Ya podés iniciar sesión y configurar tu espacio.",
    kind: "welcome",
    actionUrl: `${WEB_ORIGIN}/iniciar-sesion`,
  });
  res.status(204).end();
});

app.post("/api/identity/sessions", (req, res) => {
  const body = (req.body ?? {}) as Record<string, unknown>;
  const email = String(body.email ?? "");
  const password = String(body.password ?? "");
  const user = findUserByEmail(email);
  if (!user || !verifyPassword(password, user.passwordHash)) {
    problem(res, {
      status: 401,
      code: "invalid_credentials",
      title: "El correo o la contraseña no coinciden",
    });
    return;
  }
  if (!user.emailConfirmed) {
    problem(res, {
      status: 403,
      code: "email_not_confirmed",
      title: "Todavía no confirmaste tu correo",
      detail: "Revisá tu bandeja de entrada. Si no encontrás el correo, podés pedir uno nuevo.",
    });
    return;
  }
  const memberships = membershipsOf(user.id);
  const session: Session = {
    id: newToken(),
    userId: user.id,
    activeTenantId: memberships.length === 1 ? memberships[0].tenantId : null,
    createdAt: nowIso(),
    expiresAt: isoIn(SESSION_TTL_MS),
  };
  store.sessions.set(session.id, session);
  res.cookie(SESSION_COOKIE, session.id, {
    httpOnly: true,
    sameSite: "lax",
    path: "/",
    maxAge: SESSION_TTL_MS,
  });
  res.status(204).end();
});

app.delete("/api/identity/sessions/current", requireSession, (req, res) => {
  store.sessions.delete(sessionOf(req).id);
  res.clearCookie(SESSION_COOKIE, { path: "/" });
  res.status(204).end();
});

app.get("/api/identity/context", requireSession, (req, res) => {
  const session = sessionOf(req);
  const user = store.users.get(session.userId);
  if (!user) {
    problem(res, { status: 401, code: "unauthenticated", title: "No hay una sesión activa" });
    return;
  }
  res.json(contextDto(user, session));
});

app.put("/api/identity/context/tenant", requireSession, (req, res) => {
  const session = sessionOf(req);
  const user = store.users.get(session.userId)!;
  const tenantId = String((req.body ?? {}).tenantId ?? "");
  const membership = membershipsOf(user.id).find((m) => m.tenantId === tenantId);
  if (!membership) {
    problem(res, { status: 404, code: "tenant_not_found", title: "No encontramos ese espacio" });
    return;
  }
  session.activeTenantId = tenantId;
  res.json(contextDto(user, session));
});

// --- Demo-only helpers ----------------------------------------------------

app.get("/api/demo/mailbox", (_req, res) => {
  res.json({ messages: store.mailbox });
});

app.post("/api/demo/reset", (_req, res) => {
  resetStore();
  res.clearCookie(SESSION_COOKIE, { path: "/" });
  res.status(204).end();
});

app.use((_req, res) => {
  problem(res, { status: 404, code: "not_found", title: "Recurso no encontrado" });
});

app.listen(PORT, () => {
  console.log(`API de ejemplo escuchando en http://localhost:${PORT}`);
});
