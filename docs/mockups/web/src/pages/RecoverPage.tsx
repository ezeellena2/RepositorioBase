import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { AuthLayout } from "../components/AuthLayout";
import { Button } from "../components/Button";
import { Field } from "../components/Field";
import { MailIcon } from "../components/Icons";

export function RecoverPage() {
  const [email, setEmail] = useState("");
  const [sent, setSent] = useState(false);

  function submit(event: FormEvent) {
    event.preventDefault();
    setSent(true);
  }

  if (sent) {
    return (
      <AuthLayout aux={<Link to="/iniciar-sesion">Volver a iniciar sesión</Link>}>
        <div className="status">
          <span className="status__icon">
            <MailIcon size={26} />
          </span>
          <h1 className="auth__title">Revisá tu correo</h1>
          <p className="auth__subtitle">
            Si <span className="status__email">{email}</span> tiene una cuenta, vas a recibir un enlace para crear una
            contraseña nueva.
          </p>
        </div>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout
      title="Recuperá tu contraseña"
      subtitle="Ingresá tu correo y te enviamos un enlace para crear una nueva."
      aux={<Link to="/iniciar-sesion">Volver a iniciar sesión</Link>}
    >
      <form className="form" onSubmit={submit} noValidate>
        <Field
          label="Correo electrónico"
          type="email"
          autoComplete="email"
          placeholder="nombre@empresa.com.ar"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
          autoFocus
        />
        <Button type="submit" block>
          Enviar enlace
        </Button>
      </form>
    </AuthLayout>
  );
}
