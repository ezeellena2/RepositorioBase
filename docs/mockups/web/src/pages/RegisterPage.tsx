import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api, ApiError } from "../api/client";
import { Alert } from "../components/Alert";
import { AuthLayout } from "../components/AuthLayout";
import { Button } from "../components/Button";
import { Field, PasswordField } from "../components/Field";
import { formatCuit, kindFromCuit } from "../lib/cuit";

type Errors = Partial<Record<"cuit" | "displayName" | "email" | "password", string>>;

export function RegisterPage() {
  const navigate = useNavigate();
  const [cuit, setCuit] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [errors, setErrors] = useState<Errors>({});
  const [generic, setGeneric] = useState(false);
  const [busy, setBusy] = useState(false);

  const kind = kindFromCuit(cuit);

  const cuitHint =
    kind === "persona" ? (
      <span className="pill pill--primary">Persona física</span>
    ) : kind === "empresa" ? (
      <span className="pill pill--primary">Empresa</span>
    ) : kind === "unknown" ? (
      "No reconocemos ese tipo de CUIT."
    ) : (
      "Con el CUIT sabemos si sos una persona o una empresa."
    );

  const nameLabel = kind === "empresa" ? "Razón social" : kind === "persona" ? "Nombre y apellido" : "Nombre";
  const namePlaceholder = kind === "empresa" ? "Acme S.A." : "Ana Pereyra";

  async function submit(event: FormEvent) {
    event.preventDefault();
    setErrors({});
    setGeneric(false);
    setBusy(true);
    try {
      await api.register({ cuit, displayName, email, password });
      navigate("/revisa-tu-correo", { state: { email } });
    } catch (e) {
      if (e instanceof ApiError && e.code === "validation_failed") {
        const next: Errors = {};
        for (const [field, messages] of Object.entries(e.errors)) {
          next[field as keyof Errors] = messages[0];
        }
        setErrors(next);
      } else {
        setGeneric(true);
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthLayout
      title="Creá tu cuenta"
      subtitle="Solo te pedimos lo necesario. El resto lo configurás adentro."
      aux={
        <>
          ¿Ya tenés cuenta? <Link to="/iniciar-sesion">Iniciá sesión</Link>
        </>
      }
    >
      <form className="form" onSubmit={submit} noValidate>
        {generic && <Alert tone="danger">No pudimos crear la cuenta. Intentá de nuevo en unos minutos.</Alert>}

        <Field
          label="CUIT"
          inputMode="numeric"
          autoComplete="off"
          placeholder="20-12345678-9"
          value={formatCuit(cuit)}
          onChange={(e) => setCuit(e.target.value)}
          hint={cuitHint}
          error={errors.cuit}
          required
          autoFocus
        />
        <Field
          label={nameLabel}
          name="name"
          autoComplete={kind === "empresa" ? "organization" : "name"}
          placeholder={namePlaceholder}
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          error={errors.displayName}
          required
        />
        <Field
          label="Correo electrónico"
          type="email"
          name="email"
          autoComplete="email"
          placeholder="nombre@empresa.com.ar"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          error={errors.email}
          required
        />
        <PasswordField
          label="Contraseña"
          name="password"
          autoComplete="new-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          hint="Al menos 8 caracteres."
          error={errors.password}
          required
        />
        <Button type="submit" block loading={busy}>
          Crear cuenta
        </Button>
        <p className="field__hint" style={{ textAlign: "center" }}>
          Al crear la cuenta aceptás los <a href="#terminos">Términos</a> y la{" "}
          <a href="#privacidad">Política de privacidad</a>.
        </p>
      </form>
    </AuthLayout>
  );
}
