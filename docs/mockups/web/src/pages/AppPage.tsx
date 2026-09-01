import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { AuthLayout, Button, Card } from "../components";
import { api, type Me } from "../lib/api";

// Destino provisorio del login. Se reemplaza por la aplicación real.
export function AppPage() {
  const navigate = useNavigate();
  const [me, setMe] = useState<Me | null>(null);

  useEffect(() => {
    api
      .me()
      .then(setMe)
      .catch(() => navigate("/login", { replace: true }));
  }, [navigate]);

  if (!me) return null;

  async function logout() {
    await api.logout();
    navigate("/login", { replace: true });
  }

  return (
    <AuthLayout>
      <Card title={`Hola, ${me.user.name}`} subtitle={me.user.email}>
        <Button variant="ghost" onClick={logout}>
          Cerrar sesión
        </Button>
      </Card>
    </AuthLayout>
  );
}
