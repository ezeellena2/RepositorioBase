import express from 'express';
import cookieParser from 'cookie-parser';
import { randomUUID } from 'node:crypto';
import { normalize, kind, isValid, format } from './cuit.js';

const app = express();
app.use(express.json());
app.use(cookieParser());

const PORT = 3001;
const WEB = 'http://localhost:5173';
const DAY = 24 * 60 * 60 * 1000;

/* ── datos en memoria ─────────────────────────── */
const db = { users: [], tenants: [], memberships: [], sessions: [], tokens: [], mails: [] };

function seed() {
  db.users.length = 0; db.tenants.length = 0; db.memberships.length = 0;
  db.sessions.length = 0; db.tokens.length = 0; db.mails.length = 0;

  const juan = { id: 'u1', email: 'juan@acme.com', password: '1234', name: 'Juan Pérez', confirmed: true };
  const maria = { id: 'u2', email: 'maria@acme.com', password: '1234', name: 'María Gómez', confirmed: true };
  const nuevo = { id: 'u3', email: 'nuevo@sur.com', password: '1234', name: 'Ana Soto', confirmed: false };
  db.users.push(juan, maria, nuevo);

  const acme = { id: 't1', type: 'empresa', name: 'Acme S.A.', cuit: '30712345671' };
  const juanP = { id: 't2', type: 'persona', name: 'Juan Pérez', cuit: '20334445551' };
  const sur = { id: 't3', type: 'empresa', name: 'Distribuidora Sur SRL', cuit: '30709988774' };
  db.tenants.push(acme, juanP, sur);

  db.memberships.push(
    { userId: 'u1', tenantId: 't1', role: 'owner' },
    { userId: 'u1', tenantId: 't2', role: 'owner' },
    { userId: 'u2', tenantId: 't1', role: 'member' },
    { userId: 'u3', tenantId: 't3', role: 'owner' }
  );
}
seed();

const fail = (res, status, code, message) => res.status(status).json({ error: { code, message } });
const userOf = (req) => {
  const s = db.sessions.find((x) => x.id === req.cookies?.sid);
  return s ? { session: s, user: db.users.find((u) => u.id === s.userId) } : null;
};
const tenantsOf = (userId) =>
  db.memberships.filter((m) => m.userId === userId).map((m) => db.tenants.find((t) => t.id === m.tenantId));

function contextDto(session, user) {
  const list = tenantsOf(user.id);
  const active = session.activeTenantId ? db.tenants.find((t) => t.id === session.activeTenantId) : null;
  return {
    user: { id: user.id, name: user.name, email: user.email },
    activeTenant: active ? { ...active, cuit: format(active.cuit) } : null,
    tenants: list.map((t) => ({ id: t.id, type: t.type, name: t.name }))
  };
}

/* ── endpoints ────────────────────────────────── */
app.get('/api/health', (_req, res) => res.json({ ok: true }));

app.post('/api/auth/register', (req, res) => {
  const { cuit, name, email, password } = req.body ?? {};
  const n = normalize(cuit);
  if (!n || !isValid(n)) return fail(res, 400, 'invalid_cuit', 'El CUIT no es válido.');
  if (!name?.trim()) return fail(res, 400, 'invalid_name', 'Falta el nombre.');
  if (!email?.includes('@')) return fail(res, 400, 'invalid_email', 'El correo no es válido.');
  if (!password || password.length < 12) return fail(res, 400, 'weak_password', 'La contraseña necesita 12 caracteres o más.');

  if (db.tenants.some((t) => t.cuit === n)) return fail(res, 409, 'cuit_taken', 'Ese CUIT ya está registrado.');

  const normalizedEmail = email.trim().toLowerCase();
  if (db.users.some((u) => u.email === normalizedEmail)) {
    db.mails.unshift({
      to: normalizedEmail, subject: 'Ya tenés una cuenta',
      body: 'Alguien intentó registrarse con este correo. Si fuiste vos, iniciá sesión.',
      link: `${WEB}/login`, sentAt: new Date().toISOString()
    });
    return res.status(202).end();
  }

  const user = { id: randomUUID(), email: normalizedEmail, password, name: name.trim(), confirmed: false };
  const tenant = { id: randomUUID(), type: kind(n), name: name.trim(), cuit: n };
  db.users.push(user);
  db.tenants.push(tenant);
  db.memberships.push({ userId: user.id, tenantId: tenant.id, role: 'owner' });

  const token = randomUUID().replace(/-/g, '').slice(0, 24);
  db.tokens.push({ token, userId: user.id, expiresAt: Date.now() + DAY, usedAt: null });
  db.mails.unshift({
    to: normalizedEmail, subject: 'Confirmá tu correo',
    body: 'Abrí este enlace para confirmar tu cuenta. Vence en 24 horas.',
    link: `${WEB}/confirmar?token=${token}`, sentAt: new Date().toISOString()
  });
  res.status(202).end();
});

app.post('/api/auth/confirm', (req, res) => {
  const { token } = req.body ?? {};
  const t = db.tokens.find((x) => x.token === token);
  if (!t) return fail(res, 400, 'invalid_token', 'El enlace no es válido.');
  if (t.usedAt) return res.status(204).end();
  if (Date.now() > t.expiresAt) return fail(res, 400, 'invalid_token', 'El enlace venció.');
  t.usedAt = Date.now();
  const u = db.users.find((x) => x.id === t.userId);
  if (u) u.confirmed = true;
  res.status(204).end();
});

app.post('/api/auth/login', (req, res) => {
  const { email, password } = req.body ?? {};
  const u = db.users.find((x) => x.email === String(email ?? '').trim().toLowerCase());
  if (!u || u.password !== password || !u.confirmed)
    return fail(res, 401, 'invalid_credentials', 'El correo o la contraseña no son correctos.');

  const list = tenantsOf(u.id);
  const session = { id: randomUUID(), userId: u.id, activeTenantId: list.length === 1 ? list[0].id : null };
  db.sessions.push(session);
  res.cookie('sid', session.id, { httpOnly: true, sameSite: 'lax', path: '/' });
  res.status(204).end();
});

app.post('/api/auth/logout', (req, res) => {
  const i = db.sessions.findIndex((s) => s.id === req.cookies?.sid);
  if (i >= 0) db.sessions.splice(i, 1);
  res.clearCookie('sid', { path: '/' });
  res.status(204).end();
});

app.get('/api/me', (req, res) => {
  const ctx = userOf(req);
  if (!ctx) return fail(res, 401, 'unauthenticated', 'Iniciá sesión para continuar.');
  res.json(contextDto(ctx.session, ctx.user));
});

app.put('/api/me/tenant', (req, res) => {
  const ctx = userOf(req);
  if (!ctx) return fail(res, 401, 'unauthenticated', 'Iniciá sesión para continuar.');
  const { tenantId } = req.body ?? {};
  const isMember = db.memberships.some((m) => m.userId === ctx.user.id && m.tenantId === tenantId);
  if (!isMember) return fail(res, 403, 'forbidden', 'No pertenecés a esa organización.');
  ctx.session.activeTenantId = tenantId;
  res.json(contextDto(ctx.session, ctx.user));
});

app.get('/api/dev/mails', (_req, res) => res.json(db.mails));
app.post('/api/dev/reset', (_req, res) => { seed(); res.status(204).end(); });

app.listen(PORT, () => console.log(`api  →  http://localhost:${PORT}`));
