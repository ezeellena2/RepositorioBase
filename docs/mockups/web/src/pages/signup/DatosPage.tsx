import { Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { AuthLayout, Button, Card, Stepper } from "../../components";
import { CompanyForm } from "../../components/onboarding/CompanyForm";
import { PersonalForm } from "../../components/onboarding/PersonalForm";
import { TypeChip } from "../../components/onboarding/TypeChip";
import type { Me } from "../../lib/api";
import { useSession } from "../../lib/session";
import { readType, SETUP_STEPS, SIGNUP_STEPS } from "../../lib/signup";
import { Waiting } from "./shared";

/**
 * Último paso: los datos del tipo elegido. Ya hay sesión, llegue la persona por
 * Google, por código y contraseña, o desde «Terminá de configurar tu cuenta».
 */
export function DatosPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const type = readType(params);
  const fromSetup = params.get("desde") === "configurar";
  const { me, loading, switchOrg, switching } = useSession();

  if (!type) return <Navigate to="/configurar" replace />;
  if (loading || !me) return <Waiting />;

  const steps = fromSetup ? SETUP_STEPS : SIGNUP_STEPS;
  const current = steps.length - 1;
  const personal = me.orgs.find((org) => org.type === "persona");
  const created = (_next: Me) => navigate("/app", { replace: true, state: { created: type } });

  // A lo sumo una cuenta personal: quien ya la tiene no vuelve a cargar su DNI.
  if (type === "persona" && personal) {
    return (
      <AuthLayout>
        <Stepper steps={steps} current={current} />
        <Card title="Ya tenés una cuenta personal" subtitle={`${personal.name} ya está en tu cuenta. No hace falta crear otra.`}>
          <Button
            loading={switching}
            onClick={async () => {
              await switchOrg(personal.id);
              navigate("/app", { replace: true });
            }}
          >
            Ir a tu cuenta personal
          </Button>
          <Button variant="ghost" onClick={() => navigate(`/crear-cuenta/datos?tipo=empresa${fromSetup ? "&desde=configurar" : ""}`)}>
            Crear una empresa
          </Button>
        </Card>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout>
      <Stepper steps={steps} current={current} />
      <Card
        title={type === "persona" ? "Tus datos" : "Datos de la empresa"}
        subtitle={type === "persona" ? "Así te identificamos en tu cuenta personal." : "Vas a quedar como titular de la empresa."}
        footer={<>Entraste como {me.user.email}.</>}
      >
        <TypeChip type={type} changeTo="/configurar" />
        {type === "persona" ? (
          <PersonalForm submitLabel="Crear cuenta personal" onCreated={created} />
        ) : (
          <CompanyForm submitLabel="Crear empresa" onCreated={created} />
        )}
      </Card>
    </AuthLayout>
  );
}
