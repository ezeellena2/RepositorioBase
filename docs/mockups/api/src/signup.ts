// Reglas del alta con código por correo. Son puras: el servidor las aplica y los
// tests las fijan, igual que el ciclo de vida de cuentas.

export const CODE_LENGTH = 6;
export const CODE_TTL_MS = 10 * 60 * 1000;
export const CODE_MAX_ATTEMPTS = 5;
export const CODE_RESEND_MS = 30 * 1000;
export const MIN_PASSWORD = 12;

export interface CodeState {
  code: string;
  expiresAt: string;
  attempts: number;
  spentAt: string | null;
}

/**
 * Qué pasa con un código enviado. "closed" es un desafío ya usado para crear o
 * abrir la sesión; "locked" es uno que agotó los intentos. Ninguno de los dos
 * vuelve a aceptar nada: hay que pedir un código nuevo.
 */
export type CodeOutcome = "ok" | "wrong" | "expired" | "locked" | "closed";

/** Seis dígitos, con ceros a la izquierda cuando tocan. */
export function generateCode(random: () => number = Math.random): string {
  return String(Math.floor(random() * 10 ** CODE_LENGTH)).padStart(CODE_LENGTH, "0");
}

export function evaluateCode(state: CodeState, submitted: string, nowMs: number): CodeOutcome {
  if (state.spentAt) return "closed";
  if (state.attempts >= CODE_MAX_ATTEMPTS) return "locked";
  if (new Date(state.expiresAt).getTime() <= nowMs) return "expired";
  return String(submitted ?? "").replace(/\s/g, "") === state.code ? "ok" : "wrong";
}

export function remainingAttempts(attempts: number): number {
  return Math.max(0, CODE_MAX_ATTEMPTS - attempts);
}

/** Segundos que faltan para poder pedir otro código. Cero cuando ya se puede. */
export function resendWaitSeconds(resendAvailableAt: string, nowMs: number): number {
  return Math.max(0, Math.ceil((new Date(resendAvailableAt).getTime() - nowMs) / 1000));
}

/** Acepta puntos y espacios ("30.111.222"). Devuelve 7 u 8 dígitos, o null. */
export function normalizeDni(input: string): string | null {
  const digits = String(input ?? "").replace(/[.\s]/g, "");
  return /^\d{7,8}$/.test(digits) ? digits : null;
}

/** El documento nunca viaja entero hacia la pantalla: sólo los últimos tres dígitos. */
export function maskDni(dni: string): string {
  return `••••${dni.slice(-3)}`;
}
