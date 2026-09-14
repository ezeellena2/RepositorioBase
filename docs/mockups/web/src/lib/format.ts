import type { AccountStatus, OrgRole, OrgStatus, OrgType, Permission, PlatformPermission, SuspensionReason } from "./api";

export const TYPE_LABEL: Record<OrgType, string> = {
  persona: "Personal",
  empresa: "Empresa",
};

export const STATUS_LABEL: Record<OrgStatus, string> = {
  active: "Activa",
  suspended: "Suspendida",
};

export const ROLE_LABEL: Record<OrgRole, string> = {
  owner: "Titular",
  admin: "Administradora",
  member: "Integrante",
};

export const PERMISSION_LABEL: Record<Permission, string> = {
  "members.view": "Ver integrantes",
  "members.invite": "Invitar integrantes",
  "members.manage": "Gestionar integrantes",
  "org.manage": "Administrar la organización",
};

export const PLATFORM_PERMISSION_LABEL: Record<PlatformPermission, string> = {
  "platform.identities.read": "Ver identidades",
  "platform.identities.manage": "Detener y reactivar cuentas",
  "platform.retention.read": "Ver la política de retención",
  "platform.retention.manage": "Poner y levantar retenciones",
};

export const ACCOUNT_STATUS_LABEL: Record<AccountStatus, string> = {
  pending_confirmation: "Sin confirmar",
  active: "Activa",
  self_deactivated: "Baja propia",
  administratively_suspended: "Detenida por Platform",
  closed: "Cerrada",
};

/** Qué dice cada estado sobre lo que la cuenta puede hacer. */
export const ACCOUNT_STATUS_HELP: Record<AccountStatus, string> = {
  pending_confirmation: "Se registró y todavía no confirmó el correo.",
  active: "Único estado que puede ingresar.",
  self_deactivated: "La persona dio de baja su propia cuenta. Vuelve por sus medios.",
  administratively_suspended: "La detuvo una administración de Platform. Sus sesiones se terminaron.",
  closed: "Borrado ejecutado. Es terminal: no hay vuelta.",
};

export const SUSPENSION_REASON_LABEL: Record<SuspensionReason, string> = {
  PolicyViolation: "Incumplimiento de las condiciones",
  SecurityIncident: "Incidente de seguridad",
  BillingHold: "Retención por facturación",
  OperatorRequest: "Pedido de la administración",
};

export const INVITATION_STATUS_LABEL: Record<string, string> = {
  pending: "Pendiente",
  accepted: "Aceptada",
  cancelled: "Cancelada",
  expired: "Vencida",
};

/** "Juan Pérez" -> "JP" */
export function initials(name: string): string {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0] ?? "")
    .join("")
    .toUpperCase();
}

export function shortDate(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "—";
  return date.toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit", year: "numeric" });
}

export function shortDateTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "—";
  return date.toLocaleString("es-AR", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}
