import type { AccountStatus, SuspensionReason } from "./store.js";
import { SUSPENSION_REASONS } from "./store.js";

// Reglas del ciclo de vida de una cuenta, sin estado ni HTTP alrededor. Son las
// mismas que aplica el backend real: acá están separadas para poder probarlas.

/** Único estado que puede ingresar. Todo lo demás es una cuenta detenida. */
export const canSignIn = (status: AccountStatus): boolean => status === "active";

export const isSuspensionReason = (value: unknown): value is SuspensionReason =>
  SUSPENSION_REASONS.includes(value as SuspensionReason);

/**
 * Qué transición aceptaría el servidor desde un estado dado, y por lo tanto la
 * única que vale la pena ofrecer. Reactivar se acepta desde
 * "administratively_suspended" y desde ningún otro lado; suspender, desde todos
 * menos ese y "closed", que es terminal.
 */
export function transitionFor(status: AccountStatus): "suspend" | "reactivate" | null {
  if (status === "administratively_suspended") return "reactivate";
  if (status === "closed") return null;
  return "suspend";
}

/**
 * A dónde vuelve una cuenta cuando se levanta la suspensión: a donde estaba
 * cuando la suspensión la interrumpió, no a "active" por definición. Una cuenta
 * sin nada recordado se suspendió antes de que esto se registrara, y entonces
 * "active" es el único estado desde el que la suspensión pudo haber salido.
 */
export function restoredStatus(before: AccountStatus | null): AccountStatus {
  return before ?? "active";
}
