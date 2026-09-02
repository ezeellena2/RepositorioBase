import express, { type NextFunction, type Request, type Response } from "express";
import { format, isValid, kind, normalize } from "./cuit.js";
import {
  db,
  degradedOf,
  findSession,
  findTenantByCuit,
  findUserByEmail,
  membershipsOf,
  newId,
  newToken,
  now,
  pushMail,
  seed,
  tenantById,
  PERMISSIONS_BY_ROLE,
  type Membership,
  type Session,
  type Tenant,
  type User,
} from "./store.js";

const PORT = 3001;
const WEB_ORIGIN = "http://localhost:5173";
const SESSION_COOKIE = "sid";
const TOKEN_TTL_MS = 24 * 60 * 60 * 1000;

seed();

const app = express();
app.use(express.json());

// --- helpers ---------------------------------------------------------------

function fail(res: Response, status: number, code: string, message: string): void {
  res.status(status).json({ error: { code, message } });
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
  const user = session ? db.users.find((u) => u.id === session.userId) : undefined;
  if (!session || !user) {
    fail(res, 401, "unauthenticated", "Necesitás iniciar sesión.");
    return;
  }
  (req as AuthedRequest).session = session;
  (req as AuthedRequest).user = user;
  next();
}

function tenantBase(t: Tenant, m: Membership) {
  return {
    id: t.id,
    name: t.name,
    scope: t.scope,
    type: t.type,
    cuit: t.cuit ? format(t.cuit) : null,
    role: m.role,
  };
}

function meDto(user: User, session: Session) {
  const rows = membershipsOf(user.id)
    .map((m) => ({ m, t: tenantById(m.tenantId) }))
    .filter((x): x is { m: Membership; t: Tenant } => Boolean(x.t));
  const active = rows.find((x) => x.t.id === session.activeTenantId) ?? null;
  return {
    user: { id: user.id, name: user.name, email: user.email, mfa: user.mfa },
    activeTenant: active
      ? {
          ...tenantBase(active.t, active.m),
          permissions: PERMISSIONS_BY_ROLE[active.m.role],
          degraded: degradedOf(active.t.id),
        }
      : null,
    tenants: rows.map((x) => ({ ...tenantBase(x.t, x.m), hasDegraded: degradedOf(x.t.id).length > 0 })),
  };
}

function sendConfirmationMail(user: User): void {
  const token = newToken();
  db.confirmTokens.push({
    token,
    userId: user.id,
    expiresAt: new Date(Date.now() + TOKEN_TTL_MS).toISOString(),
    usedAt: null,
  });
  const link = `${WEB_ORIGIN}/confirmar?token=${token}`;
  pushMail({
    to: user.email,
    subject: "Confirmá tu correo",
    body: `Hola, ${user.name}. Para activar tu cuenta confirmá este correo. El enlace vence en 24 horas.`,
    link,
  });
}

// --- health ----------------------------------------------------------------

app.get("/api/health", (_req, res) => {
  res.json({ ok: true });
});

// --- auth ------------------------------------------------------------------

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
  if (findTenantByCuit(cuit)) {
    fail(res, 409, "cuit_taken", "Ese CUIT ya está registrado.");
    return;
  }

  const existing = findUserByEmail(email);
  if (existing) {
    // No revelar que el correo existe: misma respuesta, distinto correo.
    pushMail({
      to: existing.email,
      subject: "Ya tenés cuenta, iniciá sesión",
      body: `Hola, ${existing.name}. Alguien intentó registrarse con este correo. Si fuiste vos, iniciá sesión con tu contraseña.`,
      link: `${WEB_ORIGIN}/iniciar-sesion`,
    });
    res.status(202).end();
    return;
  }

  const user: User = { id: newId(), email, password, name, confirmed: false, mfa: false, createdAt: now() };
  const tenant: Tenant = { id: newId(), scope: "tenant", type, name, cuit };
  db.users.push(user);
  db.tenants.push(tenant);
  db.memberships.push({ userId: user.id, tenantId: tenant.id, role: "owner" });
  sendConfirmationMail(user);
  res.status(202).end();
});

app.post("/api/auth/confirm", (req, res) => {
  const token = String((req.body ?? {}).token ?? "");
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
    fail(res, 400, "invalid_token", "El enlace no es válido o venció.");
    return;
  }
  const user = db.users.find((u) => u.id === record.userId);
  if (!user) {
    fail(res, 400, "invalid_token", "El enlace no es válido o venció.");
    return;
  }
  record.usedAt = now();
  user.confirmed = true;
  res.status(204).end();
});

app.post("/api/auth/login", (req, res) => {
  const body = (req.body ?? {}) as Record<string, unknown>;
  const email = String(body.email ?? "");
  const password = String(body.password ?? "");
  const user = findUserByEmail(email);
  if (!user || user.password !== password || !user.confirmed) {
    fail(res, 401, "invalid_credentials", "El correo o la contraseña no coinciden.");
    return;
  }
  const memberships = membershipsOf(user.id);
  const session: Session = {
    id: newToken(),
    userId: user.id,
    activeTenantId: memberships.length === 1 ? memberships[0].tenantId : null,
    createdAt: now(),
  };
  db.sessions.push(session);
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

// --- me --------------------------------------------------------------------

app.get("/api/me", requireSession, (req, res) => {
  const { user, session } = req as AuthedRequest;
  res.json(meDto(user, session));
});

app.put("/api/me/tenant", requireSession, (req, res) => {
  const { user, session } = req as AuthedRequest;
  const tenantId = String((req.body ?? {}).tenantId ?? "");
  const isMember = membershipsOf(user.id).some((m) => m.tenantId === tenantId);
  if (!isMember) {
    fail(res, 403, "forbidden", "No sos miembro de ese espacio.");
    return;
  }
  session.activeTenantId = tenantId;
  res.json(meDto(user, session));
});

app.get("/api/me/resumen", requireSession, (req, res) => {
  const { user, session } = req as AuthedRequest;
  const tenant = session.activeTenantId ? tenantById(session.activeTenantId) : undefined;
  if (!tenant) {
    fail(res, 404, "no_active_tenant", "Todavía no elegiste con quién operar.");
    return;
  }
  if (tenant.scope === "platform") {
    const p = db.platformResumen.get(tenant.id);
    res.json({ scope: "platform", ...p });
    return;
  }
  const t = db.tenantResumen.get(tenant.id) ?? { modules: [], capabilities: [] };
  const whatsapp = db.whatsappLinks.get(`${user.id}:${tenant.id}`) ?? null;
  res.json({ scope: "tenant", whatsapp, modules: t.modules, capabilities: t.capabilities });
});

// --- dev -------------------------------------------------------------------

app.get("/api/dev/mails", (_req, res) => {
  res.json(db.mails);
});

app.use((_req, res) => {
  fail(res, 404, "not_found", "No existe ese recurso.");
});

app.listen(PORT, () => {
  console.log(`API en http://localhost:${PORT}`);
});
