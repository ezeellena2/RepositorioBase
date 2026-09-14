import { Link, Navigate, useLocation, useNavigate } from "react-router-dom";
import { Alert, Badge, Button, PageHeader } from "../../components";
import type { OrgType } from "../../lib/api";
import { PERMISSION_LABEL, STATUS_LABEL, TYPE_LABEL } from "../../lib/format";
import { useSession } from "../../lib/session";

export function HomePage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { me, switchOrg, switching } = useSession();
  if (!me) return null;

  // Sin cuenta personal ni empresa no hay nada que mostrar acá: falta elegir qué crear.
  if (me.orgs.length === 0) return <Navigate to="/configurar" replace />;

  // Varios contextos y ninguno elegido: selector inicial.
  if (!me.activeOrg) {
    return (
      <>
        <PageHeader
          title="¿Con qué cuenta querés operar?"
          description="Elegís el contexto de trabajo: tu cuenta personal o una empresa. Seguís siendo la misma persona autenticada."
        />
        <ul className="orgpick">
          {me.orgs.map((org) => (
            <li key={org.id}>
              <button type="button" className="orgpick__row" onClick={() => void switchOrg(org.id)} disabled={switching}>
                <span className="orgpick__name">{org.name}</span>
                <span className="orgpick__meta">
                  <Badge>{TYPE_LABEL[org.type]}</Badge>
                  <Badge tone={org.status === "suspended" ? "danger" : "ok"}>{STATUS_LABEL[org.status]}</Badge>
                  <span className="orgpick__role">{org.roleLabel}</span>
                </span>
              </button>
            </li>
          ))}
        </ul>
      </>
    );
  }

  const org = me.activeOrg;
  const created = (location.state as { created?: OrgType } | null)?.created;
  const canInvite = org.permissions.includes("members.invite");
  const others = me.orgs.filter((o) => o.id !== org.id);

  return (
    <>
      <PageHeader
        title={`Hola, ${me.user.name.split(" ")[0]}`}
        description={
          org.type === "persona" ? "Estás en tu cuenta personal." : `Estás operando con ${org.name} como ${org.roleLabel}.`
        }
      />

      {created && (
        <Alert variant="success">
          {created === "persona" ? "Tu cuenta personal está lista." : `${org.name} está lista y quedaste como Titular.`}
        </Alert>
      )}

      {org.status === "suspended" && (
        <Alert variant="error">
          <strong>Organización suspendida.</strong> {org.suspendedReason ?? "Sin razón registrada."} Mientras dure la
          suspensión no se pueden emitir invitaciones ni realizar operaciones. Podés seguir consultando la información.
        </Alert>
      )}

      {org.type === "persona" ? (
        <section className="panel">
          <h2 className="panel__title">Tu cuenta personal</h2>
          <p className="panel__text">
            {org.name} · DNI {org.document}. El documento no se edita desde acá: corregirlo es un proceso verificado.
          </p>
          <p className="panel__text">Una cuenta personal no tiene integrantes. Para trabajar con otras personas, creá una empresa.</p>
          <div className="panel__actions">
            <Button className="btn--inline" variant="ghost" onClick={() => navigate("/app/organizaciones/nueva")}>
              Crear una empresa
            </Button>
          </div>
        </section>
      ) : (
        <section className="panel">
          <h2 className="panel__title">Qué podés hacer en este contexto</h2>
          <ul className="permlist">
            {org.permissions.map((permission) => (
              <li key={permission} className="permlist__item">
                <span className="permlist__mark" aria-hidden="true">
                  ✓
                </span>
                {PERMISSION_LABEL[permission]}
              </li>
            ))}
          </ul>
          <div className="panel__actions">
            {canInvite ? (
              <Button
                className="btn--inline"
                onClick={() => navigate("/app/invitaciones")}
                disabled={org.status === "suspended"}
              >
                Invitar integrante
              </Button>
            ) : (
              <p className="panel__denied">
                Invitar integrantes no está disponible: en {org.name} tu rol es {org.roleLabel}. La acción no aparece en el
                menú y tampoco se puede forzar por URL.
              </p>
            )}
          </div>
        </section>
      )}

      {others.length > 0 && (
        <section className="panel">
          <h2 className="panel__title">Aislamiento entre contextos</h2>
          <p className="panel__text">
            Tus permisos se recalculan por contexto. Cambiá desde el selector del menú lateral y mirá cómo cambian el rol,
            los permisos y las secciones disponibles.
          </p>
          <ul className="minilist">
            {others.map((other) => (
              <li key={other.id} className="minilist__item">
                <span>
                  {other.name} — {TYPE_LABEL[other.type]} · {other.roleLabel}
                </span>
                <button type="button" className="btn btn--text" onClick={() => void switchOrg(other.id)} disabled={switching}>
                  Operar con esta
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      {me.pendingInvitations.length > 0 && (
        <section className="panel">
          <h2 className="panel__title">Invitaciones pendientes para vos</h2>
          <ul className="minilist">
            {me.pendingInvitations.map((invitation) => (
              <li key={invitation.token} className="minilist__item">
                <span>{invitation.orgName}</span>
                <Link className="btn btn--text" to={`/invitacion?token=${invitation.token}`}>
                  Ver invitación
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  );
}
