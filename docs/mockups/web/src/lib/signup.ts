import type { OrgType } from "./api";

// Lo que comparten las pantallas de entrar y crear cuenta. El paso en curso viaja
// en la URL (?tipo=&reto=), así que recargar o cargar un escenario no lo pierde.

export const SIGNUP_STEPS = ["Tipo", "Acceso", "Verificación", "Datos"];
/** Quien ya tiene sesión y ningún contexto sólo elige el tipo y completa los datos. */
export const SETUP_STEPS = ["Tipo", "Datos"];

export const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
export const MIN_PASSWORD = 12;

export const TYPE_COPY: Record<OrgType, { title: string; detail: string; noun: string }> = {
  persona: { title: "Cuenta personal", detail: "Para vos: tu nombre y DNI.", noun: "la cuenta personal" },
  empresa: { title: "Cuenta de empresa", detail: "Para tu organización: razón social y CUIT.", noun: "la empresa" },
};

export function readType(params: URLSearchParams): OrgType | null {
  const value = params.get("tipo");
  return value === "persona" || value === "empresa" ? value : null;
}

export function signupPath(path: string, values: { tipo?: OrgType | null; reto?: string | null }): string {
  const params = new URLSearchParams();
  if (values.tipo) params.set("tipo", values.tipo);
  if (values.reto) params.set("reto", values.reto);
  const query = params.toString();
  return query ? `${path}?${query}` : path;
}
