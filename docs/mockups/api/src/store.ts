import { randomBytes, randomUUID } from "node:crypto";
import type { CuitKind } from "./cuit.js";
import type { OrgRole } from "./permissions.js";

// Todo el estado del mockup vive acá, en memoria. Se pierde al reiniciar el
// proceso y se puede reiniciar desde el panel de demostración.

export interface Mfa {
  enrolled: boolean;
  secret: string | null;
  recoveryCodesSaved: boolean;
  /** Última verificación simulada. Alimenta el step-up. */
  verifiedAt: string | null;
}

export interface User {
  id: string;
  email: string;
  password: string;
  name: string;
  confirmed: boolean;
  mfa: Mfa;
  createdAt: string;
}

export type OrgStatus = "active" | "suspended";

export interface Org {
  id: string;
  type: CuitKind;
  name: string;
  cuit: string; // 11 dígitos normalizados
  status: OrgStatus;
  suspendedReason: string | null;
  /** Se incrementa en cada mutación: sirve para simular conflicto de concurrencia. */
  version: number;
  createdAt: string;
}

export interface Membership {
  userId: string;
  orgId: string;
  role: OrgRole;
}

export interface PlatformMembership {
  userId: string;
  role: "owner" | "admin";
  /** Null hasta completar la secuencia de MFA simulada. */
  activatedAt: string | null;
}

export type InvitationStatus = "pending" | "accepted" | "cancelled" | "expired";

export interface Invitation {
  id: string;
  token: string;
  scope: "org" | "platform";
  orgId: string | null;
  email: string;
  role: OrgRole | "admin";
  status: InvitationStatus;
  expiresAt: string;
  createdAt: string;
  invitedByUserId: string | null;
}

export interface Session {
  id: string;
  userId: string;
  activeOrgId: string | null;
  createdAt: string;
  revoked: boolean;
  /** Última revalidación MFA simulada de esta sesión. */
  stepUpAt: string | null;
}

export type MailKind = "registro" | "confirmacion" | "invitacion" | "invitacion-platform" | "aviso";

export interface Mail {
  id: string;
  to: string;
  subject: string;
  body: string;
  link: string | null;
  kind: MailKind;
  sentAt: string;
}

export interface ConfirmToken {
  token: string;
  userId: string;
  expiresAt: string;
  usedAt: string | null;
}

export interface AuditEntry {
  id: string;
  at: string;
  actorEmail: string;
  action: string;
  target: string;
  detail: string;
  result: "ok" | "denegado";
}

export interface LoginAttempts {
  email: string;
  failures: number;
  blockedUntil: string | null;
}

/** Fallas que el panel de demostración puede armar para el próximo pedido. */
export type FailMode = "network" | "conflict" | "notfound";

export interface Db {
  users: User[];
  orgs: Org[];
  memberships: Membership[];
  platform: PlatformMembership[];
  invitations: Invitation[];
  sessions: Session[];
  mails: Mail[];
  confirmTokens: ConfirmToken[];
  audit: AuditEntry[];
  attempts: LoginAttempts[];
  failNext: FailMode | null;
}

export const db: Db = {
  users: [],
  orgs: [],
  memberships: [],
  platform: [],
  invitations: [],
  sessions: [],
  mails: [],
  confirmTokens: [],
  audit: [],
  attempts: [],
  failNext: null,
};

export const WEB_ORIGIN = "http://localhost:5173";
export const TOKEN_TTL_MS = 24 * 60 * 60 * 1000;
export const STEP_UP_TTL_MS = 5 * 60 * 1000;
export const MAX_LOGIN_FAILURES = 3;
export const LOGIN_BLOCK_MS = 30 * 1000;

/** El "código TOTP" de la simulación. No hay criptografía detrás. */
export const SIMULATED_TOTP_CODE = "123456";
export const SIMULATED_TOTP_SECRET = "MOCK-KPZX-4T7Q-91HD";
export const SIMULATED_RECOVERY_CODES = [
  "4F2K-99DA",
  "7Q1M-30XB",
  "PL8S-24RC",
  "ZT6H-71NV",
  "BW3J-58EY",
  "MK9C-16UF",
];

export const now = () => new Date().toISOString();
export const newId = () => randomUUID();
export const newToken = () => randomBytes(24).toString("base64url");
const inHours = (hours: number) => new Date(Date.now() + hours * 3600_000).toISOString();
const minutesAgo = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

const noMfa = (): Mfa => ({ enrolled: false, secret: null, recoveryCodesSaved: false, verifiedAt: null });
const fullMfa = (verifiedMinutesAgo: number): Mfa => ({
  enrolled: true,
  secret: SIMULATED_TOTP_SECRET,
  recoveryCodesSaved: true,
  verifiedAt: minutesAgo(verifiedMinutesAgo),
});

function user(id: string, email: string, name: string, options: Partial<User> = {}): User {
  return { id, email, password: "1234", name, confirmed: true, mfa: noMfa(), createdAt: now(), ...options };
}

function org(id: string, name: string, cuit: string, type: CuitKind, options: Partial<Org> = {}): Org {
  return { id, type, name, cuit, status: "active", suspendedReason: null, version: 1, createdAt: now(), ...options };
}

export function seed(): void {
  db.users.length = 0;
  db.orgs.length = 0;
  db.memberships.length = 0;
  db.platform.length = 0;
  db.invitations.length = 0;
  db.sessions.length = 0;
  db.mails.length = 0;
  db.confirmTokens.length = 0;
  db.audit.length = 0;
  db.attempts.length = 0;
  db.failNext = null;

  // --- identidades ---------------------------------------------------------
  db.users.push(
    user("u-juan", "juan@acme.com", "Juan Pérez"),
    user("u-maria", "maria@acme.com", "María López"),
    user("u-bruno", "bruno@sur.com", "Bruno Ortiz"),
    user("u-sofia", "sofia@sinorg.com", "Sofía Aguirre"),
    user("u-nuevo", "nuevo@sur.com", "Distribuidora Sur SRL", { confirmed: false }),
    user("u-paula", "paula@nueva.com", "Paula Giménez", { confirmed: false }),
    user("u-carla", "carla@plataforma.com", "Carla Ruiz", { mfa: fullMfa(1) }),
    user("u-diego", "diego@plataforma.com", "Diego Sosa", { mfa: fullMfa(240) }),
    user("u-elena", "elena@plataforma.com", "Elena Vidal"),
  );

  // --- organizaciones ------------------------------------------------------
  // Los CUIT son ficticios pero pasan el dígito verificador: el propio mockup
  // los valida al registrar, así que tienen que ser coherentes.
  db.orgs.push(
    org("org-acme", "Acme S.A.", "30712345671", "empresa"),
    org("org-sur", "Distribuidora Sur SRL", "30709988774", "empresa"),
    org("org-norte", "Norte Servicios SRL", "30711223343", "empresa", {
      status: "suspended",
      suspendedReason: "Falta de documentación impositiva.",
      version: 2,
    }),
    org("org-valle", "Cooperativa Del Valle", "30715556665", "empresa"),
    org("org-litoral", "Litoral Logística SRL", "30712233449", "empresa"),
    org("org-pampa", "Pampa Alimentos S.A.", "30715566776", "empresa", {
      status: "suspended",
      suspendedReason: "Pedido de la propia organización.",
      version: 3,
    }),
    org("org-cuyo", "Cuyo Textil SRL", "30701122336", "empresa"),
  );

  // --- membresías ----------------------------------------------------------
  // Juan: puede invitar en Acme, no puede invitar en Sur. Es el caso central de aislamiento.
  db.memberships.push(
    { userId: "u-juan", orgId: "org-acme", role: "owner" },
    { userId: "u-juan", orgId: "org-sur", role: "member" },
    { userId: "u-maria", orgId: "org-acme", role: "member" },
    { userId: "u-bruno", orgId: "org-sur", role: "owner" },
    { userId: "u-bruno", orgId: "org-norte", role: "owner" },
    { userId: "u-bruno", orgId: "org-litoral", role: "owner" },
    { userId: "u-bruno", orgId: "org-cuyo", role: "admin" },
  );
  // org-valle y org-pampa quedan sin membresías: alimentan el caso
  // "organización sin miembros visibles" del directorio de Platform.

  // --- Platform ------------------------------------------------------------
  db.platform.push(
    { userId: "u-carla", role: "owner", activatedAt: minutesAgo(60 * 24 * 30) },
    { userId: "u-diego", role: "admin", activatedAt: minutesAgo(60 * 24 * 10) },
  );

  // --- invitaciones --------------------------------------------------------
  db.invitations.push(
    {
      id: "inv-1",
      token: "inv-nueva",
      scope: "org",
      orgId: "org-acme",
      email: "nadia@nueva.com",
      role: "member",
      status: "pending",
      expiresAt: inHours(48),
      createdAt: now(),
      invitedByUserId: "u-juan",
    },
    {
      id: "inv-2",
      token: "inv-existente",
      scope: "org",
      orgId: "org-sur",
      email: "maria@acme.com",
      role: "member",
      status: "pending",
      expiresAt: inHours(48),
      createdAt: now(),
      invitedByUserId: "u-bruno",
    },
    {
      id: "inv-3",
      token: "inv-sin-confirmar",
      scope: "org",
      orgId: "org-acme",
      email: "paula@nueva.com",
      role: "member",
      status: "pending",
      expiresAt: inHours(48),
      createdAt: now(),
      invitedByUserId: "u-juan",
    },
    {
      id: "inv-4",
      token: "inv-vencida",
      scope: "org",
      orgId: "org-acme",
      email: "vencida@ejemplo.com",
      role: "member",
      status: "pending",
      expiresAt: minutesAgo(60),
      createdAt: minutesAgo(60 * 72),
      invitedByUserId: "u-juan",
    },
    {
      id: "inv-5",
      token: "inv-cancelada",
      scope: "org",
      orgId: "org-acme",
      email: "cancelada@ejemplo.com",
      role: "member",
      status: "cancelled",
      expiresAt: inHours(48),
      createdAt: now(),
      invitedByUserId: "u-juan",
    },
    {
      id: "inv-6",
      token: "inv-usada",
      scope: "org",
      orgId: "org-acme",
      email: "maria@acme.com",
      role: "member",
      status: "accepted",
      expiresAt: inHours(48),
      createdAt: now(),
      invitedByUserId: "u-juan",
    },
    {
      id: "inv-7",
      token: "inv-platform-vencida",
      scope: "platform",
      orgId: null,
      email: "elena@plataforma.com",
      role: "admin",
      status: "pending",
      expiresAt: minutesAgo(30),
      createdAt: minutesAgo(60 * 72),
      invitedByUserId: "u-carla",
    },
  );

  // --- bandeja simulada ----------------------------------------------------
  pushMail({
    to: "nuevo@sur.com",
    subject: "Confirmá tu correo",
    body: "Hola, Distribuidora Sur SRL. Para activar tu cuenta confirmá este correo. El enlace vence en 24 horas.",
    link: `${WEB_ORIGIN}/confirmar?token=confirm-nuevo`,
    kind: "confirmacion",
  });
  db.confirmTokens.push({ token: "confirm-nuevo", userId: "u-nuevo", expiresAt: inHours(24), usedAt: null });

  pushMail({
    to: "paula@nueva.com",
    subject: "Confirmá tu correo",
    body: "Hola, Paula Giménez. Confirmá este correo para poder aceptar la invitación a Acme S.A.",
    link: `${WEB_ORIGIN}/confirmar?token=confirm-paula`,
    kind: "confirmacion",
  });
  db.confirmTokens.push({ token: "confirm-paula", userId: "u-paula", expiresAt: inHours(24), usedAt: null });

  pushMail({
    to: "nadia@nueva.com",
    subject: "Te invitaron a Acme S.A.",
    body: "Juan Pérez te invitó a sumarte a Acme S.A. como Integrante. Todavía no tenés cuenta: vas a poder crearla desde el enlace.",
    link: `${WEB_ORIGIN}/invitacion?token=inv-nueva`,
    kind: "invitacion",
  });
  pushMail({
    to: "paula@nueva.com",
    subject: "Te invitaron a Acme S.A.",
    body: "Juan Pérez te invitó a sumarte a Acme S.A. como Integrante. Confirmá tu correo antes de aceptar.",
    link: `${WEB_ORIGIN}/invitacion?token=inv-sin-confirmar`,
    kind: "invitacion",
  });
  pushMail({
    to: "maria@acme.com",
    subject: "Te invitaron a Distribuidora Sur SRL",
    body: "Bruno Ortiz te invitó a sumarte a Distribuidora Sur SRL como Integrante. Ya tenés cuenta: ingresá y aceptá.",
    link: `${WEB_ORIGIN}/invitacion?token=inv-existente`,
    kind: "invitacion",
  });
  pushMail({
    to: "vencida@ejemplo.com",
    subject: "Te invitaron a Acme S.A.",
    body: "Esta invitación ya venció. Sirve para ver el estado de invitación vencida.",
    link: `${WEB_ORIGIN}/invitacion?token=inv-vencida`,
    kind: "invitacion",
  });
  pushMail({
    to: "elena@plataforma.com",
    subject: "Invitación para administrar Platform",
    body: "Carla Ruiz te invitó a administrar Platform. Esta invitación bootstrap venció: se puede pedir una nueva desde la pantalla de recuperación.",
    link: `${WEB_ORIGIN}/platform/invitacion?token=inv-platform-vencida`,
    kind: "invitacion-platform",
  });

  audit("carla@plataforma.com", "platform.admin.invite", "elena@plataforma.com", "Invitación bootstrap emitida", "ok");
  audit("carla@plataforma.com", "org.suspend", "Norte Servicios SRL", "Falta de documentación impositiva.", "ok");
  audit("diego@plataforma.com", "platform.login", "diego@plataforma.com", "Ingreso con MFA simulada", "ok");
}

// --- consultas ------------------------------------------------------------

export const findUserByEmail = (email: string) => db.users.find((u) => u.email === email.trim().toLowerCase());
export const findUserById = (id: string) => db.users.find((u) => u.id === id);
export const findOrgByCuit = (cuit: string) => db.orgs.find((o) => o.cuit === cuit);
export const orgById = (id: string | null) => (id ? db.orgs.find((o) => o.id === id) : undefined);
export const membershipsOf = (userId: string) => db.memberships.filter((m) => m.userId === userId);
export const membersOf = (orgId: string) => db.memberships.filter((m) => m.orgId === orgId);
export const membershipIn = (userId: string, orgId: string) =>
  db.memberships.find((m) => m.userId === userId && m.orgId === orgId);
export const findSession = (id: string) => db.sessions.find((s) => s.id === id && !s.revoked);
export const platformOf = (userId: string) => db.platform.find((p) => p.userId === userId);
export const findInvitationByToken = (token: string) => db.invitations.find((i) => i.token === token);

export function pushMail(mail: Omit<Mail, "sentAt" | "id">): void {
  db.mails.unshift({ ...mail, id: newId(), sentAt: now() });
}

export function audit(
  actorEmail: string,
  action: string,
  target: string,
  detail: string,
  result: "ok" | "denegado",
): void {
  db.audit.unshift({ id: newId(), at: now(), actorEmail, action, target, detail, result });
}

/** Vencimiento perezoso: una invitación pendiente con fecha pasada se lee como vencida. */
export function effectiveStatus(invitation: Invitation): InvitationStatus {
  if (invitation.status === "pending" && new Date(invitation.expiresAt).getTime() < Date.now()) return "expired";
  return invitation.status;
}

export function stepUpFresh(session: Session): boolean {
  if (!session.stepUpAt) return false;
  return Date.now() - new Date(session.stepUpAt).getTime() < STEP_UP_TTL_MS;
}

export function createSession(userId: string, activeOrgId: string | null, stepUpAt: string | null = null): Session {
  const session: Session = { id: newToken(), userId, activeOrgId, createdAt: now(), revoked: false, stepUpAt };
  db.sessions.push(session);
  return session;
}

/** Al ingresar: si hay una sola membresía se entra directo a ese contexto. */
export function defaultOrgFor(userId: string): string | null {
  const memberships = membershipsOf(userId);
  return memberships.length === 1 ? memberships[0].orgId : null;
}
