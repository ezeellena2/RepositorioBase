import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Alert, Badge, Button, DataTable, Field, Modal, PageHeader, StateBlock, type Column } from "../../components";
import { api, ApiError, NetworkError, type RetentionCategory, type RetentionHold, type RetentionPolicy } from "../../lib/api";
import { shortDateTime } from "../../lib/format";
import { errorMessage } from "../../lib/session";
import { StepUpModal } from "./StepUpModal";
import { usePlatform } from "./PlatformShell";

/**
 * La forma que acepta el servidor para una razón y una referencia, espejada acá
 * para que un valor que ya sabemos que rechazaría no llegue a ser un pedido.
 * Espeja y no reemplaza: la autoridad sigue siendo el servidor, y un valor que
 * esto deja pasar todavía puede ser rechazado allá.
 */
const REFERENCE_FORMAT = /^[A-Za-z0-9._:-]{1,64}$/;
const REFERENCE_SHAPE = "Letras, dígitos, punto, guion bajo, dos puntos o guion — de 1 a 64 caracteres.";

type PolicyStatus = "loading" | "ready" | "denied" | "error";

/**
 * Retención: qué dice la política de este despliegue y qué retenciones legales
 * detienen un borrado.
 *
 * Acá no hay control de borrado y no lo va a haber. El borrado lo ejecuta el
 * proceso de mantenimiento según la política, sin endpoint y sin permiso detrás:
 * lo que puede hacer quien opera es leer las reglas y detener un borrado, nunca
 * pedirlo.
 */
export function PlatformRetentionPage() {
  const { reloadPlatform, can } = usePlatform();
  const mayRead = can("platform.retention.read");
  const mayManage = can("platform.retention.manage");

  const [policy, setPolicy] = useState<RetentionPolicy | null>(null);
  const [status, setStatus] = useState<PolicyStatus>("loading");
  const [message, setMessage] = useState("");

  const [subjectIdentityId, setSubjectIdentityId] = useState("");
  const [reasonCode, setReasonCode] = useState("");
  const [reference, setReference] = useState("");
  const [holdId, setHoldId] = useState("");
  const [pendingRelease, setPendingRelease] = useState<string | null>(null);
  const [receipt, setReceipt] = useState<RetentionHold | null>(null);
  const [releaseNotice, setReleaseNotice] = useState(false);
  const [shapeError, setShapeError] = useState("");
  const [error, setError] = useState("");
  const [working, setWorking] = useState(false);
  const [stepUp, setStepUp] = useState(false);

  // No se pide nada mientras falte el permiso: sólo produciría rechazos que quien
  // mira no puede resolver desde acá.
  const load = useCallback(async () => {
    if (!mayRead) return;
    setStatus("loading");
    try {
      setPolicy(await api.retentionPolicy());
      setStatus("ready");
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 403) {
        setMessage(caught.message);
        setStatus("denied");
        return;
      }
      setMessage(caught instanceof NetworkError ? caught.message : "No pudimos leer la política de retención.");
      setStatus("error");
    }
  }, [mayRead]);

  useEffect(() => {
    void load();
  }, [load]);

  function handleFailure(caught: unknown): boolean {
    if (caught instanceof ApiError && caught.code === "step_up_required") {
      setPendingRelease(null);
      setStepUp(true);
      return true;
    }
    setError(errorMessage(caught));
    return false;
  }

  async function placeHold(event: FormEvent) {
    event.preventDefault();
    setError("");
    setReleaseNotice(false);
    setReceipt(null);
    if (!REFERENCE_FORMAT.test(reasonCode.trim()) || !REFERENCE_FORMAT.test(reference.trim())) {
      setShapeError(`La razón y la referencia tienen la misma forma: ${REFERENCE_SHAPE} No se mandó nada.`);
      return;
    }
    setShapeError("");
    setWorking(true);
    try {
      const placed = await api.placeRetentionHold({
        subjectIdentityId: subjectIdentityId.trim(),
        reasonCode: reasonCode.trim(),
        reference: reference.trim(),
      });
      setReceipt(placed);
      setSubjectIdentityId("");
      setReasonCode("");
      setReference("");
      // El contador de retenciones en pie es parte de lo que la pantalla afirma:
      // un cambio que lo movió se vuelve a leer.
      await load();
    } catch (caught) {
      handleFailure(caught);
    } finally {
      setWorking(false);
    }
  }

  async function releaseHold() {
    if (!pendingRelease) return;
    setError("");
    setReceipt(null);
    setWorking(true);
    try {
      await api.releaseRetentionHold(pendingRelease);
      setPendingRelease(null);
      setHoldId("");
      setReleaseNotice(true);
      await load();
    } catch (caught) {
      handleFailure(caught);
    } finally {
      setWorking(false);
    }
  }

  if (!mayRead) {
    return (
      <>
        <PageHeader title="Retención" description="Qué guarda este despliegue, por cuánto tiempo y qué lo detiene." />
        <StateBlock
          kind="denied"
          title="Sin permiso"
          description="Esta pantalla necesita el permiso de lectura de retención. Pedíselo a una titular de Platform."
        />
      </>
    );
  }

  const columns: Column<RetentionCategory>[] = [
    { key: "category", header: "Categoría", render: (rule) => rule.category },
    { key: "period", header: "Plazo", render: (rule) => rule.retentionPeriod },
    { key: "trigger", header: "Desde", render: (rule) => rule.trigger },
    { key: "action", header: "Qué se hace", render: (rule) => rule.action },
    {
      key: "evidence",
      header: "Evidencia",
      end: true,
      render: (rule) => <Badge tone={rule.evidenceRequired ? "info" : "neutral"}>{rule.evidenceRequired ? "Requerida" : "No"}</Badge>,
    },
  ];

  // Todos los campos en null y ninguna categoría es un despliegue sin política, y
  // eso es una respuesta completa, no una vacía: una tabla vacía se leería como
  // "no hay categorías", y lo cierto es "acá no se va a borrar nada".
  const noPolicy = policy !== null && policy.policyId === null && policy.categories.length === 0;

  return (
    <>
      <PageHeader
        title="Retención"
        description="Qué guarda este despliegue, por cuánto tiempo y qué lo detiene. No hay control de borrado: eso lo ejecuta el mantenimiento según la política."
      />

      {shapeError && <Alert variant="error">{shapeError}</Alert>}
      {!shapeError && error && <Alert variant="error">{error}</Alert>}
      {!mayManage && (
        <Alert variant="info">
          Tu rol lee la política y no toca ninguna retención. Poner y levantar retenciones es un permiso aparte.
        </Alert>
      )}

      {status === "loading" && <StateBlock kind="loading" title="Leyendo la política de retención…" />}
      {status === "denied" && <StateBlock kind="denied" title="Sin permiso" description={message} />}
      {status === "error" && (
        <StateBlock
          kind="error"
          title="No pudimos leer la política"
          description={message}
          action={<Button onClick={() => void load()}>Reintentar</Button>}
        />
      )}

      {status === "ready" && policy && (
        <>
          <section className="panel">
            <h2 className="panel__title">Este despliegue</h2>
            <ul className="minilist">
              <li className="minilist__item">
                <span>Datos personales</span>
                <Badge tone="info">{policy.personalDataMode}</Badge>
              </li>
              <li className="minilist__item">
                <span>Retenciones en pie</span>
                <Badge tone={policy.activeHoldCount > 0 ? "warn" : "neutral"}>{policy.activeHoldCount}</Badge>
              </li>
              <li className="minilist__item">
                <span>Política</span>
                <span>{policy.policyId ?? "—"}</span>
              </li>
              <li className="minilist__item">
                <span>Versión</span>
                <span>{policy.version ?? "—"}</span>
              </li>
              <li className="minilist__item">
                <span>Aprobada por</span>
                <span>{policy.owner ?? "—"}</span>
              </li>
              <li className="minilist__item">
                <span>Fecha de aprobación</span>
                <span>{policy.approvedOn ?? "—"}</span>
              </li>
              <li className="minilist__item">
                <span>Origen</span>
                <span>{policy.source ?? "—"}</span>
              </li>
            </ul>
          </section>

          {noPolicy ? (
            <StateBlock
              kind="empty"
              title="No hay política de retención configurada"
              description="Este despliegue no va a borrar nada. Las retenciones legales se pueden poner igual."
            />
          ) : (
            <DataTable
              caption="Categorías de la política de retención"
              columns={columns}
              rows={policy.categories}
              getKey={(rule) => rule.category}
            />
          )}
        </>
      )}

      {mayManage && (
        <>
          <section className="panel">
            <h2 className="panel__title">Poner una retención</h2>
            <p className="panel__text">
              Detiene el borrado de esa identidad y no hace nada más: no cambia el estado de la cuenta, ni sus
              sesiones, ni sus permisos.
            </p>
            <form className="form" onSubmit={placeHold}>
              <Field
                label="Identidad alcanzada"
                value={subjectIdentityId}
                onChange={(event) => setSubjectIdentityId(event.target.value)}
                placeholder="u-hugo"
                hint="El identificador que muestra el directorio de identidades."
                required
              />
              <Field
                label="Razón"
                value={reasonCode}
                onChange={(event) => setReasonCode(event.target.value)}
                placeholder="LITIGATION"
                hint={REFERENCE_SHAPE}
                required
              />
              <Field
                label="Referencia"
                value={reference}
                onChange={(event) => setReference(event.target.value)}
                placeholder="CASO-2026-021"
                hint="Nombra un expediente que vive fuera de este sistema."
                required
              />
              <Button type="submit" loading={working}>
                Poner la retención
              </Button>
            </form>

            {/* Única vez que se ve el identificador de una retención: no hay ruta
                que las liste, así que quien no lo guarde no puede volver a nombrarla. */}
            {receipt && (
              <Alert variant="success">
                Retención <code>{receipt.holdId}</code> puesta sobre {receipt.subjectIdentityId} por{" "}
                {receipt.reasonCode}, referencia {receipt.reference}, el {shortDateTime(receipt.placedAt)}. Guardá ese
                identificador: nada lista las retenciones, así que esta es la única vez que se muestra.
              </Alert>
            )}
          </section>

          <section className="panel">
            <h2 className="panel__title">Levantar una retención</h2>
            <form
              className="form"
              onSubmit={(event) => {
                event.preventDefault();
                setPendingRelease(holdId.trim());
              }}
            >
              <Field
                label="Identificador de la retención"
                value={holdId}
                onChange={(event) => setHoldId(event.target.value)}
                placeholder="hold-litigio"
                required
              />
              <Button type="submit" variant="ghost">
                Levantar
              </Button>
            </form>

            {releaseNotice && (
              <Alert variant="info">
                Esa retención está levantada. Una que ya estaba levantada y una que nunca existió responden igual, así
                que esto dice lo que es cierto ahora, no que este pedido haya cambiado algo.
              </Alert>
            )}
          </section>
        </>
      )}

      {/* Se confirma en vez de hacerse de un clic: levantar una retención es lo
          que deja al mantenimiento borrar lo que estaba protegiendo, y volver a
          hacer clic no lo devuelve. */}
      {pendingRelease && (
        <Modal
          title="Levantar la retención"
          description="Nada se lee antes: la ruta responde igual si la retención está en pie, si ya se levantó o si nunca existió."
          onClose={() => setPendingRelease(null)}
          footer={
            <>
              <Button variant="ghost" className="btn--inline" onClick={() => setPendingRelease(null)} disabled={working}>
                Cancelar
              </Button>
              <Button className="btn--inline" onClick={() => void releaseHold()} loading={working}>
                Levantar la retención
              </Button>
            </>
          }
        >
          <div className="form">
            <p className="sim-note">
              Después de esto, el proceso de mantenimiento puede borrar los datos que la retención estaba deteniendo.
            </p>
            {error && <Alert variant="error">{error}</Alert>}
          </div>
        </Modal>
      )}

      {stepUp && (
        <StepUpModal
          onClose={() => setStepUp(false)}
          onDone={() => {
            setStepUp(false);
            reloadPlatform();
            void load();
            setError("");
            // Lo que vale una revalidación acá es una lectura fresca y nada más:
            // el cambio rechazado lo vuelve a pedir quien opera.
            setReleaseNotice(false);
          }}
        />
      )}
    </>
  );
}
