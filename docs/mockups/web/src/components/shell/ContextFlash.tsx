import { useShell } from "../../lib/shell-context";

/** Acuse del cambio de contexto. Siempre en el DOM para que aria-live tenga región. */
export function ContextFlash() {
  const { flash } = useShell();
  return (
    <div className={`context-flash${flash ? " is-visible" : ""}`} role="status" aria-live="polite">
      {flash}
    </div>
  );
}
