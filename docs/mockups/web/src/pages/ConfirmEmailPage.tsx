import { useEffect, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { api, ApiError } from "../api/client";
import { AuthLayout } from "../components/AuthLayout";
import { LinkButton } from "../components/Button";
import { AlertIcon, CheckIcon } from "../components/Icons";

type State = "working" | "ok" | "expired" | "invalid";

export function ConfirmEmailPage() {
  const location = useLocation();
  const token = new URLSearchParams(location.search).get("token") ?? "";
  const [state, setState] = useState<State>("working");

  useEffect(() => {
    let cancelled = false;
    if (!token) {
      setState("invalid");
      return;
    }
    api
      .confirmEmail(token)
      .then(() => !cancelled && setState("ok"))
      .catch((e) => {
        if (cancelled) return;
        setState(e instanceof ApiError && e.code === "expired_token" ? "expired" : "invalid");
      });
    return () => {
      cancelled = true;
    };
  }, [token]);

  if (state === "working") {
    return (
      <AuthLayout>
        <div className="status">
          <h1 className="auth__title">Confirmando tu correo</h1>
          <p className="auth__subtitle">Esto tarda un instante.</p>
        </div>
      </AuthLayout>
    );
  }

  if (state === "ok") {
    return (
      <AuthLayout>
        <div className="status">
          <span className="status__icon status__icon--success">
            <CheckIcon size={26} />
          </span>
          <h1 className="auth__title">Tu correo quedó confirmado</h1>
          <p className="auth__subtitle">Tu cuenta ya está activa. Iniciá sesión para empezar a configurar tu espacio.</p>
          <div className="status__actions">
            <LinkButton to="/iniciar-sesion?confirmado=1" block>
              Iniciar sesión
            </LinkButton>
          </div>
        </div>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout
      aux={
        <>
          ¿Necesitás ayuda? <Link to="/iniciar-sesion">Volvé a iniciar sesión</Link>
        </>
      }
    >
      <div className="status">
        <span className="status__icon status__icon--danger">
          <AlertIcon size={26} />
        </span>
        <h1 className="auth__title">{state === "expired" ? "El enlace venció" : "El enlace no es válido"}</h1>
        <p className="auth__subtitle">
          {state === "expired"
            ? "Los enlaces de confirmación duran 24 horas. Iniciá sesión y pedí uno nuevo."
            : "Revisá que hayas copiado la dirección completa o pedí un enlace nuevo desde el inicio de sesión."}
        </p>
        <div className="status__actions">
          <LinkButton to="/iniciar-sesion" block variant="secondary">
            Ir a iniciar sesión
          </LinkButton>
        </div>
      </div>
    </AuthLayout>
  );
}
