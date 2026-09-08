import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { AuthLayout, Button, Card } from "../components";

interface SentState {
  email?: string;
}

export function RegisterSentPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const email = (location.state as SentState | null)?.email;

  if (!email) return <Navigate to="/registro" replace />;

  return (
    <AuthLayout>
      <Card
        title="Revisá tu correo"
        subtitle={
          <>
            Si <strong>{email}</strong> no estaba registrado, te enviamos un enlace para confirmarlo. Vence en 24 horas.
          </>
        }
      >
        <Button variant="ghost" onClick={() => navigate("/login")}>
          Volver a iniciar sesión
        </Button>
      </Card>
    </AuthLayout>
  );
}
