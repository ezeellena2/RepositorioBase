export type CuitKind = "persona" | "empresa";

const PERSONA = ["20", "23", "24", "25", "26", "27"];
const EMPRESA = ["30", "33", "34"];

export function digitsOnly(value: string): string {
  return value.replace(/\D/g, "").slice(0, 11);
}

/** 20123456789 -> 20-12345678-9, progressively while typing. */
export function formatCuit(value: string): string {
  const d = digitsOnly(value);
  if (d.length <= 2) return d;
  if (d.length <= 10) return `${d.slice(0, 2)}-${d.slice(2)}`;
  return `${d.slice(0, 2)}-${d.slice(2, 10)}-${d.slice(10)}`;
}

export function kindFromCuit(value: string): CuitKind | "unknown" | null {
  const d = digitsOnly(value);
  if (d.length < 2) return null;
  const prefix = d.slice(0, 2);
  if (PERSONA.includes(prefix)) return "persona";
  if (EMPRESA.includes(prefix)) return "empresa";
  return "unknown";
}
