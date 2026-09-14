export interface Health {
  ok: true;
}

export type OrgRole = "owner" | "admin" | "member";
export type OrgStatus = "active" | "suspended";
export type OrgType = "persona" | "empresa";
export type Permission = "members.view" | "members.invite" | "members.manage" | "org.manage";
export type InvitationStatus = "pending" | "accepted" | "cancelled" | "expired";

export interface OrgSummary {
  id: string;
  name: string;
  type: OrgType;
  status: OrgStatus;
  role: OrgRole;
  roleLabel: string;
}

export interface ActiveOrg extends OrgSummary {
  cuit: string;
  suspendedReason: string | null;
  permissions: Permission[];
  version: number;
}

export type PlatformPermission =
  | "platform.identities.read"
  | "platform.identities.manage"
  | "platform.retention.read"
  | "platform.retention.manage";

export interface PlatformState {
  role: "owner" | "admin";
  activated: boolean;
  mfaEnrolled: boolean;
  recoveryCodesSaved: boolean;
  stepUpFresh: boolean;
}

export interface PendingInvitation {
  token: string;
  orgName: string;
  role: OrgRole;
}

export interface Me {
  user: { id: string; name: string; email: string; confirmed: boolean };
  activeOrg: ActiveOrg | null;
  orgs: OrgSummary[];
  pendingInvitations: PendingInvitation[];
  platform: PlatformState | null;
}

export interface Member {
  id: string;
  name: string;
  email: string;
  role: OrgRole;
  roleLabel: string;
}

export interface OrgInvitation {
  id: string;
  email: string;
  role: OrgRole;
  status: InvitationStatus;
  expiresAt: string;
  token: string;
}

export interface InvitationView {
  token: string;
  scope: "org" | "platform";
  email: string;
  role: string;
  roleLabel: string;
  status: InvitationStatus;
  orgName: string;
  invitedByName: string | null;
  expiresAt: string;
  next: "register" | "confirm" | "login" | "accept" | "other-identity" | "none";
  viewerEmail: string | null;
  alreadyMember: boolean;
}

export interface Page<T> {
  page: T[];
  nextCursor: string | null;
  total: number;
}

export interface PlatformOrgRow {
  id: string;
  name: string;
  status: OrgStatus;
  suspendedReason: string | null;
  membersCount: number;
  createdAt: string;
  version: number;
}

/** Estados de una cuenta. Conjunto cerrado: sólo "active" puede ingresar. */
export type AccountStatus =
  | "pending_confirmation"
  | "active"
  | "self_deactivated"
  | "administratively_suspended"
  | "closed";

/** Razones aceptadas para detener una cuenta. Quedan en auditoría, no en la cuenta. */
export type SuspensionReason = "PolicyViolation" | "SecurityIncident" | "BillingHold" | "OperatorRequest";

export const SUSPENSION_REASONS: SuspensionReason[] = [
  "PolicyViolation",
  "SecurityIncident",
  "BillingHold",
  "OperatorRequest",
];

export interface PlatformIdentityRow {
  id: string;
  name: string;
  email: string;
  accountStatus: AccountStatus;
  confirmed: boolean;
  mfaEnrolled: boolean;
  orgsCount: number;
  lastSeenAt: string | null;
  createdAt: string;
}

export interface RetentionCategory {
  category: string;
  retentionPeriod: string;
  trigger: string;
  action: string;
  evidenceRequired: boolean;
}

/**
 * La política tal como se lee. Un despliegue sin política contesta con todos los
 * campos en null y sin categorías: eso es "acá no se va a borrar nada", que no es
 * lo mismo que una tabla vacía.
 */
export interface RetentionPolicy {
  policyId: string | null;
  version: string | null;
  owner: string | null;
  approvedOn: string | null;
  source: string | null;
  personalDataMode: string;
  activeHoldCount: number;
  categories: RetentionCategory[];
}

export interface RetentionHold {
  holdId: string;
  subjectIdentityId: string;
  reasonCode: string;
  reference: string;
  placedAt: string;
  placedByEmail: string;
  version: number;
}

export interface PlatformAdminRow {
  id: string;
  name: string;
  email: string;
  role: "owner" | "admin";
  activated: boolean;
  mfaEnrolled: boolean;
}

export interface AuditRow {
  id: string;
  at: string;
  actorEmail: string;
  action: string;
  target: string;
  detail: string;
  result: "ok" | "denegado";
}

export interface PlatformMe {
  role: "owner" | "admin";
  activated: boolean;
  permissions: PlatformPermission[];
  mfa: { enrolled: boolean; recoveryCodesSaved: boolean; verifiedAt: string | null };
  stepUpFresh: boolean;
}

export interface Mail {
  id: string;
  to: string;
  subject: string;
  body: string;
  link: string | null;
  kind: "registro" | "confirmacion" | "invitacion" | "invitacion-platform" | "aviso";
  sentAt: string;
}

export interface Scenario {
  id: string;
  journey: "A" | "B" | "C" | "D";
  name: string;
  description: string;
  start: string;
  hint: string;
}

/** Error devuelto por la API con { error: { code, message } }. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly retryAfter?: number;
  constructor(status: number, code: string, message: string, retryAfter?: number) {
    super(message);
    this.status = status;
    this.code = code;
    this.retryAfter = retryAfter;
  }
}

/** La API no respondió: red caída, servidor apagado, respuesta ilegible. */
export class NetworkError extends Error {
  constructor() {
    super("No pudimos comunicarnos con el servidor.");
  }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  let response: Response;
  try {
    response = await fetch(url, {
      method,
      headers: body === undefined ? undefined : { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: "same-origin",
    });
  } catch {
    throw new NetworkError();
  }

  if (response.status === 204 || response.status === 202) {
    return undefined as T;
  }

  let payload: unknown = null;
  try {
    payload = await response.json();
  } catch {
    if (response.ok) return undefined as T;
    throw new NetworkError();
  }

  if (!response.ok) {
    const error = (payload as { error?: { code?: string; message?: string; retryAfter?: number } } | null)?.error;
    if (response.status >= 500) throw new NetworkError();
    throw new ApiError(response.status, error?.code ?? "unknown", error?.message ?? "Error", error?.retryAfter);
  }
  return payload as T;
}

export const api = {
  health: () => request<Health>("GET", "/api/health"),

  // Recorrido A
  register: (input: { cuit: string; name: string; email: string; password: string }) =>
    request<void>("POST", "/api/auth/register", input),
  confirm: (token: string) => request<void>("POST", "/api/auth/confirm", { token }),
  login: (input: { email: string; password: string }) => request<void>("POST", "/api/auth/login", input),
  logout: () => request<void>("POST", "/api/auth/logout"),
  me: () => request<Me>("GET", "/api/me"),

  // Recorrido B
  selectOrg: (orgId: string) => request<Me>("PUT", "/api/me/org", { orgId }),
  createOrg: (input: { cuit: string; name: string }) => request<Me>("POST", "/api/orgs", input),

  // Recorrido C
  members: (orgId: string) => request<{ members: Member[] }>("GET", `/api/orgs/${orgId}/members`),
  invitations: (orgId: string) =>
    request<{ invitations: OrgInvitation[]; assignableRoles: OrgRole[] }>("GET", `/api/orgs/${orgId}/invitations`),
  invite: (orgId: string, input: { email: string; role: OrgRole }) =>
    request<unknown>("POST", `/api/orgs/${orgId}/invitations`, input),
  cancelInvitation: (orgId: string, invitationId: string) =>
    request<unknown>("POST", `/api/orgs/${orgId}/invitations/${invitationId}/cancel`),
  invitation: (token: string) => request<InvitationView>("GET", `/api/invitations/${token}`),
  registerFromInvitation: (token: string, input: { name: string; password: string }) =>
    request<void>("POST", `/api/invitations/${token}/register`, input),
  acceptInvitation: (token: string) =>
    request<{ alreadyAccepted: boolean; me: Me }>("POST", `/api/invitations/${token}/accept`),

  // Recorrido D
  platformMe: () => request<PlatformMe>("GET", "/api/platform/me"),
  mfaEnroll: () => request<{ secret: string; simulatedCode: string }>("POST", "/api/platform/mfa/enroll"),
  mfaVerify: (code: string) => request<{ recoveryCodes: string[] }>("POST", "/api/platform/mfa/verify", { code }),
  mfaAck: () => request<{ activated: boolean }>("POST", "/api/platform/mfa/recovery-codes/ack"),
  stepUp: (code: string) => request<{ stepUpFresh: boolean }>("POST", "/api/platform/step-up", { code }),
  platformOrgs: (cursor?: string | null) =>
    request<Page<PlatformOrgRow>>("GET", `/api/platform/orgs${cursor ? `?cursor=${cursor}` : ""}`),
  platformIdentities: (cursor?: string | null) =>
    request<Page<PlatformIdentityRow>>("GET", `/api/platform/identities${cursor ? `?cursor=${cursor}` : ""}`),
  platformAdmins: (cursor?: string | null) =>
    request<Page<PlatformAdminRow>>("GET", `/api/platform/admins${cursor ? `?cursor=${cursor}` : ""}`),
  platformAudit: (cursor?: string | null) =>
    request<Page<AuditRow>>("GET", `/api/platform/audit${cursor ? `?cursor=${cursor}` : ""}`),
  suspendOrg: (orgId: string, input: { reason: string; version: number }) =>
    request<PlatformOrgRow>("POST", `/api/platform/orgs/${orgId}/suspend`, input),
  reactivateOrg: (orgId: string, input: { version: number }) =>
    request<PlatformOrgRow>("POST", `/api/platform/orgs/${orgId}/reactivate`, input),
  // El estado esperado viaja con el pedido: es el que se leyó en el directorio, y
  // el cambio sólo se aplica si la cuenta sigue ahí.
  suspendIdentity: (userId: string, input: { reason: SuspensionReason; expectedStatus: AccountStatus }) =>
    request<void>("POST", `/api/platform/identities/${userId}/suspend`, input),
  reactivateIdentity: (
    userId: string,
    input: { expectedStatus: AccountStatus; acknowledgeSelfDeactivation: boolean },
  ) => request<void>("POST", `/api/platform/identities/${userId}/reactivate`, input),
  retentionPolicy: () => request<RetentionPolicy>("GET", "/api/platform/retention/policy"),
  placeRetentionHold: (input: { subjectIdentityId: string; reasonCode: string; reference: string }) =>
    request<RetentionHold>("POST", "/api/platform/retention/holds", input),
  releaseRetentionHold: (holdId: string) =>
    request<void>("DELETE", `/api/platform/retention/holds/${holdId}`),
  invitePlatformAdmin: (email: string) => request<unknown>("POST", "/api/platform/admins", { email }),
  revokePlatformAdmin: (userId: string) => request<void>("DELETE", `/api/platform/admins/${userId}`),
  bootstrapRecover: () => request<{ message: string }>("POST", "/api/platform/bootstrap/recover"),
  acceptPlatformInvitation: (token: string) =>
    request<{ me: Me }>("POST", `/api/platform/invitations/${token}/accept`),

  // Panel de demostración
  scenarios: () => request<{ scenarios: Scenario[] }>("GET", "/api/dev/scenarios"),
  applyScenario: (id: string) =>
    request<{ id: string; start: string; hint: string; name: string }>("POST", "/api/dev/scenario", { id }),
  resetDemo: () => request<void>("POST", "/api/dev/reset"),
  mails: () => request<{ mails: Mail[] }>("GET", "/api/dev/mails"),
  failNext: (mode: "network" | "conflict" | "notfound" | null) =>
    request<{ failNext: string | null }>("POST", "/api/dev/fail-next", { mode }),
  revokeSession: () => request<void>("POST", "/api/dev/revoke-session"),
  expireStepUp: () => request<void>("POST", "/api/dev/expire-step-up"),
};
