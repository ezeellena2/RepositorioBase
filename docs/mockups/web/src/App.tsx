import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import { useIdentity } from "./lib/identity";
import { CheckEmailPage } from "./pages/CheckEmailPage";
import { ChooseWorkspacePage } from "./pages/ChooseWorkspacePage";
import { ConfirmEmailPage } from "./pages/ConfirmEmailPage";
import { HomePage } from "./pages/HomePage";
import { LoginPage } from "./pages/LoginPage";
import { MailboxPage } from "./pages/MailboxPage";
import { RecoverPage } from "./pages/RecoverPage";
import { RegisterPage } from "./pages/RegisterPage";

function Protected({ children, needsTenant }: { children: React.ReactElement; needsTenant: boolean }) {
  const { context, loading } = useIdentity();
  const location = useLocation();
  if (loading) return <div className="skeleton" />;
  if (!context) return <Navigate to="/iniciar-sesion" replace state={{ from: location }} />;
  if (needsTenant && !context.activeTenant) return <Navigate to="/elegir-espacio" replace />;
  return children;
}

function PublicOnly({ children }: { children: React.ReactElement }) {
  const { context, loading } = useIdentity();
  if (loading) return <div className="skeleton" />;
  if (context) return <Navigate to={context.activeTenant ? "/inicio" : "/elegir-espacio"} replace />;
  return children;
}

export function App() {
  return (
    <Routes>
      <Route path="/" element={<Navigate to="/iniciar-sesion" replace />} />
      <Route
        path="/iniciar-sesion"
        element={
          <PublicOnly>
            <LoginPage />
          </PublicOnly>
        }
      />
      <Route
        path="/registrate"
        element={
          <PublicOnly>
            <RegisterPage />
          </PublicOnly>
        }
      />
      <Route path="/recuperar" element={<RecoverPage />} />
      <Route path="/revisa-tu-correo" element={<CheckEmailPage />} />
      <Route path="/confirmar" element={<ConfirmEmailPage />} />
      <Route path="/correo" element={<MailboxPage />} />
      <Route
        path="/elegir-espacio"
        element={
          <Protected needsTenant={false}>
            <ChooseWorkspacePage />
          </Protected>
        }
      />
      <Route
        path="/inicio"
        element={
          <Protected needsTenant>
            <HomePage />
          </Protected>
        }
      />
      <Route path="*" element={<Navigate to="/iniciar-sesion" replace />} />
    </Routes>
  );
}
