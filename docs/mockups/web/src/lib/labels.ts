import type { ActiveTenant, FiscalType, Role, Scope, TenantSummary } from "./api";

type TenantLike = Pick<TenantSummary, "scope" | "type" | "name">;

export type ContextKey = "empresa" | "persona" | "platform";

export function contextKey(t: { scope: Scope; type: FiscalType | null }): ContextKey {
  if (t.scope === "platform") return "platform";
  return t.type === "persona" ? "persona" : "empresa";
}

export function typeLabel(t: TenantLike): string {
  if (t.scope === "platform") return "Operaciones";
  return t.type === "persona" ? "Persona física" : "Empresa";
}

export const ROLE_LABEL: Record<Role, string> = {
  owner: "Dueño",
  admin: "Administrador",
  member: "Miembro",
  operator: "Operador",
};

export function initials(name: string): string {
  const words = name
    .replace(/[.,]/g, "")
    .split(/\s+/)
    .filter((w) => w.length > 0 && !/^(s|sa|srl|sas|sh|de|del|la|el|y)$/i.test(w));
  if (words.length === 0) return name.slice(0, 2).toUpperCase();
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}

export function firstName(name: string): string {
  return name.split(/\s+/)[0];
}

/** "CUIT 30-71234567-1 · Empresa · Tu rol: Dueño", o sin CUIT en plataforma. */
export function sealLine(t: ActiveTenant): { cuit: string | null; rest: string } {
  const kind = t.scope === "platform" ? "Alcance de plataforma" : typeLabel(t);
  return { cuit: t.cuit, rest: `${kind} · Tu rol: ${ROLE_LABEL[t.role]}` };
}

export function longDate(date = new Date()): string {
  return new Intl.DateTimeFormat("es-AR", { weekday: "long", day: "numeric", month: "long" }).format(date).replace(",", "");
}

export function shortTime(iso: string): string {
  return new Intl.DateTimeFormat("es-AR", { hour: "2-digit", minute: "2-digit" }).format(new Date(iso));
}

export function shortDate(iso: string): string {
  return new Intl.DateTimeFormat("es-AR", { day: "numeric", month: "short", year: "numeric" }).format(new Date(iso));
}
