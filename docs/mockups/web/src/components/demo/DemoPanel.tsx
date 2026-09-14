import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api, type Mail, type Scenario } from "../../lib/api";
import { shortDateTime } from "../../lib/format";

type Tab = "escenarios" | "correos" | "estados";

const HINT_KEY = "mockup-demo-hint";

const JOURNEY_LABEL: Record<Scenario["journey"], string> = {
  A: "A · Entrar, crear cuenta y sesión",
  B: "B · Contexto, permisos y organizaciones",
  C: "C · Invitación y aceptación",
  D: "D · Platform",
};

const MAIL_LABEL: Record<Mail["kind"], string> = {
  registro: "Registro",
  confirmacion: "Confirmación",
  codigo: "Código",
  invitacion: "Invitación",
  "invitacion-platform": "Invitación Platform",
  aviso: "Aviso",
};

/**
 * Controles de demostración. Está deliberadamente fuera de la estética del
 * producto: no es una función para personas usuarias reales.
 */
export function DemoPanel() {
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [tab, setTab] = useState<Tab>("escenarios");
  const [scenarios, setScenarios] = useState<Scenario[]>([]);
  const [mails, setMails] = useState<Mail[]>([]);
  const [hint, setHint] = useState("");
  const [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    try {
      const [scenarioData, mailData] = await Promise.all([api.scenarios(), api.mails()]);
      setScenarios(scenarioData.scenarios);
      setMails(mailData.mails);
    } catch {
      // El panel es auxiliar: si la API no responde se muestra vacío.
    }
  }, []);

  useEffect(() => {
    if (open) void refresh();
  }, [open, refresh]);

  // Pista del último escenario cargado, guardada antes de la recarga.
  useEffect(() => {
    try {
      const stored = sessionStorage.getItem(HINT_KEY);
      if (stored) {
        setHint(stored);
        sessionStorage.removeItem(HINT_KEY);
        setOpen(true);
      }
    } catch {
      // Sin sessionStorage el panel arranca cerrado y sin pista.
    }
  }, []);

  useEffect(() => {
    function onKey(event: globalThis.KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }
    if (!open) return;
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open]);

  async function runScenario(scenario: Scenario) {
    setBusy(true);
    try {
      const data = await api.applyScenario(scenario.id);
      // Recarga dura: el contexto de sesión se rehace con los datos nuevos.
      // La pista sobrevive a la recarga en sessionStorage.
      try {
        sessionStorage.setItem(HINT_KEY, `${data.name}: ${data.hint}`);
      } catch {
        // Sin sessionStorage se pierde la pista, nada más.
      }
      window.location.assign(data.start);
    } finally {
      setBusy(false);
    }
  }

  async function reset() {
    setBusy(true);
    try {
      await api.resetDemo();
      setHint("Datos del mockup reiniciados. La sesión quedó cerrada.");
      window.location.assign("/entrar");
    } finally {
      setBusy(false);
    }
  }

  function openLink(link: string) {
    try {
      const url = new URL(link);
      setOpen(false);
      navigate(`${url.pathname}${url.search}`);
    } catch {
      setOpen(false);
    }
  }

  async function arm(mode: "network" | "conflict" | "notfound") {
    await api.failNext(mode);
    setHint(
      mode === "network"
        ? "El próximo pedido al servidor va a fallar (500 → error de red)."
        : mode === "conflict"
          ? "La próxima suspensión o reactivación va a devolver 409."
          : "El próximo pedido va a devolver 404.",
    );
  }

  return (
    <>
      <button type="button" className="demo-fab" onClick={() => setOpen(true)} aria-expanded={open} aria-controls="demo-panel">
        Demo
      </button>

      {open && (
        <div className="demo" id="demo-panel" role="dialog" aria-modal="false" aria-label="Panel de demostración">
          <header className="demo__head">
            <div>
              <p className="demo__title">Panel de demostración</p>
              <p className="demo__sub">No forma parte del producto. Sólo afecta los datos en memoria del mockup.</p>
            </div>
            <button type="button" className="demo__close" onClick={() => setOpen(false)} aria-label="Cerrar el panel">
              ✕
            </button>
          </header>

          <div className="demo__tabs" role="tablist" aria-label="Secciones del panel">
            {(["escenarios", "correos", "estados"] as Tab[]).map((value) => (
              <button
                key={value}
                type="button"
                role="tab"
                aria-selected={tab === value}
                className={`demo__tab${tab === value ? " is-active" : ""}`}
                onClick={() => setTab(value)}
              >
                {value === "escenarios" ? "Escenarios" : value === "correos" ? `Correos (${mails.length})` : "Estados"}
              </button>
            ))}
          </div>

          {hint && <p className="demo__hint">{hint}</p>}

          <div className="demo__body">
            {tab === "escenarios" &&
              (["A", "B", "C", "D"] as Scenario["journey"][]).map((journey) => (
                <section key={journey} className="demo__group">
                  <h3 className="demo__grouptitle">{JOURNEY_LABEL[journey]}</h3>
                  {scenarios
                    .filter((scenario) => scenario.journey === journey)
                    .map((scenario) => (
                      <article key={scenario.id} className="demo__card">
                        <p className="demo__cardtitle">{scenario.name}</p>
                        <p className="demo__cardtext">{scenario.description}</p>
                        <button
                          type="button"
                          className="demo__action"
                          onClick={() => void runScenario(scenario)}
                          disabled={busy}
                        >
                          Cargar escenario
                        </button>
                      </article>
                    ))}
                </section>
              ))}

            {tab === "correos" && (
              <section className="demo__group">
                <h3 className="demo__grouptitle">Bandeja simulada</h3>
                {mails.length === 0 && <p className="demo__cardtext">Todavía no se envió ningún correo.</p>}
                {mails.map((mail) => (
                  <article key={mail.id} className="demo__card">
                    <p className="demo__cardtitle">{mail.subject}</p>
                    <p className="demo__meta">
                      Para {mail.to} · {MAIL_LABEL[mail.kind]} · {shortDateTime(mail.sentAt)}
                    </p>
                    <p className="demo__cardtext">{mail.body}</p>
                    {mail.link && (
                      <button type="button" className="demo__action" onClick={() => openLink(mail.link as string)}>
                        Abrir el enlace
                      </button>
                    )}
                  </article>
                ))}
              </section>
            )}

            {tab === "estados" && (
              <section className="demo__group">
                <h3 className="demo__grouptitle">Estados y fallas</h3>
                <article className="demo__card">
                  <p className="demo__cardtitle">Armar una falla para el próximo pedido</p>
                  <div className="demo__row">
                    <button type="button" className="demo__action" onClick={() => void arm("network")}>
                      Error de red
                    </button>
                    <button type="button" className="demo__action" onClick={() => void arm("conflict")}>
                      Conflicto 409
                    </button>
                    <button type="button" className="demo__action" onClick={() => void arm("notfound")}>
                      No encontrado 404
                    </button>
                  </div>
                </article>
                <article className="demo__card">
                  <p className="demo__cardtitle">Sesión y MFA</p>
                  <div className="demo__row">
                    <button
                      type="button"
                      className="demo__action"
                      onClick={async () => {
                        await api.revokeSession();
                        setHint("Sesión revocada: el próximo pedido devuelve 401 y te lleva al ingreso.");
                      }}
                    >
                      Revocar la sesión
                    </button>
                    <button
                      type="button"
                      className="demo__action"
                      onClick={async () => {
                        await api.expireStepUp();
                        setHint("Step-up vencido: la próxima mutación de Platform va a pedir revalidar MFA.");
                      }}
                    >
                      Vencer el step-up
                    </button>
                  </div>
                </article>
                <article className="demo__card">
                  <p className="demo__cardtitle">Ir a Platform</p>
                  <p className="demo__cardtext">
                    Platform no se enlaza desde el menú del cliente a propósito. Desde acá, que no es producto, sí.
                  </p>
                  <button
                    type="button"
                    className="demo__action"
                    onClick={() => {
                      setOpen(false);
                      navigate("/platform");
                    }}
                  >
                    Abrir Platform
                  </button>
                </article>
                <article className="demo__card">
                  <p className="demo__cardtitle">Reiniciar</p>
                  <p className="demo__cardtext">
                    Vuelve a los datos iniciales del mockup y cierra la sesión. No toca nada fuera de la memoria del
                    proceso.
                  </p>
                  <button type="button" className="demo__action demo__action--danger" onClick={() => void reset()} disabled={busy}>
                    Reiniciar los datos del mockup
                  </button>
                </article>
              </section>
            )}
          </div>
        </div>
      )}
    </>
  );
}
