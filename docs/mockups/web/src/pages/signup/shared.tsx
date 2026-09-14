import { useNavigate } from "react-router-dom";
import { AuthLayout, Button, Card, Spinner } from "../../components";
import type { OrgType } from "../../lib/api";
import { signupPath } from "../../lib/signup";

/** Mientras se lee el desafío o la sesión, la tarjeta ocupa su lugar en vez de saltar. */
export function Waiting() {
  return (
    <AuthLayout>
      <Card title="Un momento">
        <div className="card__spinner">
          <Spinner />
        </div>
      </Card>
    </AuthLayout>
  );
}

/** Un código que ya no se puede usar: vencido de todo, cerrado o de otro recorrido. */
export function ChallengeGone({ type, message }: { type: OrgType; message: string }) {
  const navigate = useNavigate();
  return (
    <AuthLayout>
      <Card title="Este código ya no sirve" subtitle={message}>
        <Button onClick={() => navigate(signupPath("/crear-cuenta/acceso", { tipo: type }), { replace: true })}>
          Empezar de nuevo
        </Button>
      </Card>
    </AuthLayout>
  );
}
