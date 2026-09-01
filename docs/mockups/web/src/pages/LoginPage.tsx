import { useState, type FormEvent } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { api, ApiError } from "../api/client";
import { Alert } from "../components/Alert";
import { AuthLayout } from "../components/AuthLayout";
import { Button } from "../components/Button";
import { Field, PasswordField } from "../components/Field";
import { useIdentity } from "../lib/identity";

export function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { refresh } = useIdentity();
  const params = new URLSearchParams(location.search);
  const justConfirmed = params.get("confirmado") === "1";
  const signedOut = params.get("salida") === "1";

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<"credentials" | "unconfirmed" | "generic" | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await api.signIn({ email, password });
      const context = await refresh();
      if (context?.activeTenant) navigate("/inicio", { replace: true });
      else navigate("/elegir-espacio", { replace: true });
    } catch (e) {
      if (e instanceof ApiError && e.code === "invalid_credentials") setError("credentials");
      else if (e instanceof ApiError && e.code === "email_not_confirmed") setError("unconfirmed");
      else setError("generic");
    } finally {
      setBusy(false);
    }
  }

  async function resend() {
    await api.resendConfirmation(email);
    navigate("/revisa-tu-correo", { state: { email } });
  }

  return (
    <AuthLayout
      title="Iniciá sesión"
      subtitle="Ingresá con tu correo y contraseña."
      aux={
        <>
          ¿Todavía no tenés cuenta? <Link to="/registrate">Registrate</Link>
        </>
      }
    >
      <form className="form" onSubmit={submit} noValidate>
        {justConfirmed && !error && (
          <Alert tone="success">Tu correo quedó confirmado. Ya podés iniciar sesión.</Alert>
        )}
        {signedOut && !error && <Alert tone="info">Cerraste sesión correctamente.</Alert>}
        {error === "credentials" && <Alert tone="danger">El correo o la contraseña no coinciden.</Alert>}
        {error === "unconfirmed" && (
          <Alert tone="danger">
            Todavía no confirmaste tu correo. Revisá tu bandeja de entrada.{" "}
            <button type="button" onClick={resend}>Pedir un enlace nuevo</button>
          </Alert>
        )}
        {error === "generic" && <Alert tone="danger">No pudimos iniciar sesión. Intentá de nuevo en unos minutos.</Alert>}

        <Field
          label="Correo electrónico"
          type="email"
          name="email"
          autoComplete="email"
          placeholder="nombre@empresa.com.ar"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
          autoFocus
        />
        <PasswordField
          label="Contraseña"
          name="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
          trailing={
            <Link className="field__link" to="/recuperar">
              ¿Olvidaste tu contraseña?
            </Link>
          }
        />
        <Button type="submit" block loading={busy}>
          Continuar
        </Button>
      </form>
    </AuthLayout>
  );
}
