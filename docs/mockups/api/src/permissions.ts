// Permisos simulados del mockup. No son los del producto: alcanzan para mostrar
// que el menú y las acciones cambian según el rol en la organización activa.

export type OrgRole = "owner" | "admin" | "member";

export type Permission = "members.view" | "members.invite" | "members.manage" | "org.manage";

const BY_ROLE: Record<OrgRole, Permission[]> = {
  owner: ["members.view", "members.invite", "members.manage", "org.manage"],
  admin: ["members.view", "members.invite"],
  member: ["members.view"],
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

export function permissionsOf(role: OrgRole): Permission[] {
  return [...BY_ROLE[role]];
}

export function can(role: OrgRole, permission: Permission): boolean {
  return BY_ROLE[role].includes(permission);
}

/** Nunca se puede invitar con autoridad mayor a la propia. */
export function assignableRoles(role: OrgRole): OrgRole[] {
  if (role === "owner") return ["admin", "member"];
  if (role === "admin") return ["member"];
  return [];
}

// --- Platform --------------------------------------------------------------
// El acceso a Platform también se reparte por permisos, y leer no implica poder
// cambiar. Un rol que sólo lee es una combinación real: la pantalla tiene que
// mostrarla sin ofrecer botones cuya única respuesta posible es un rechazo.

export type PlatformRole = "owner" | "admin";

export type PlatformPermission =
  | "platform.identities.read"
  | "platform.identities.manage"
  | "platform.retention.read"
  | "platform.retention.manage";

const PLATFORM_BY_ROLE: Record<PlatformRole, PlatformPermission[]> = {
  owner: [
    "platform.identities.read",
    "platform.identities.manage",
    "platform.retention.read",
    "platform.retention.manage",
  ],
  // Administradora: mira los dos directorios nuevos y no cambia ninguno.
  admin: ["platform.identities.read", "platform.retention.read"],
};

export const PLATFORM_PERMISSION_LABEL: Record<PlatformPermission, string> = {
  "platform.identities.read": "Ver identidades",
  "platform.identities.manage": "Detener y reactivar cuentas",
  "platform.retention.read": "Ver la política de retención",
  "platform.retention.manage": "Poner y levantar retenciones",
};

export function platformPermissionsOf(role: PlatformRole): PlatformPermission[] {
  return [...PLATFORM_BY_ROLE[role]];
}

export function canPlatform(role: PlatformRole, permission: PlatformPermission): boolean {
  return PLATFORM_BY_ROLE[role].includes(permission);
}
