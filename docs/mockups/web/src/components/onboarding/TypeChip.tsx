import { Link } from "react-router-dom";
import type { OrgType } from "../../lib/api";
import { TYPE_COPY } from "../../lib/signup";

/** Lo que se eligió en el primer paso, a la vista y con la salida para cambiarlo. */
export function TypeChip({ type, changeTo = "/crear-cuenta" }: { type: OrgType; changeTo?: string }) {
  return (
    <p className="typechip">
      <span className="tag">{TYPE_COPY[type].title}</span>
      <Link to={changeTo}>Cambiar</Link>
    </p>
  );
}
