import { useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { api } from "../api/client";
import { AuthLayout } from "../components/AuthLayout";
import { Button, LinkButton } from "../components/Button";
import { MailIcon } from "../components/Icons";

export function CheckEmailPage() {
  const location = useLocation();
  const email = (location.state as { email?: string } | null)?.email ?? "";
  const [resent, setResent] = useState(false);
  const [busy, setBusy] = useState(false);

  async function resend() {
    setBusy(true);
    try {
      await api.resendConfirmation(email);
      setResent(true);
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthLayout
      aux={
        <>
          ¿Te equivocaste de correo? <Link to="/registrate">Volvé a registrarte</Link>
        </>
      }
    >
      <div className="status">
        <span className="status__icon">
          <MailIcon size={26} />
        </span>
        <h1 className="auth__title">Revisá tu correo</h1>
        <p className="auth__subtitle">
          Te enviamos un enlace{email ? <> a <span className="status__email">{email}</span></> : null}. Hasta que
          confirmes no vas a poder iniciar sesión.
        </p>
        <div className="status__actions">
          <LinkButton to="/iniciar-sesion" block>
            Ir a iniciar sesión
          </LinkButton>
          <Button variant="secondary" block onClick={resend} loading={busy} disabled={!email || resent}>
            {resent ? "Enlace reenviado" : "Reenviar el enlace"}
          </Button>
        </div>
        <div className="demo-note">
          En este entorno de demostración no se envían correos reales. Podés abrir la{" "}
          <Link to="/correo">bandeja de correo</Link> para ver el mensaje y confirmar desde ahí.
        </div>
      </div>
    </AuthLayout>
  );
}
