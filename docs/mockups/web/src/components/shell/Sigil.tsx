import type { FiscalType, Scope } from "../../lib/api";
import { contextKey, initials } from "../../lib/labels";

interface SigilProps {
  name: string;
  scope: Scope;
  type: FiscalType | null;
  size?: 26 | 30 | 44;
  /** Punto de degradación con anillo, para contextos no activos. */
  dot?: boolean;
}

/** Cuadrado redondeado con iniciales: el único lugar donde aparece el color del contexto. */
export function Sigil({ name, scope, type, size = 26, dot = false }: SigilProps) {
  return (
    <span className={`sigil sigil--${size}`} data-ctx={contextKey({ scope, type })} aria-hidden="true">
      {initials(name)}
      {dot && <span className="sigil__dot" />}
    </span>
  );
}
