export type CuitKind = "persona" | "empresa";

const PERSONA_PREFIXES = ["20", "23", "24", "25", "26", "27"];
const EMPRESA_PREFIXES = ["30", "33", "34"];

export function normalizeCuit(input: string): string {
  return input.replace(/\D/g, "");
}

export function formatCuit(digits: string): string {
  const d = normalizeCuit(digits);
  if (d.length !== 11) return d;
  return `${d.slice(0, 2)}-${d.slice(2, 10)}-${d.slice(10)}`;
}

export function kindFromPrefix(prefix: string): CuitKind | null {
  if (PERSONA_PREFIXES.includes(prefix)) return "persona";
  if (EMPRESA_PREFIXES.includes(prefix)) return "empresa";
  return null;
}

export function checkDigit(first10: string): number {
  const weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
  const sum = first10
    .split("")
    .reduce((acc, ch, i) => acc + Number(ch) * weights[i], 0);
  const rest = 11 - (sum % 11);
  if (rest === 11) return 0;
  if (rest === 10) return 9;
  return rest;
}

export type CuitValidation =
  | { ok: true; digits: string; kind: CuitKind }
  | { ok: false; reason: "length" | "prefix" | "check" };

export function validateCuit(input: string): CuitValidation {
  const digits = normalizeCuit(input);
  if (digits.length !== 11) return { ok: false, reason: "length" };
  const kind = kindFromPrefix(digits.slice(0, 2));
  if (!kind) return { ok: false, reason: "prefix" };
  if (checkDigit(digits.slice(0, 10)) !== Number(digits[10])) {
    return { ok: false, reason: "check" };
  }
  return { ok: true, digits, kind };
}

/** Builds a valid CUIT from a prefix and 8-digit body. Used only for seed data. */
export function buildCuit(prefix: string, body: string): string {
  const first10 = `${prefix}${body}`;
  return `${first10}${checkDigit(first10)}`;
}
