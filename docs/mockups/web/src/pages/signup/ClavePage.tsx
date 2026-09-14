import { useEffect, useState, type FormEvent } from "react";
import { Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { Alert, AuthLayout, Button, Card, Field, GoogleButton, Stepper } from "../../components";
import { TypeChip } from "../../components/onboarding/TypeChip";
import { api, ApiError, type SignupChallenge } from "../../lib/api";
import { errorMessage } from "../../lib/session";
import { MIN_PASSWORD, readType, SIGNUP_STEPS, signupPath, TYPE_COPY } from "../../lib/signup";
import { ChallengeGone, Waiting } from "./shared";

/**
 * Después del código. La dirección ya está probada, así que acá sí se dice qué
 * cuenta hay: una nueva elige contraseña; una existente pone la suya, porque un
 * código solo nunca abre una cuenta; una que entra con Google sigue con Google.
 */
export function ClavePage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const type = readType(params);
  const reto = params.get("reto");

  const [challenge, setChallenge] = useState<SignupChallenge | null>(null);
  const [gone, setGone] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState("");
  const [formError, setFormError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!reto) return;
    let cancelled = false;
    api
      .signup(reto)
      .then((loaded) => {
        if (cancelled) return;
        if (!loaded.verified) navigate(signupPath("/crear-cuenta/codigo", { tipo: type, reto }), { replace: true });
        else setChallenge(loaded);
      })
      .catch((failure) => {
        if (!cancelled) setGone(errorMessage(failure));
      });
    return () => {
      cancelled = true;
    };
  }, [reto, type, navigate]);

  if (!type || !reto) return <Navigate to="/crear-cuenta" replace />;
  if (gone) return <ChallengeGone type={type} message={gone} />;
  if (!challenge) return <Waiting />;

  const account = challenge.account ?? "new";
  const noun = TYPE_COPY[type].noun;

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting || !reto) return;
    if (!password) {
      setError(account === "new" ? "Elegí una contraseña." : "Ingresá tu contraseña.");
      return;
    }
    if (account === "new" && password.length < MIN_PASSWORD) {
      setError(`La contraseña tiene que tener al menos ${MIN_PASSWORD} caracteres.`);
      return;
    }
    setError("");
    setFormError("");
    setSubmitting(true);
    try {
      await api.signupPassword(reto, password);
      navigate(signupPath("/crear-cuenta/datos", { tipo: type }), { replace: true });
    } catch (failure) {
      setSubmitting(false);
      if (failure instanceof ApiError && (failure.code === "challenge_closed" || failure.code === "code_expired")) {
        setGone(failure.message);
      } else if (failure instanceof ApiError && (failure.code === "invalid_credentials" || failure.code === "weak_password")) {
        setPassword("");
        setError(failure.message);
      } else {
        setFormError(errorMessage(failure));
      }
    }
  }

  if (account === "google") {
    return (
      <AuthLayout>
        <Stepper steps={SIGNUP_STEPS} current={2} />
        <Card
          title="Tu cuenta entra con Google"
          subtitle={
            <>
              Ya tenés una cuenta con <strong>{challenge.email}</strong>. Seguí con Google para agregarle {noun}.
            </>
          }
        >
          <TypeChip type={type} />
          <GoogleButton
            onClick={() => navigate(`/google?intent=crear&tipo=${type}&sugerida=${encodeURIComponent(challenge.email)}`)}
          />
        </Card>
      </AuthLayout>
    );
  }

  const existing = account === "password";

  return (
    <AuthLayout>
      <Stepper steps={SIGNUP_STEPS} current={2} />
      <Card
        title={existing ? "Ya tenés una cuenta" : "Elegí tu contraseña"}
        subtitle={
          existing ? (
            <>
              Con <strong>{challenge.email}</strong>. Ingresá tu contraseña y le agregamos {noun}.
            </>
          ) : (
            <>
              La vas a usar para entrar con <strong>{challenge.email}</strong>.
            </>
          )
        }
      >
        <TypeChip type={type} />
        {!existing && <Alert variant="success">Email verificado.</Alert>}
        {formError && <Alert variant="error">{formError}</Alert>}
        <form className="form" onSubmit={submit} noValidate>
          <Field
            label="Contraseña"
            type={showPassword ? "text" : "password"}
            name="password"
            autoComplete={existing ? "current-password" : "new-password"}
            autoFocus
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            hint={existing ? undefined : `Mínimo ${MIN_PASSWORD} caracteres.`}
            hintMuted
            error={error}
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
            {existing ? "Entrar y seguir" : "Continuar"}
          </Button>
        </form>
      </Card>
    </AuthLayout>
  );
}
