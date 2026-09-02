import type { ActiveTenant, Scope } from "./api";

export type IconName =
  | "home"
  | "channel"
  | "checklist"
  | "link"
  | "grid"
  | "activity"
  | "plug"
  | "bolt"
  | "users"
  | "phone";

export interface NavItem {
  id: string;
  code: string; // código del inventario (P1, O1, M1...)
  label: string;
  to: string;
  icon: IconName;
  group: "root" | "platform" | "org" | "mine";
  scope: Scope | "any";
  permission?: string;
}

/**
 * Inventario de pantallas que son ítems de menú. Se filtra dos veces:
 * por alcance del contexto activo y por permiso efectivo por ítem.
 */
export const NAV: NavItem[] = [
  { id: "home", code: "", label: "Inicio", to: "/inicio", icon: "home", group: "root", scope: "any" },

  { id: "p1", code: "P1", label: "Canal de WhatsApp", to: "/plataforma/canal", icon: "channel", group: "platform", scope: "platform", permission: "platform.whatsapp.read" },
  { id: "p2", code: "P2", label: "Puesta en marcha", to: "/plataforma/puesta-en-marcha", icon: "checklist", group: "platform", scope: "platform", permission: "platform.whatsapp.read" },
  { id: "p3", code: "P3", label: "Números vinculados", to: "/plataforma/numeros", icon: "link", group: "platform", scope: "platform", permission: "platform.whatsapp.read" },
  { id: "p4", code: "P4", label: "Catálogo de capacidades", to: "/plataforma/capacidades", icon: "grid", group: "platform", scope: "platform", permission: "platform.bot.capabilities.read" },
  { id: "p5", code: "P5", label: "Monitor", to: "/plataforma/monitor", icon: "activity", group: "platform", scope: "platform", permission: "platform.whatsapp.read" },

  { id: "o1", code: "O1", label: "Integraciones", to: "/organizacion/integraciones", icon: "plug", group: "org", scope: "tenant", permission: "bot.modules.manage" },
  { id: "o3", code: "O3", label: "Capacidades del asistente", to: "/organizacion/capacidades", icon: "bolt", group: "org", scope: "tenant", permission: "bot.capabilities.manage" },
  { id: "o4", code: "O4", label: "Miembros y WhatsApp", to: "/organizacion/miembros", icon: "users", group: "org", scope: "tenant", permission: "whatsapp.links.read" },

  { id: "m1", code: "M1", label: "Mi WhatsApp", to: "/mi/whatsapp", icon: "phone", group: "mine", scope: "tenant" },
];

/** Pantallas del inventario que no son ítems de menú pero viven dentro del shell. */
export const SCREENS_WITHOUT_NAV = [
  { id: "o2", code: "O2", label: "Credenciales del módulo", to: "/organizacion/integraciones/:modulo/credenciales", scope: "tenant", permission: "bot.modules.manage" },
  { id: "s2", code: "S2", label: "Autorización externa", to: "/organizacion/integraciones/:modulo/autorizacion", scope: "tenant", permission: "bot.modules.manage" },
  { id: "m2", code: "M2", label: "Vincular número", to: "/mi/whatsapp/vincular", scope: "tenant" },
] as const;

export const GROUP_LABEL: Record<NavItem["group"], string | null> = {
  root: null,
  platform: "Plataforma",
  org: "Organización",
  mine: "Lo mío",
};

export function canAccess(item: { scope: Scope | "any"; permission?: string }, tenant: ActiveTenant | null): boolean {
  if (!tenant) return false;
  if (item.scope !== "any" && item.scope !== tenant.scope) return false;
  if (item.permission && !tenant.permissions.includes(item.permission)) return false;
  return true;
}

export interface NavGroup {
  group: NavItem["group"];
  label: string | null;
  items: NavItem[];
}

/** Navegación visible para el contexto activo: ausente, no deshabilitado. */
export function visibleNav(tenant: ActiveTenant | null): NavGroup[] {
  const order: NavItem["group"][] = ["root", "platform", "org", "mine"];
  return order
    .map((group) => ({ group, label: GROUP_LABEL[group], items: NAV.filter((i) => i.group === group && canAccess(i, tenant)) }))
    .filter((g) => g.items.length > 0);
}

/** Adónde lleva una condición abierta del contexto: P1 en plataforma, O1 en organización. */
export function degradedTarget(tenant: ActiveTenant): NavItem | undefined {
  return NAV.find((i) => i.id === (tenant.scope === "platform" ? "p1" : "o1"));
}

export function findNav(id: string): NavItem {
  const item = NAV.find((i) => i.id === id);
  if (!item) throw new Error(`Nav item desconocido: ${id}`);
  return item;
}
