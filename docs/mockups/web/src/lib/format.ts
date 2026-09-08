import type { OrgRole, OrgStatus, OrgType, Permission } from "./api";

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
