import { useEffect, useState } from "react";
import { createBrowserRouter, RouterProvider } from "react-router-dom";
import { Alert, Button, Card, Field } from "./components";
import { api } from "./lib/api";

// Página de verificación del sistema visual. Se borra cuando existan las pantallas reales.
function SistemaVisual() {
  const [apiOk, setApiOk] = useState<boolean | null>(null);

  useEffect(() => {
    api
      .health()
      .then(() => setApiOk(true))
      .catch(() => setApiOk(false));
  }, []);

  return (
    <div className="auth">
      <div className="auth__stack">
        <div className="brand">Plataforma</div>
        <Card
          title="Sistema visual"
          subtitle="Cada componente base en cada uno de sus estados."
          footer={
            <>
              ¿Ya tenés cuenta? <a href="#">Iniciá sesión</a>
            </>
          }
        >
          <Field label="Normal" placeholder="nombre@empresa.com.ar" />
          <Field label="Con foco" defaultValue="ana@ejemplo.com" autoFocus />
          <Field label="Con error" defaultValue="20-1234" error="El CUIT tiene que tener 11 dígitos." />
          <Field label="Deshabilitado" defaultValue="Persona física" disabled />
          <Field
            label="Con acción"
            type="password"
            defaultValue="contraseña"
            action={
              <Button variant="text" type="button">
                Mostrar
              </Button>
            }
          />

          <Button>Continuar</Button>
          <Button loading>Continuar</Button>
          <Button variant="ghost">Cancelar</Button>

          <Alert variant="error">El correo o la contraseña no coinciden.</Alert>
          <Alert variant="success">Tu correo quedó confirmado.</Alert>
          <Alert variant="info">
            {apiOk === null ? "Consultando la API." : apiOk ? "La API responde en /api/health." : "La API no responde."}
          </Alert>
        </Card>
      </div>
    </div>
  );
}

const router = createBrowserRouter([{ path: "/", element: <SistemaVisual /> }]);

export function AppRoutes() {
  return <RouterProvider router={router} />;
}
