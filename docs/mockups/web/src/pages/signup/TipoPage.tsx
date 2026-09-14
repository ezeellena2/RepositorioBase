import { Link, useNavigate } from "react-router-dom";
import { AuthLayout, Card, ChoiceCard, Stepper } from "../../components";
import type { OrgType } from "../../lib/api";
import { SIGNUP_STEPS, signupPath, TYPE_COPY } from "../../lib/signup";

const TYPES: OrgType[] = ["persona", "empresa"];

/** Primer paso de crear cuenta: qué se crea. Es la única vez que se pregunta. */
export function TipoPage() {
  const navigate = useNavigate();

  return (
    <AuthLayout>
      <Stepper steps={SIGNUP_STEPS} current={0} />
      <Card
        title="¿Qué querés crear?"
        subtitle="Después podés sumar la otra desde tu cuenta."
        footer={
          <>
            ¿Ya tenés cuenta? <Link to="/entrar">Entrá</Link>
          </>
        }
      >
        {TYPES.map((type) => (
          <ChoiceCard
            key={type}
            kind={type}
            title={TYPE_COPY[type].title}
            detail={TYPE_COPY[type].detail}
            onChoose={() => navigate(signupPath("/crear-cuenta/acceso", { tipo: type }))}
          />
        ))}
      </Card>
    </AuthLayout>
  );
}
