import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { Alert, AuthLayout, Button, Card, Field, Spinner } from "../components";
import { GoogleMark } from "../components/GoogleButton";
import { api, ApiError, type GoogleAccount } from "../lib/api";
import { initials } from "../lib/format";
import { errorMessage } from "../lib/session";
import { EMAIL_PATTERN, readType, signupPath } from "../lib/signup";

/** Rótulos de la simulación: dicen qué va a pasar para que el escenario se entienda. */
const STATUS_COPY: Record<GoogleAccount["status"], string> = {
  linked: "Vinculada: entra",
  conflict: "Tiene cuenta con contraseña",
  new: "Nueva: se crea la cuenta",
};

/**
 * La pantalla de Google, simulada y rotulada como tal. Vuelve a entrar o a crear
 * cuenta según de dónde vino; lo que decide qué cuenta abre es el servidor.
 */
export function GooglePage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const creating = params.get("intent") === "crear";
  const type = readType(params);
  const next = params.get("next");
  const suggested = params.get("sugerida");

  const [accounts, setAccounts] = useState<GoogleAccount[] | null>(null);
  const [other, setOther] = useState(false);
  const [email, setEmail] = useState("");
  const [name, setName] = useState("");
  const [emailError, setEmailError] = useState("");
  const [formError, setFormError] = useState("");
  const [busy, setBusy] = useState(false);
  const [conflict, setConflict] = useState<string | null>(null);

  useEffect(() => {
    api
      .googleAccounts()
      .then((data) => setAccounts(data.accounts))
      .catch(() => setAccounts([]));
  }, []);

  const back = creating && type ? signupPath("/crear-cuenta/acceso", { tipo: type }) : `/entrar${next ? `?next=${encodeURIComponent(next)}` : ""}`;

  async function choose(account: { email: string; name: string }) {
    if (busy) return;
    setBusy(true);
    setFormError("");
    try {
      await api.googleSignIn(account);
      if (creating && type) navigate(`/crear-cuenta/datos?tipo=${type}`, { replace: true });
      else navigate(next ?? "/app", { replace: true });
    } catch (error) {
      setBusy(false);
      if (error instanceof ApiError && error.code === "external_login_conflict") setConflict(account.email);
      else setFormError(errorMessage(error));
    }
  }

  function submitOther(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const value = email.trim();
    if (!EMAIL_PATTERN.test(value)) {
      setEmailError(value ? "Ingresá un correo válido." : "Ingresá el correo de la cuenta de Google.");
      return;
    }
    setEmailError("");
    void choose({ email: value, name: name.trim() });
  }

  if (conflict) {
    return (
      <AuthLayout>
        <Card
          title="Esa cuenta de Google no se puede usar todavía"
          subtitle={
            <>
              <strong>{conflict}</strong> ya tiene una cuenta con contraseña. Entrá con tu contraseña: Google no se vincula
              solo por tener el mismo email.
            </>
          }
        >
          <Button onClick={() => navigate("/entrar")}>Entrar con contraseña</Button>
          <Button variant="ghost" onClick={() => navigate(back)}>
            Volver
          </Button>
          <p className="sim-note">Vincular Google desde adentro de la cuenta no está en la maqueta.</p>
        </Card>
      </AuthLayout>
    );
  }

  const sorted = accounts
    ? [...accounts].sort((left, right) => Number(right.email === suggested) - Number(left.email === suggested))
    : null;

  return (
    <div className="auth">
      <div className="auth__stack gsim">
        <p className="sim-banner">Pantalla de Google simulada. En el producto es la de Google.</p>
        <section className="gsim__card" aria-labelledby="gsim-title">
          <div className="gsim__logo">
            <GoogleMark size={28} />
          </div>
          <h1 className="gsim__title" id="gsim-title">
            Elegí una cuenta
          </h1>
          <p className="gsim__sub">para continuar a Plataforma</p>
          {formError && <Alert variant="error">{formError}</Alert>}

          {sorted === null ? (
            <div className="card__spinner">
              <Spinner />
            </div>
          ) : (
            <ul className="gsim__list">
              {sorted.map((account) => (
                <li key={account.email}>
                  <button type="button" className="gsim__account" onClick={() => void choose(account)} disabled={busy}>
                    <span className="gsim__avatar" aria-hidden="true">
                      {initials(account.name)}
                    </span>
                    <span className="gsim__lines">
                      <span className="gsim__name">{account.name}</span>
                      <span className="gsim__email">{account.email}</span>
                    </span>
                    <span className="gsim__status">{STATUS_COPY[account.status]}</span>
                  </button>
                </li>
              ))}
              <li>
                <button type="button" className="gsim__account" onClick={() => setOther(true)} disabled={busy}>
                  <span className="gsim__avatar" aria-hidden="true">
                    +
                  </span>
                  <span className="gsim__lines">
                    <span className="gsim__name">Usar otra cuenta</span>
                  </span>
                </button>
              </li>
            </ul>
          )}

          {other && (
            <form className="form gsim__other" onSubmit={submitOther} noValidate>
              <Field
                label="Correo de Google"
                type="email"
                name="email"
                autoFocus
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                error={emailError}
                disabled={busy}
              />
              <Field label="Nombre" name="name" value={name} onChange={(event) => setName(event.target.value)} disabled={busy} />
              <Button type="submit" loading={busy}>
                Continuar
              </Button>
            </form>
          )}

          <p className="gsim__foot">
            <Link to={back}>Cancelar</Link>
          </p>
        </section>
      </div>
    </div>
  );
}
