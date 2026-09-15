import { randomBytes, randomUUID } from "node:crypto";
import type { OrgRole } from "./permissions.js";
import type { CodeState } from "./signup.js";

// Todo el estado del mockup vive acá, en memoria. Se pierde al reiniciar el
// proceso y se puede reiniciar desde el panel de demostración.

export interface Mfa {
  enrolled: boolean;
  secret: string | null;
  recoveryCodesSaved: boolean;
  /** Última verificación simulada. Alimenta el step-up. */
  verifiedAt: string | null;
}

/**
 * Los estados que puede tener una cuenta. Es un conjunto cerrado: "active" es el
 * único que puede ingresar, "closed" es terminal y no tiene vuelta, y cada uno de
 * los otros dice quién lo produjo y cómo se sale.
 */
export type AccountStatus =
  | "pending_confirmation"
  | "active"
  | "self_deactivated"
  | "administratively_suspended"
  | "closed";

/** Razones de suspensión operativa. Conjunto cerrado: queda en auditoría, nunca en la cuenta. */
export type SuspensionReason = "PolicyViolation" | "SecurityIncident" | "BillingHold" | "OperatorRequest";

export const SUSPENSION_REASONS: SuspensionReason[] = [
  "PolicyViolation",
  "SecurityIncident",
  "BillingHold",
  "OperatorRequest",
];

export interface User {
  id: string;
  email: string;
  /** Null cuando la cuenta entra sólo con Google: nadie eligió una contraseña, así que no se inventa. */
  password: string | null;
  /** La cuenta de Google vinculada. Un email igual no alcanza para vincular: tiene que ser esta. */
  googleSubject: string | null;
  name: string;
  confirmed: boolean;
  accountStatus: AccountStatus;
  /** Dónde estaba la cuenta cuando la suspensión la interrumpió. Null si nunca se suspendió. */
  statusBeforeSuspension: AccountStatus | null;
  /** Borrado ya ejecutado: una retención sobre una lápida sería retroactiva. */
  purgedAt: string | null;
  lastSeenAt: string | null;
  mfa: Mfa;
  createdAt: string;
}

export type OrgStatus = "active" | "suspended";

/**
 * Los dos contextos que puede tener una identidad. "persona" es la cuenta
 * personal, identificada por DNI y a lo sumo una por identidad; "empresa" es una
 * organización con CUIT, sea de una persona humana o jurídica.
 */
export type OrgType = "persona" | "empresa";

export interface Org {
  id: string;
  type: OrgType;
  name: string;
  /** 11 dígitos normalizados. Sólo en una empresa. */
  cuit: string | null;
  /** 7 u 8 dígitos. Sólo en una cuenta personal; nunca sale entero hacia la pantalla. */
  dni: string | null;
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

export type MailKind = "registro" | "confirmacion" | "codigo" | "invitacion" | "invitacion-platform" | "aviso";

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

/**
 * Un código mandado a una dirección para crear cuenta. Se crea igual exista o no
 * una cuenta con ese email; qué cuenta hay detrás se dice recién cuando el código
 * prueba que la dirección es de quien lo escribe.
 */
export interface EmailChallenge extends CodeState {
  id: string;
  email: string;
  type: OrgType;
  resendAvailableAt: string;
  verifiedAt: string | null;
  createdAt: string;
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

/** Una regla de la política de retención, tal como se lee. */
export interface RetentionCategoryRule {
  category: string;
  retentionPeriod: string;
  trigger: string;
  action: string;
  evidenceRequired: boolean;
}

/**
 * La política de retención configurada en el despliegue. Puede no existir: un
 * despliegue sin política es uno que no va a borrar nada, y eso se dice.
 */
export interface RetentionPolicyDoc {
  policyId: string;
  version: string;
  owner: string;
  approvedOn: string;
  source: string;
  categories: RetentionCategoryRule[];
}

/**
 * Una retención legal. Detiene el borrado de una identidad y no hace nada más:
 * no cambia el estado de la cuenta, ni sus sesiones, ni sus permisos.
 */
export interface RetentionHold {
  id: string;
  subjectUserId: string;
  reasonCode: string;
  reference: string;
  placedAt: string;
  placedByEmail: string;
  releasedAt: string | null;
  version: number;
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
  challenges: EmailChallenge[];
  audit: AuditEntry[];
  attempts: LoginAttempts[];
  failNext: FailMode | null;
  /** Null = despliegue sin política configurada. */
  retentionPolicy: RetentionPolicyDoc | null;
  retentionHolds: RetentionHold[];
  personalDataMode: string;
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
  challenges: [],
  audit: [],
  attempts: [],
  failNext: null,
  retentionPolicy: null,
  retentionHolds: [],
  personalDataMode: "Restricted",
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

/**
 * La política que trae el mockup. Es la misma forma que lee la pantalla real:
 * quién la aprobó, de dónde sale, y qué se hace con cada categoría de datos.
 */
export const DEFAULT_RETENTION_POLICY = (): RetentionPolicyDoc => ({
  policyId: "retencion-identidad-2026",
  version: "3",
  owner: "Comité de Privacidad",
  approvedOn: "2026-02-10",
  source: "visual-prototype",
  categories: [
    {
      category: "AuditEvents",
      retentionPeriod: "P5Y",
      trigger: "OccurredAt",
      action: "Anonymize",
      evidenceRequired: true,
    },
    {
      category: "SessionRecords",
      retentionPeriod: "P90D",
      trigger: "EndedAt",
      action: "Erase",
      evidenceRequired: false,
    },
    {
      category: "IdentityDocuments",
      retentionPeriod: "P7Y",
      trigger: "AccountClosed",
      action: "Erase",
      evidenceRequired: true,
    },
    {
      category: "InvitationRecords",
      retentionPeriod: "P1Y",
      trigger: "ExpiredAt",
      action: "Erase",
      evidenceRequired: false,
    },
  ],
});

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
  const confirmed = options.confirmed ?? true;
  return {
    id,
    email,
    password: "1234",
    googleSubject: null,
    name,
    confirmed,
    // El estado de cuenta y la confirmación del correo dicen lo mismo mientras
    // nadie los separe: una cuenta sin confirmar está pendiente, no activa.
    accountStatus: confirmed ? "active" : "pending_confirmation",
    statusBeforeSuspension: null,
    purgedAt: null,
    lastSeenAt: null,
    mfa: noMfa(),
    createdAt: now(),
    ...options,
  };
}

function org(id: string, name: string, cuit: string | null, type: OrgType, options: Partial<Org> = {}): Org {
  return { id, type, name, cuit, dni: null, status: "active", suspendedReason: null, version: 1, createdAt: now(), ...options };
}

/** La identidad de Google simulada: el "sub" de verdad es opaco, acá alcanza con derivarlo del email. */
export const googleSubjectFor = (email: string) => `google:${email.trim().toLowerCase()}`;

export function seed(): void {
  db.users.length = 0;
  db.orgs.length = 0;
  db.memberships.length = 0;
  db.platform.length = 0;
  db.invitations.length = 0;
  db.sessions.length = 0;
  db.mails.length = 0;
  db.confirmTokens.length = 0;
  db.challenges.length = 0;
  db.audit.length = 0;
  db.attempts.length = 0;
  db.failNext = null;
  db.retentionHolds.length = 0;
  db.retentionPolicy = DEFAULT_RETENTION_POLICY();
  db.personalDataMode = "Restricted";

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
    // Entra sólo con Google: sirve para ver qué pasa cuando alguien crea cuenta
    // con el email de una cuenta que no tiene contraseña.
    user("u-ana", "ana@gmail.com", "Ana", { password: null, googleSubject: googleSubjectFor("ana@gmail.com") }),
    // Material del ciclo de vida de cuentas: cada una entra en una transición distinta.
    user("u-lucia", "lucia@acme.com", "Lucía Ferrer", { lastSeenAt: minutesAgo(90) }),
    user("u-tomas", "tomas@sur.com", "Tomás Vega", {
      accountStatus: "administratively_suspended",
      statusBeforeSuspension: "active",
      lastSeenAt: minutesAgo(60 * 24 * 6),
    }),
    // Se dio de baja sola y después la suspendieron: levantar la suspensión la
    // devuelve a donde ella la dejó, no a activa.
    user("u-vera", "vera@cuyo.com", "Vera Costa", {
      accountStatus: "administratively_suspended",
      statusBeforeSuspension: "self_deactivated",
      lastSeenAt: minutesAgo(60 * 24 * 20),
    }),
    user("u-hugo", "hugo@valle.com", "Hugo Peña", {
      accountStatus: "self_deactivated",
      lastSeenAt: minutesAgo(60 * 24 * 45),
    }),
    // Lápida: borrado ya ejecutado. No admite transiciones ni retenciones.
    user("u-cerrada", "cerrada@ejemplo.com", "Cuenta cerrada", {
      accountStatus: "closed",
      purgedAt: minutesAgo(60 * 24 * 120),
    }),
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
    // El DNI 30111222 ya está registrado: alimenta el conflicto al crear una cuenta personal.
    org("org-ana", "Ana Martínez", null, "persona", { dni: "30111222" }),
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
    { userId: "u-lucia", orgId: "org-acme", role: "member" },
    { userId: "u-tomas", orgId: "org-sur", role: "member" },
    { userId: "u-vera", orgId: "org-cuyo", role: "member" },
    { userId: "u-ana", orgId: "org-ana", role: "owner" },
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

  // --- retención ------------------------------------------------------------
  // Una retención en pie: alcanza para que el contador de la política no sea cero
  // y para que un segundo pedido con la misma razón choque.
  db.retentionHolds.push({
    id: "hold-litigio",
    subjectUserId: "u-hugo",
    reasonCode: "LITIGATION",
    reference: "CASO-2026-014",
    placedAt: minutesAgo(60 * 24 * 12),
    placedByEmail: "carla@plataforma.com",
    releasedAt: null,
    version: 1,
  });

  audit("carla@plataforma.com", "platform.admin.invite", "elena@plataforma.com", "Invitación bootstrap emitida", "ok");
  audit("carla@plataforma.com", "org.suspend", "Norte Servicios SRL", "Falta de documentación impositiva.", "ok");
  audit("diego@plataforma.com", "platform.login", "diego@plataforma.com", "Ingreso con MFA simulada", "ok");
}

// --- consultas ------------------------------------------------------------

export const findUserByEmail = (email: string) => db.users.find((u) => u.email === email.trim().toLowerCase());
export const findUserById = (id: string) => db.users.find((u) => u.id === id);
export const findOrgByCuit = (cuit: string) => db.orgs.find((o) => o.cuit === cuit);
export const findOrgByDni = (dni: string) => db.orgs.find((o) => o.dni === dni);
export const findUserByGoogleSubject = (subject: string) => db.users.find((u) => u.googleSubject === subject);
export const findChallenge = (id: string) => db.challenges.find((c) => c.id === id);
export const personalContextOf = (userId: string) =>
  membershipsOf(userId)
    .map((m) => orgById(m.orgId))
    .find((o) => o?.type === "persona");
export const orgById = (id: string | null) => (id ? db.orgs.find((o) => o.id === id) : undefined);
export const membershipsOf = (userId: string) => db.memberships.filter((m) => m.userId === userId);
export const membersOf = (orgId: string) => db.memberships.filter((m) => m.orgId === orgId);
export const membershipIn = (userId: string, orgId: string) =>
  db.memberships.find((m) => m.userId === userId && m.orgId === orgId);
export const findSession = (id: string) => db.sessions.find((s) => s.id === id && !s.revoked);
export const platformOf = (userId: string) => db.platform.find((p) => p.userId === userId);
export const findInvitationByToken = (token: string) => db.invitations.find((i) => i.token === token);
export const activeHolds = () => db.retentionHolds.filter((h) => h.releasedAt === null);
export const findHold = (id: string) => db.retentionHolds.find((h) => h.id === id);
/** Una identidad no puede tener dos retenciones en pie por la misma razón. */
export const standingHold = (subjectUserId: string, reasonCode: string) =>
  db.retentionHolds.find((h) => h.subjectUserId === subjectUserId && h.reasonCode === reasonCode && h.releasedAt === null);

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
  const owner = findUserById(userId);
  if (owner) owner.lastSeenAt = session.createdAt;
  return session;
}

/**
 * Detener una cuenta termina todas sus sesiones. Reactivarla no las devuelve: si
 * volvieran, una suspensión sería algo que se puede esperar sentado.
 */
export function revokeSessionsOf(userId: string): number {
  const affected = db.sessions.filter((s) => s.userId === userId && !s.revoked);
  for (const session of affected) session.revoked = true;
  return affected.length;
}

/** Al ingresar: si hay una sola membresía se entra directo a ese contexto. */
export function defaultOrgFor(userId: string): string | null {
  const memberships = membershipsOf(userId);
  return memberships.length === 1 ? memberships[0].orgId : null;
}
