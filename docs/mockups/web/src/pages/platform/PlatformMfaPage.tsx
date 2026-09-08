import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, Field, StateBlock } from "../../components";
import { api, ApiError } from "../../lib/api";
import { errorMessage, useSession } from "../../lib/session";

type Step = "checking" | "intro" | "verify" | "codes" | "done" | "denied";

/**
 * Enrolamiento MFA simulado. No hay TOTP real ni secretos: la secuencia existe
 * para mostrar el orden (enrolar → verificar → guardar códigos → activar).
 */
export function PlatformMfaPage() {
  const navigate = useNavigate();
  const { me, loading, reload } = useSession();
  const [step, setStep] = useState<Step>("checking");
  const [secret, setSecret] = useState("");
  const [simulatedCode, setSimulatedCode] = useState("");
  const [code, setCode] = useState("");
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [saved, setSaved] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (loading || !me) return;
    if (!me.platform) {
      setStep("denied");
      return;
    }
    if (me.platform.mfaEnrolled && me.platform.recoveryCodesSaved && me.platform.activated) {
      setStep("done");
      return;
    }
    setStep("intro");
  }, [loading, me]);

  async function enroll() {
    setWorking(true);
    setError("");
    try {
      const data = await api.mfaEnroll();
      setSecret(data.secret);
      setSimulatedCode(data.simulatedCode);
      setStep("verify");
    } catch (caught) {
      setError(errorMessage(caught));
    } finally {
      setWorking(false);
    }
  }

  async function verify() {
    setWorking(true);
    setError("");
    try {
      const data = await api.mfaVerify(code.trim());
      setRecoveryCodes(data.recoveryCodes);
      setStep("codes");
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : errorMessage(caught));
    } finally {
      setWorking(false);
    }
  }

  async function finish() {
    setWorking(true);
    setError("");
    try {
      await api.mfaAck();
      await reload();
      setStep("done");
    } catch (caught) {
      setError(errorMessage(caught));
    } finally {
      setWorking(false);
    }
  }

  if (step === "checking" || loading) {
    return (
      <div className="plat plat--narrow">
        <main className="plat__main">
          <StateBlock kind="loading" title="Verificando tu acceso…" />
        </main>
      </div>
    );
  }

  if (step === "denied") {
    return (
      <div className="plat plat--narrow">
        <main className="plat__main">
          <StateBlock
            kind="denied"
            title="Sin acceso a Platform"
            description="Tu identidad no tiene una membresía de Platform. Si te invitaron, abrí el enlace de la invitación."
            action={
              <Button variant="ghost" onClick={() => navigate("/app")}>
                Volver a la aplicación
              </Button>
            }
          />
        </main>
      </div>
    );
  }

  return (
    <div className="plat plat--narrow">
      <main className="plat__main">
        <p className="sim-banner">
          Secuencia de <strong>MFA simulada</strong>. No hay TOTP, secretos ni criptografía: sirve para evaluar el orden y
          los textos de la experiencia.
        </p>

        {step === "intro" && (
          <Card
            title="Activá tu acceso a Platform"
            subtitle="La membresía se activa recién cuando completes los tres pasos: enrolar, verificar y guardar los códigos de recuperación."
          >
            {error && <Alert variant="error">{error}</Alert>}
            <ol className="steps">
              <li>Enrolar la aplicación de segundo factor (simulado).</li>
              <li>Verificar un código de 6 dígitos (simulado).</li>
              <li>Guardar los códigos de recuperación y confirmarlo.</li>
            </ol>
            <Button onClick={() => void enroll()} loading={working}>
              Empezar el enrolamiento
            </Button>
          </Card>
        )}

        {step === "verify" && (
          <Card title="Verificá el código" subtitle="Paso 2 de 3">
            <p className="sim-note">
              Secreto simulado: <code>{secret}</code>. Código válido de la simulación: <code>{simulatedCode}</code>.
            </p>
            <Field
              label="Código de 6 dígitos"
              value={code}
              onChange={(event) => setCode(event.target.value)}
              inputMode="numeric"
              placeholder="123456"
              autoFocus
            />
            {error && <Alert variant="error">{error}</Alert>}
            <Button onClick={() => void verify()} loading={working}>
              Verificar
            </Button>
          </Card>
        )}

        {step === "codes" && (
          <Card
            title="Guardá los códigos de recuperación"
            subtitle="Paso 3 de 3. Son ficticios y sirven una sola vez en la simulación."
          >
            <ul className="codes">
              {recoveryCodes.map((recovery) => (
                <li key={recovery}>
                  <code>{recovery}</code>
                </li>
              ))}
            </ul>
            <label className="check">
              <input type="checkbox" checked={saved} onChange={(event) => setSaved(event.target.checked)} />
              <span>Confirmo que los guardé en un lugar seguro.</span>
            </label>
            {error && <Alert variant="error">{error}</Alert>}
            <Button onClick={() => void finish()} loading={working} disabled={!saved}>
              Activar mi acceso a Platform
            </Button>
          </Card>
        )}

        {step === "done" && (
          <Card title="Acceso activo" subtitle="Completaste la secuencia simulada y tu membresía de Platform quedó activa.">
            <Button onClick={() => navigate("/platform")}>Entrar a Platform</Button>
          </Card>
        )}
      </main>
    </div>
  );
}
