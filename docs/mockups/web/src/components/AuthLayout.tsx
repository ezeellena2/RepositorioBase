import { type ReactNode } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api } from "../api/client";
import { Wordmark } from "./Wordmark";

interface Props {
  title?: string;
  subtitle?: ReactNode;
  children: ReactNode;
  aux?: ReactNode;
  wide?: boolean;
}

export function AuthLayout({ title, subtitle, children, aux, wide }: Props) {
  const navigate = useNavigate();

  async function reset() {
    await api.resetDemo();
    navigate("/iniciar-sesion", { replace: true });
    window.location.reload();
  }

  return (
    <div className="auth">
      <main className="auth__main">
        <Wordmark size="lg" to="/iniciar-sesion" />
        <section className={`auth__card${wide ? " auth__card--wide" : ""}`}>
          {title && <h1 className="auth__title">{title}</h1>}
          {subtitle && <p className="auth__subtitle">{subtitle}</p>}
          {children}
        </section>
        {aux && <p className="auth__aux">{aux}</p>}
      </main>
      <footer className="auth__footer">
        <span>Entorno de demostración</span>
        <Link to="/correo">Bandeja de correo</Link>
        <button type="button" onClick={reset}>
          Reiniciar datos
        </button>
      </footer>
    </div>
  );
}
