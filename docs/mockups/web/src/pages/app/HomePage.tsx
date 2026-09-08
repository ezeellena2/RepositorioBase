import { Link, useNavigate } from "react-router-dom";
import { Alert, Badge, Button, PageHeader, StateBlock } from "../../components";
import { PERMISSION_LABEL, STATUS_LABEL, TYPE_LABEL } from "../../lib/format";
import { useSession } from "../../lib/session";

export function HomePage() {
  const navigate = useNavigate();
  const { me, switchOrg, switching } = useSession();
  if (!me) return null;

  // Sin membresías: estado vacío explicativo con la salida.
  if (me.orgs.length === 0) {
    return (
      <StateBlock
        kind="empty"
        title="Todavía no pertenecés a ninguna organización"
        description="Podés crear la tuya o esperar a que te inviten. Si te invitaron, abrí el enlace del correo: la membresía se activa al aceptar."
        action={<Button onClick={() => navigate("/app/organizaciones/nueva")}>Crear una organización</Button>}
      />
    );
  }

  // Varias organizaciones y ninguna elegida: selector inicial.
  if (!me.activeOrg) {
    return (
      <>
        <PageHeader
          title="¿Con qué organización querés operar?"
          description="Elegís el contexto de trabajo. Seguís siendo la misma persona autenticada."
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
  const canInvite = org.permissions.includes("members.invite");
  const others = me.orgs.filter((o) => o.id !== org.id);

  return (
    <>
      <PageHeader
        title={`Hola, ${me.user.name.split(" ")[0]}`}
        description={`Estás operando con ${org.name} como ${org.roleLabel}.`}
      />

      {org.status === "suspended" && (
        <Alert variant="error">
          <strong>Organización suspendida.</strong> {org.suspendedReason ?? "Sin razón registrada."} Mientras dure la
          suspensión no se pueden emitir invitaciones ni realizar operaciones. Podés seguir consultando la información.
        </Alert>
      )}

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

      {others.length > 0 && (
        <section className="panel">
          <h2 className="panel__title">Aislamiento entre organizaciones</h2>
          <p className="panel__text">
            Tus permisos se recalculan por organización. Cambiá de contexto desde el selector del menú lateral y mirá cómo
            cambian el rol, los permisos y las secciones disponibles.
          </p>
          <ul className="minilist">
            {others.map((other) => (
              <li key={other.id} className="minilist__item">
                <span>
                  {other.name} — {other.roleLabel}
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
