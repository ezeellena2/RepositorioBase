import { useEffect, useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { Alert, AuthLayout, Button, Card, Field, Stepper } from "../../components";
import { TypeChip } from "../../components/onboarding/TypeChip";
import { api, ApiError, type SignupChallenge } from "../../lib/api";
import { errorMessage } from "../../lib/session";
import { readType, SIGNUP_STEPS, signupPath } from "../../lib/signup";
import { ChallengeGone, Waiting } from "./shared";

/**
 * Tercer paso con email: el código. No dice nada sobre si la dirección tiene
 * cuenta; eso recién aparece cuando el código prueba que la dirección es de quien
 * lo escribe.
 */
export function CodigoPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const type = readType(params);
  const reto = params.get("reto");

  const [challenge, setChallenge] = useState<SignupChallenge | null>(null);
  const [gone, setGone] = useState("");
  const [code, setCode] = useState("");
  const [codeError, setCodeError] = useState("");
  const [notice, setNotice] = useState("");
  const [locked, setLocked] = useState(false);
  const [verifying, setVerifying] = useState(false);
  const [resending, setResending] = useState(false);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (!reto) return;
    let cancelled = false;
    api
      .signup(reto)
      .then((loaded) => {
        if (cancelled) return;
        // Volver atrás desde la contraseña no pide el código otra vez.
        if (loaded.verified) navigate(signupPath("/crear-cuenta/clave", { tipo: type, reto }), { replace: true });
        else setChallenge(loaded);
      })
      .catch((error) => {
        if (!cancelled) setGone(errorMessage(error));
      });
    return () => {
      cancelled = true;
    };
  }, [reto, type, navigate]);

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  if (!type || !reto) return <Navigate to="/crear-cuenta" replace />;
  if (gone) return <ChallengeGone type={type} message={gone} />;
  if (!challenge) return <Waiting />;

  const wait = Math.max(0, Math.ceil((Date.parse(challenge.resendAvailableAt) - now) / 1000));

  async function verify(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (verifying || !reto) return;
    if (!/^\d{6}$/.test(code)) {
      setCodeError("Escribí los 6 dígitos del código.");
      return;
    }
    setCodeError("");
    setNotice("");
    setVerifying(true);
    try {
      await api.signupVerify(reto, code);
      navigate(signupPath("/crear-cuenta/clave", { tipo: type, reto }));
    } catch (error) {
      setVerifying(false);
      if (error instanceof ApiError && (error.code === "challenge_closed" || error.code === "challenge_not_found")) {
        setGone(error.message);
        return;
      }
      if (error instanceof ApiError && error.code === "code_locked") setLocked(true);
      setCode("");
      setCodeError(errorMessage(error));
    }
  }

  async function resend() {
    if (!reto) return;
    setResending(true);
    try {
      setChallenge(await api.signupResend(reto));
      setLocked(false);
      setCode("");
      setCodeError("");
      setNotice("Te mandamos un código nuevo. El anterior ya no sirve.");
    } catch (error) {
      if (error instanceof ApiError && error.code === "challenge_closed") setGone(error.message);
      else setNotice(errorMessage(error));
    } finally {
      setResending(false);
    }
  }

  return (
    <AuthLayout>
      <Stepper steps={SIGNUP_STEPS} current={2} />
      <Card
        title="Revisá tu correo"
        subtitle={
          <>
            Te mandamos un código de 6 dígitos a <strong>{challenge.email}</strong>.{" "}
            <Link to={signupPath("/crear-cuenta/acceso", { tipo: type })}>Cambiar email</Link>
          </>
        }
        footer={
          <>
            ¿No te llegó?{" "}
            {wait > 0 ? (
              <span className="muted">Podés pedir otro en {wait} s.</span>
            ) : (
              <Button type="button" variant="text" onClick={() => void resend()} disabled={resending}>
                Reenviar código
              </Button>
            )}
          </>
        }
      >
        <TypeChip type={type} />
        {notice && <Alert variant="info">{notice}</Alert>}
        <form className="form" onSubmit={verify} noValidate>
          <Field
            label="Código"
            name="code"
            className="field--code"
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={6}
            autoFocus
            value={code}
            onChange={(event) => setCode(event.target.value.replace(/\D/g, "").slice(0, 6))}
            error={codeError}
            disabled={verifying || locked}
          />
          <Button type="submit" loading={verifying} disabled={locked}>
            Verificar
          </Button>
        </form>
        <p className="sim-note">En la maqueta el correo llega a DEMO › Correos.</p>
      </Card>
    </AuthLayout>
  );
}
