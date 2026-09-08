import { useRef, useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { Alert, AuthLayout, Button, Card, Field } from "../components";
import { api, ApiError, NetworkError } from "../lib/api";

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

interface FieldErrors {
  email?: string;
  password?: string;
}

export function LoginPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const next = params.get("next");
  const expired = params.get("motivo") === "sesion";
  const passwordRef = useRef<HTMLInputElement>(null);

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [unconfirmed, setUnconfirmed] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  function validate(): FieldErrors {
    const validation: FieldErrors = {};
    if (!email.trim()) validation.email = "Ingresá tu correo.";
    else if (!EMAIL_PATTERN.test(email.trim())) validation.email = "Ingresá un correo válido.";
    if (!password) validation.password = "Ingresá tu contraseña.";
    return validation;
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) return;

    const validation = validate();
    setErrors(validation);
    setFormError(null);
    setUnconfirmed(false);
    if (validation.email || validation.password) return;

    setSubmitting(true);
    try {
      await api.login({ email: email.trim(), password });
      navigate(next ?? "/app", { replace: true });
    } catch (error) {
      if (error instanceof ApiError && error.status === 429) {
        setFormError(error.message);
      } else if (error instanceof ApiError && error.code === "unconfirmed") {
        setUnconfirmed(true);
        setFormError(error.message);
      } else if (error instanceof ApiError && error.status === 401) {
        setFormError("El correo o la contraseña no son correctos.");
        setPassword("");
        requestAnimationFrame(() => passwordRef.current?.focus());
      } else if (error instanceof NetworkError) {
        setFormError("No pudimos conectarnos con el servidor. Probá de nuevo.");
      } else {
        setFormError("No pudimos completar el ingreso. Probá de nuevo.");
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AuthLayout>
      <Card
        title="Iniciá sesión"
        footer={
          <>
            ¿No tenés cuenta? <Link to="/registro">Registrate</Link>
          </>
        }
      >
        <form className="form" onSubmit={handleSubmit} noValidate>
          {expired && (
            <Alert variant="info">
              Tu sesión venció o fue revocada. Ingresá de nuevo para seguir donde estabas.
            </Alert>
          )}
          {formError && <Alert variant="error">{formError}</Alert>}
          {unconfirmed && (
            <Alert variant="info">
              Buscá el correo de confirmación en la bandeja simulada del panel de demostración y seguí el enlace.
            </Alert>
          )}

          <Field
            label="Email"
            type="email"
            name="email"
            autoComplete="username"
            autoFocus
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            error={errors.email}
            disabled={submitting}
          />

          <Field
            ref={passwordRef}
            label="Contraseña"
            type={showPassword ? "text" : "password"}
            name="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            error={errors.password}
            disabled={submitting}
            action={
              <Button
                type="button"
                variant="text"
                onClick={() => setShowPassword((value) => !value)}
                aria-pressed={showPassword}
                tabIndex={-1}
              >
                {showPassword ? "Ocultar" : "Mostrar"}
              </Button>
            }
          />

          <Button type="submit" loading={submitting}>
            Entrar
          </Button>
        </form>
      </Card>
    </AuthLayout>
  );
}
