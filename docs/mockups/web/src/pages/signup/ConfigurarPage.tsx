import { Link, Navigate, useNavigate } from "react-router-dom";
import { Alert, AuthLayout, Button, Card, ChoiceCard, Stepper } from "../../components";
import type { OrgType } from "../../lib/api";
import { useSession } from "../../lib/session";
import { SETUP_STEPS, TYPE_COPY } from "../../lib/signup";
import { Waiting } from "./shared";

const TYPES: OrgType[] = ["persona", "empresa"];

/**
 * «Terminá de configurar tu cuenta». Lo ve quien tiene sesión y ningún contexto:
 * el que entró con Google por primera vez o el que dejó la mitad. Es la misma
 * elección del primer paso, sin volver a autenticarse.
 */
export function ConfigurarPage() {
  const navigate = useNavigate();
  const { me, loading, logout } = useSession();

  if (loading || !me) return <Waiting />;
  if (me.orgs.length > 0) return <Navigate to="/app" replace />;

  return (
    <AuthLayout>
      <Stepper steps={SETUP_STEPS} current={0} />
      <Card
        title="Terminá de configurar tu cuenta"
        subtitle={
          <>
            Entraste como <strong>{me.user.email}</strong>. ¿Qué querés crear?
          </>
        }
        footer={
          <Button type="button" variant="text" onClick={() => void logout()}>
            Salir
          </Button>
        }
      >
        {me.pendingInvitations.map((invitation) => (
          <Alert key={invitation.token} variant="info">
            Te invitaron a {invitation.orgName}.{" "}
            <Link to={`/invitacion?token=${invitation.token}`}>Ver la invitación</Link>
          </Alert>
        ))}
        {TYPES.map((type) => (
          <ChoiceCard
            key={type}
            kind={type}
            title={TYPE_COPY[type].title}
            detail={TYPE_COPY[type].detail}
            onChoose={() => navigate(`/crear-cuenta/datos?tipo=${type}&desde=configurar`)}
          />
        ))}
      </Card>
    </AuthLayout>
  );
}
