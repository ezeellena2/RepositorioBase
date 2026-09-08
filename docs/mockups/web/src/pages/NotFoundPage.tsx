import { useNavigate } from "react-router-dom";
import { AuthLayout, Button, Card } from "../components";

export function NotFoundPage() {
  const navigate = useNavigate();
  return (
    <AuthLayout>
      <Card title="No encontramos esa pantalla" subtitle="La dirección no corresponde a ninguna pantalla del mockup.">
        <Button variant="ghost" onClick={() => navigate("/app")}>
          Ir a la aplicación
        </Button>
      </Card>
    </AuthLayout>
  );
}
