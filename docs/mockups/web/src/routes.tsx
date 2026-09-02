import type { ReactElement } from "react";
import { createBrowserRouter, Navigate, RouterProvider } from "react-router-dom";
import { Shell } from "./components/shell/Shell";
import { canAccess, findNav } from "./lib/nav";
import { ShellProvider, useShell } from "./lib/shell-context";
import { ChooseContextPage } from "./pages/ChooseContextPage";
import { ConfirmPage } from "./pages/ConfirmPage";
import { HomePage } from "./pages/HomePage";
import { LoginPage } from "./pages/LoginPage";
import { RegisterPage } from "./pages/RegisterPage";
import { RegisterSentPage } from "./pages/RegisterSentPage";
import { ScreenPage } from "./pages/ScreenPage";

/** Raíz autenticada: carga /api/me, elige contexto si hace falta y monta el shell. */
function AuthenticatedRoot() {
  return (
    <ShellProvider>
      <ShellGate />
    </ShellProvider>
  );
}

function ShellGate() {
  const { me, loading, logout } = useShell();
  if (loading) return <div className="shell-loading" aria-busy="true" />;
  if (!me) return null; // el proveedor ya redirigió a /login
  if (!me.activeTenant) {
    if (me.tenants.length > 1) return <ChooseContextPage />;
    return (
      <div className="auth">
        <div className="auth__stack">
          <div className="brand">Plataforma</div>
          <section className="card">
            <h1 className="card__title">No tenés ningún contexto</h1>
            <p className="card__subtitle">Tu cuenta no pertenece a ninguna organización ni tiene espacio propio.</p>
            <div className="card__body">
              <button type="button" className="btn btn--ghost" onClick={() => void logout()}>
                Cerrar sesión
              </button>
            </div>
          </section>
        </div>
      </div>
    );
  }
  return <Shell />;
}

/** Filtro por permiso también en el router: redirección, no un 403. */
function Guard({ nav, children }: { nav: string; children: ReactElement }) {
  const { me } = useShell();
  const item = findNav(nav);
  if (!canAccess(item, me?.activeTenant ?? null)) return <Navigate to="/inicio" replace />;
  return children;
}

function screen(nav: string) {
  const item = findNav(nav);
  return (
    <Guard nav={nav}>
      <ScreenPage code={item.code} title={item.label} />
    </Guard>
  );
}

const router = createBrowserRouter([
  { path: "/", element: <Navigate to="/inicio" replace /> },
  { path: "/login", element: <LoginPage /> },
  { path: "/registro", element: <RegisterPage /> },
  { path: "/registro/enviado", element: <RegisterSentPage /> },
  { path: "/confirmar", element: <ConfirmPage /> },
  { path: "/app", element: <Navigate to="/inicio" replace /> },
  {
    element: <AuthenticatedRoot />,
    children: [
      { path: "/inicio", element: <HomePage /> },

      { path: "/plataforma/canal", element: screen("p1") },
      { path: "/plataforma/puesta-en-marcha", element: screen("p2") },
      { path: "/plataforma/numeros", element: screen("p3") },
      { path: "/plataforma/capacidades", element: screen("p4") },
      { path: "/plataforma/monitor", element: screen("p5") },

      { path: "/organizacion/integraciones", element: screen("o1") },
      {
        path: "/organizacion/integraciones/:modulo/credenciales",
        element: (
          <Guard nav="o1">
            <ScreenPage code="O2" title="Credenciales del módulo" />
          </Guard>
        ),
      },
      {
        path: "/organizacion/integraciones/:modulo/autorizacion",
        element: (
          <Guard nav="o1">
            <ScreenPage code="S2" title="Autorización externa" />
          </Guard>
        ),
      },
      { path: "/organizacion/capacidades", element: screen("o3") },
      { path: "/organizacion/miembros", element: screen("o4") },

      { path: "/mi/whatsapp", element: screen("m1") },
      {
        path: "/mi/whatsapp/vincular",
        element: (
          <Guard nav="m1">
            <ScreenPage code="M2" title="Vincular número" />
          </Guard>
        ),
      },

      { path: "/perfil", element: <ScreenPage code={null} title="Mi perfil" /> },
      { path: "/seguridad", element: <ScreenPage code={null} title="Seguridad y acceso" /> },

      { path: "*", element: <Navigate to="/inicio" replace /> },
    ],
  },
]);

export function AppRoutes() {
  return <RouterProvider router={router} />;
}
