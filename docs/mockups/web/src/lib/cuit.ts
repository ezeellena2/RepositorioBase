// Misma lógica que api/src/cuit.ts. Si cambia una, cambia la otra.

export type CuitKind = "persona" | "empresa";

const PERSONA_PREFIXES = new Set(["20", "23", "24", "25", "26", "27"]);
const EMPRESA_PREFIXES = new Set(["30", "33", "34"]);
const WEIGHTS = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

/** Deja solo dígitos. Devuelve los 11 dígitos o null si no son exactamente 11. */
export function normalize(input: string): string | null {
  const digits = String(input ?? "").replace(/\D/g, "");
  return digits.length === 11 ? digits : null;
}

/** Deduce el tipo por el prefijo. */
export function kind(normalized: string): CuitKind | null {
  const prefix = normalized.slice(0, 2);
  if (PERSONA_PREFIXES.has(prefix)) return "persona";
  if (EMPRESA_PREFIXES.has(prefix)) return "empresa";
  return null;
}

/** Verifica el dígito verificador (módulo 11). */
export function isValid(normalized: string): boolean {
  if (!/^\d{11}$/.test(normalized)) return false;
  const sum = WEIGHTS.reduce((acc, weight, i) => acc + Number(normalized[i]) * weight, 0);
  const rest = 11 - (sum % 11);
  const expected = rest === 11 ? 0 : rest === 10 ? 9 : rest;
  return expected === Number(normalized[10]);
}

/** 20334445551 → "20-33444555-1" */
export function format(normalized: string): string {
  return `${normalized.slice(0, 2)}-${normalized.slice(2, 10)}-${normalized.slice(10)}`;
}

/** Atajo: normaliza, valida y deduce en un paso. */
export function parse(input: string): { normalized: string; kind: CuitKind } | null {
  const normalized = normalize(input);
  if (!normalized || !isValid(normalized)) return null;
  const type = kind(normalized);
  return type ? { normalized, kind: type } : null;
}
