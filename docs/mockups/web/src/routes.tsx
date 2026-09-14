import { useEffect, useState } from "react";
import { createBrowserRouter, Navigate, Outlet, RouterProvider, useLocation } from "react-router-dom";
import { Alert, Button, Card, Field } from "./components";
import { DemoPanel } from "./components/demo/DemoPanel";
import { AppShell } from "./components/shell/AppShell";
import { api } from "./lib/api";
import { SessionProvider } from "./lib/session";
import { ConfirmPage } from "./pages/ConfirmPage";
import { EntrarPage } from "./pages/EntrarPage";
import { GooglePage } from "./pages/GooglePage";
import { InvitationPage } from "./pages/InvitationPage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { HomePage } from "./pages/app/HomePage";
import { InvitationsPage } from "./pages/app/InvitationsPage";
import { MembersPage } from "./pages/app/MembersPage";
import { NewOrgPage } from "./pages/app/NewOrgPage";
import { NewPersonalPage } from "./pages/app/NewPersonalPage";
import { AccesoPage } from "./pages/signup/AccesoPage";
import { ClavePage } from "./pages/signup/ClavePage";
import { CodigoPage } from "./pages/signup/CodigoPage";
import { ConfigurarPage } from "./pages/signup/ConfigurarPage";
import { DatosPage } from "./pages/signup/DatosPage";
import { TipoPage } from "./pages/signup/TipoPage";
import { PlatformAdminsPage } from "./pages/platform/PlatformAdminsPage";
import { PlatformAuditPage } from "./pages/platform/PlatformAuditPage";
import { PlatformHomePage } from "./pages/platform/PlatformHomePage";
import { PlatformIdentitiesPage } from "./pages/platform/PlatformIdentitiesPage";
import { PlatformInvitationPage } from "./pages/platform/PlatformInvitationPage";
import { PlatformMfaPage } from "./pages/platform/PlatformMfaPage";
import { PlatformOrgsPage } from "./pages/platform/PlatformOrgsPage";
import { PlatformRetentionPage } from "./pages/platform/PlatformRetentionPage";
import { PlatformShell } from "./pages/platform/PlatformShell";

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
              ¿Ya tenés cuenta? <a href="/entrar">Iniciá sesión</a>
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

/** Las direcciones viejas siguen andando: llevan a la nueva conservando la consulta (?next, ?motivo). */
function LegacyRedirect({ to }: { to: string }) {
  const { search } = useLocation();
  return <Navigate to={`${to}${search}`} replace />;
}

// Cada ruta con sesión lleva su propia key en SessionProvider. Sin ella React
// reutiliza la instancia de la ruta anterior, que ocupa el mismo lugar del árbol, y
// la pantalla nueva arranca con el contexto viejo: /app creía que la identidad
// recién configurada seguía sin contexto y la devolvía a /configurar.

/** El panel de demostración acompaña a todas las pantallas, fuera del producto. */
function RootLayout() {
  return (
    <>
      <Outlet />
      <DemoPanel />
    </>
  );
}

const router = createBrowserRouter([
  {
    element: <RootLayout />,
    children: [
      { path: "/", element: <SistemaVisual /> },
      // Entrar y crear cuenta. El tipo se pregunta sólo al crear; entrar va directo.
      { path: "/entrar", element: <EntrarPage /> },
      { path: "/crear-cuenta", element: <TipoPage /> },
      { path: "/crear-cuenta/acceso", element: <AccesoPage /> },
      { path: "/crear-cuenta/codigo", element: <CodigoPage /> },
      { path: "/crear-cuenta/clave", element: <ClavePage /> },
      {
        path: "/crear-cuenta/datos",
        element: (
          <SessionProvider key="datos">
            <DatosPage />
          </SessionProvider>
        ),
      },
      {
        path: "/configurar",
        element: (
          <SessionProvider key="configurar">
            <ConfigurarPage />
          </SessionProvider>
        ),
      },
      { path: "/google", element: <GooglePage /> },
      { path: "/login", element: <LegacyRedirect to="/entrar" /> },
      { path: "/registro", element: <LegacyRedirect to="/crear-cuenta" /> },
      { path: "/confirmar", element: <ConfirmPage /> },
      { path: "/invitacion", element: <InvitationPage /> },

      // Cliente
      {
        path: "/app",
        element: (
          <SessionProvider key="app">
            <AppShell />
          </SessionProvider>
        ),
        children: [
          { index: true, element: <HomePage /> },
          { path: "miembros", element: <MembersPage /> },
          { path: "invitaciones", element: <InvitationsPage /> },
          { path: "organizaciones/nueva", element: <NewOrgPage /> },
          { path: "personal/nueva", element: <NewPersonalPage /> },
        ],
      },

      // Platform: contexto separado, sin enlaces desde el menú del cliente.
      { path: "/platform/invitacion", element: <PlatformInvitationPage /> },
      {
        path: "/platform/mfa",
        element: (
          <SessionProvider key="platform-mfa">
            <PlatformMfaPage />
          </SessionProvider>
        ),
      },
      {
        path: "/platform",
        element: (
          <SessionProvider key="platform">
            <PlatformShell />
          </SessionProvider>
        ),
        children: [
          { index: true, element: <PlatformHomePage /> },
          { path: "organizaciones", element: <PlatformOrgsPage /> },
          { path: "identidades", element: <PlatformIdentitiesPage /> },
          { path: "retencion", element: <PlatformRetentionPage /> },
          { path: "administradores", element: <PlatformAdminsPage /> },
          { path: "auditoria", element: <PlatformAuditPage /> },
        ],
      },

      { path: "*", element: <NotFoundPage /> },
    ],
  },
]);

export function AppRoutes() {
  return <RouterProvider router={router} />;
}
