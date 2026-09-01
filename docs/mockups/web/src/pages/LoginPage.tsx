import { useRef, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Alert, AuthLayout, Button, Card, Field } from "../components";
import { api, ApiError } from "../lib/api";

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

interface FieldErrors {
  email?: string;
  password?: string;
}

export function LoginPage() {
  const navigate = useNavigate();
  const passwordRef = useRef<HTMLInputElement>(null);

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  function validate(): FieldErrors {
    const next: FieldErrors = {};
    if (!email.trim()) next.email = "Ingresá tu correo.";
    else if (!EMAIL_PATTERN.test(email.trim())) next.email = "Ingresá un correo válido.";
    if (!password) next.password = "Ingresá tu contraseña.";
    return next;
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) return;

    const validation = validate();
    setErrors(validation);
    setFormError(null);
    if (validation.email || validation.password) return;

    setSubmitting(true);
    try {
      await api.login({ email: email.trim(), password });
      navigate("/app", { replace: true });
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        setFormError("El correo o la contraseña no son correctos.");
        setPassword("");
        // El input recién se habilita cuando termina el render.
        requestAnimationFrame(() => passwordRef.current?.focus());
      } else {
        setFormError("No pudimos conectarnos. Probá de nuevo.");
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
          {formError && <Alert variant="error">{formError}</Alert>}

          <Field
            label="Email"
            type="email"
            name="email"
            autoComplete="username"
            autoFocus
            value={email}
            onChange={(e) => setEmail(e.target.value)}
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
            onChange={(e) => setPassword(e.target.value)}
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
            Entrar
          </Button>
        </form>
      </Card>
    </AuthLayout>
  );
}
