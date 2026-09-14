import { Badge } from "../Badge";
import { PERMISSION_LABEL, STATUS_LABEL, TYPE_LABEL } from "../../lib/format";
import { useSession } from "../../lib/session";

/**
 * Contexto persistente: quién sos, con qué organización operás, con qué rol y
 * permisos, y el estado de MFA simulada cuando corresponde.
 */
export function ContextBar() {
  const { me } = useSession();
  if (!me) return null;
  const org = me.activeOrg;

  return (
    <section className="ctxbar" aria-label="Contexto de trabajo">
      <div className="ctxbar__block">
        <p className="ctxbar__label">Identidad autenticada</p>
        <p className="ctxbar__value">{me.user.name}</p>
        <p className="ctxbar__sub">{me.user.email}</p>
      </div>

      <div className="ctxbar__block">
        <p className="ctxbar__label">Contexto activo</p>
        <p className="ctxbar__value">{org ? org.name : "Ninguno"}</p>
        <p className="ctxbar__sub">
          {org ? (
            <>
              {TYPE_LABEL[org.type]} · {org.cuit ? `CUIT ${org.cuit}` : `DNI ${org.document}`} ·{" "}
              <Badge tone={org.status === "suspended" ? "danger" : "ok"}>{STATUS_LABEL[org.status]}</Badge>
            </>
          ) : (
            "Elegí con qué contexto operar"
          )}
        </p>
      </div>

      <div className="ctxbar__block">
        <p className="ctxbar__label">Rol y permisos</p>
        <p className="ctxbar__value">{org ? org.roleLabel : "—"}</p>
        <p className="ctxbar__sub">
          {org?.type === "persona"
            ? "Cuenta personal: no tiene integrantes"
            : org && org.permissions.length > 0
              ? org.permissions.map((permission) => PERMISSION_LABEL[permission]).join(" · ")
              : "Sin permisos en este contexto"}
        </p>
      </div>

      {me.platform && (
        <div className="ctxbar__block">
          <p className="ctxbar__label">Platform</p>
          <p className="ctxbar__value">{me.platform.role === "owner" ? "Titular" : "Administradora"}</p>
          <p className="ctxbar__sub">
            MFA simulada:{" "}
            {me.platform.mfaEnrolled ? (
              <Badge tone={me.platform.stepUpFresh ? "ok" : "warn"}>
                {me.platform.stepUpFresh ? "vigente" : "sin step-up reciente"}
              </Badge>
            ) : (
              <Badge tone="warn">pendiente</Badge>
            )}
          </p>
        </div>
      )}
    </section>
  );
}
