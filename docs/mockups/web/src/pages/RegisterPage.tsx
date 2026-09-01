import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Alert, AuthLayout, Button, Card, Field } from "../components";
import { api, ApiError } from "../lib/api";
import { format, parse } from "../lib/cuit";

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const MIN_PASSWORD = 12;
const CUIT_ERROR = "Revisá el CUIT: el número no es válido.";

interface FieldErrors {
  cuit?: string;
  name?: string;
  email?: string;
  password?: string;
}

const NAME_LABEL = {
  persona: "Nombre y apellido",
  empresa: "Razón social",
  none: "Nombre o razón social",
} as const;

const KIND_LABEL = {
  persona: "Persona física",
  empresa: "Empresa",
} as const;

export function RegisterPage() {
  const navigate = useNavigate();

  const [cuit, setCuit] = useState("");
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<"cuit_taken" | "network" | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const parsedCuit = parse(cuit);
  const cuitKind = parsedCuit?.kind ?? "none";

  function handleCuitBlur() {
    if (!cuit.trim()) return;
    if (parsedCuit) {
      setCuit(format(parsedCuit.normalized));
      setErrors((e) => ({ ...e, cuit: undefined }));
    } else {
      setErrors((e) => ({ ...e, cuit: CUIT_ERROR }));
    }
  }

  function validate(): FieldErrors {
    const next: FieldErrors = {};
    if (!parsedCuit) next.cuit = CUIT_ERROR;
    if (!name.trim()) next.name = "Ingresá el nombre.";
    if (!email.trim()) next.email = "Ingresá tu correo.";
    else if (!EMAIL_PATTERN.test(email.trim())) next.email = "Ingresá un correo válido.";
    if (password.length < MIN_PASSWORD) next.password = `La contraseña tiene que tener al menos ${MIN_PASSWORD} caracteres.`;
    return next;
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) return;

    const validation = validate();
    setErrors(validation);
    setFormError(null);
    if (Object.values(validation).some(Boolean) || !parsedCuit) return;

    setSubmitting(true);
    try {
      await api.register({
        cuit: parsedCuit.normalized,
        name: name.trim(),
        email: email.trim(),
        password,
      });
      navigate("/registro/enviado", { state: { email: email.trim() } });
    } catch (error) {
      if (error instanceof ApiError && error.code === "cuit_taken") {
        setFormError("cuit_taken");
      } else if (error instanceof ApiError && error.code === "invalid_cuit") {
        setErrors((e) => ({ ...e, cuit: CUIT_ERROR }));
      } else {
        setFormError("network");
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AuthLayout>
      <Card
        title="Creá tu cuenta"
        subtitle="Con tu CUIT sabemos si sos persona o empresa."
        footer={
          <>
            ¿Ya tenés cuenta? <Link to="/login">Iniciá sesión</Link>
          </>
        }
      >
        <form className="form" onSubmit={handleSubmit} noValidate>
          {formError === "cuit_taken" && (
            <Alert variant="error">
              Ese CUIT ya está registrado. Si es tuyo, <Link to="/login">iniciá sesión</Link>.
            </Alert>
          )}
          {formError === "network" && <Alert variant="error">No pudimos conectarnos. Probá de nuevo.</Alert>}

          <Field
            label="CUIT"
            type="text"
            name="cuit"
            inputMode="numeric"
            autoComplete="off"
            autoFocus
            placeholder="20-12345678-9"
            value={cuit}
            onChange={(e) => setCuit(e.target.value)}
            onBlur={handleCuitBlur}
            hint={parsedCuit ? KIND_LABEL[parsedCuit.kind] : undefined}
            error={errors.cuit}
            disabled={submitting}
          />

          <Field
            label={NAME_LABEL[cuitKind]}
            type="text"
            name="name"
            autoComplete={cuitKind === "empresa" ? "organization" : "name"}
            value={name}
            onChange={(e) => setName(e.target.value)}
            error={errors.name}
            disabled={submitting}
          />

          <Field
            label="Email"
            type="email"
            name="email"
            autoComplete="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            error={errors.email}
            disabled={submitting}
          />

          <Field
            label="Contraseña"
            type={showPassword ? "text" : "password"}
            name="password"
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            hint={`Mínimo ${MIN_PASSWORD} caracteres.`}
            hintMuted
            error={errors.password}
            disabled={submitting}
            action={
              <Button
                type="button"
                variant="text"
                onClick={() => setShowPassword((v) => !v)}
                aria-pressed={showPassword}
                tabIndex={-1}
              >
                {showPassword ? "Ocultar" : "Mostrar"}
              </Button>
            }
          />

          <Button type="submit" loading={submitting}>
            Crear cuenta
          </Button>
        </form>
      </Card>
    </AuthLayout>
  );
}
