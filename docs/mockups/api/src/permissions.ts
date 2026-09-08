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
