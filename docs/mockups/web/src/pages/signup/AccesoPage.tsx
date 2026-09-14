import { useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { Alert, AuthLayout, Button, Card, Field, GoogleButton, OrDivider, Stepper } from "../../components";
import { TypeChip } from "../../components/onboarding/TypeChip";
import { api, ApiError } from "../../lib/api";
import { errorMessage } from "../../lib/session";
import { EMAIL_PATTERN, readType, SIGNUP_STEPS, signupPath } from "../../lib/signup";

/**
 * Segundo paso: cómo va a entrar. Con Google no hay código, porque Google ya
 * verificó la dirección. Con email se manda un código exista o no una cuenta: la
 * pantalla siguiente es la misma en los dos casos.
 */
export function AccesoPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const type = readType(params);
  const [email, setEmail] = useState("");
  const [error, setError] = useState("");
  const [formError, setFormError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  if (!type) return <Navigate to="/crear-cuenta" replace />;

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting || !type) return;
    const value = email.trim();
    if (!EMAIL_PATTERN.test(value)) {
      setError(value ? "Ingresá un correo válido." : "Ingresá tu correo.");
      return;
    }
    setError("");
    setFormError("");
    setSubmitting(true);
    try {
      const challenge = await api.signupStart({ email: value, type });
      navigate(signupPath("/crear-cuenta/codigo", { tipo: type, reto: challenge.id }));
    } catch (failure) {
      if (failure instanceof ApiError && failure.code === "invalid_email") setError(failure.message);
      else setFormError(errorMessage(failure));
      setSubmitting(false);
    }
  }

  return (
    <AuthLayout>
      <Stepper steps={SIGNUP_STEPS} current={1} />
      <Card
        title="Creá tu acceso"
        subtitle="Elegí cómo vas a entrar a tu cuenta."
        footer={
          <>
            ¿Ya tenés cuenta? <Link to="/entrar">Entrá</Link>
          </>
        }
      >
        <TypeChip type={type} />
        {formError && <Alert variant="error">{formError}</Alert>}
        <GoogleButton onClick={() => navigate(`/google?intent=crear&tipo=${type}`)} disabled={submitting} />
        <OrDivider />
        <form className="form" onSubmit={submit} noValidate>
          <Field
            label="Email"
            type="email"
            name="email"
            autoComplete="email"
            placeholder="nombre@ejemplo.com"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            hint="Te mandamos un código para verificarlo."
            hintMuted
            error={error}
            disabled={submitting}
          />
          <Button type="submit" loading={submitting}>
            Continuar
          </Button>
        </form>
      </Card>
    </AuthLayout>
  );
}
