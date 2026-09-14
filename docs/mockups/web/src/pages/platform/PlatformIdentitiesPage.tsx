import { useCallback, useState } from "react";
import {
  Alert,
  Badge,
  Button,
  DataTable,
  LoadMore,
  Modal,
  PageHeader,
  StateBlock,
  type BadgeTone,
  type Column,
} from "../../components";
import {
  api,
  ApiError,
  SUSPENSION_REASONS,
  type AccountStatus,
  type PlatformIdentityRow,
  type SuspensionReason,
} from "../../lib/api";
import { ACCOUNT_STATUS_HELP, ACCOUNT_STATUS_LABEL, shortDate, SUSPENSION_REASON_LABEL } from "../../lib/format";
import { errorMessage } from "../../lib/session";
import { usePaged } from "../../lib/usePaged";
import { StepUpModal } from "./StepUpModal";
import { usePlatform } from "./PlatformShell";

const TONE: Record<AccountStatus, BadgeTone> = {
  pending_confirmation: "warn",
  active: "ok",
  self_deactivated: "neutral",
  administratively_suspended: "danger",
  closed: "neutral",
};

/**
 * Qué transición aceptaría el servidor desde un estado dado, y por lo tanto la
 * única que vale la pena ofrecer. Reactivar se acepta sobre una suspensión
 * operativa y sobre ninguna otra cosa; suspender, desde todo menos eso y una
 * cuenta cerrada, que es terminal. Mostrar el resto sería mostrar un botón cuya
 * única respuesta posible es un rechazo.
 */
function transitionFor(status: AccountStatus): "suspend" | "reactivate" | null {
  if (status === "administratively_suspended") return "reactivate";
  if (status === "closed") return null;
  return "suspend";
}

interface Pending {
  kind: "suspend" | "reactivate";
  row: PlatformIdentityRow;
  /** El estado que se leyó en el directorio, no el que haya ahora. */
  expectedStatus: AccountStatus;
}

/**
 * Directorio operativo de cuentas y los dos cambios de ciclo de vida que ofrece.
 *
 * La precondición se lee una sola vez, cuando se arma la confirmación: volver a
 * derivarla al confirmar la haría coincidir con lo que haya llegado en el medio,
 * que es exactamente el desacuerdo que existe para detectar.
 */
export function PlatformIdentitiesPage() {
  const { reloadPlatform, can } = usePlatform();
  const load = useCallback((cursor: string | null) => api.platformIdentities(cursor), []);
  const list = usePaged<PlatformIdentityRow>(load);

  const [pending, setPending] = useState<Pending | null>(null);
  const [reason, setReason] = useState<SuspensionReason>(SUSPENSION_REASONS[0]);
  const [acknowledged, setAcknowledged] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [stepUp, setStepUp] = useState(false);

  const mayManage = can("platform.identities.manage");

  function arm(kind: "suspend" | "reactivate", row: PlatformIdentityRow) {
    setReason(SUSPENSION_REASONS[0]);
    setAcknowledged(false);
    setError("");
    setNotice("");
    setPending({ kind, row, expectedStatus: row.accountStatus });
  }

  async function run() {
    if (!pending) return;
    setWorking(true);
    setError("");
    try {
      if (pending.kind === "suspend") {
        await api.suspendIdentity(pending.row.id, { reason, expectedStatus: pending.expectedStatus });
        setNotice(`${pending.row.email} quedó detenida. Sus sesiones abiertas se terminaron.`);
      } else {
        await api.reactivateIdentity(pending.row.id, {
          expectedStatus: pending.expectedStatus,
          acknowledgeSelfDeactivation: acknowledged,
        });
        setNotice(`Se levantó la suspensión de ${pending.row.email}.`);
      }
      setPending(null);
      list.reload();
    } catch (caught) {
      if (caught instanceof ApiError && caught.code === "step_up_required") {
        // La confirmación baja antes de que suba la revalidación: lo que sostenía
        // se compuso contra una precondición que el servidor ya no atendió, y
        // dejarla armada es como una revalidación termina en un cambio que nadie
        // volvió a pedir.
        setPending(null);
        setStepUp(true);
        return;
      }
      if (caught instanceof ApiError && caught.code === "identity_concurrency_conflict") {
        setPending(null);
        setError(caught.message);
        list.reload();
        return;
      }
      // Todo otro rechazo deja la confirmación en pie: sobre uno de ellos, tildar
      // el reconocimiento y confirmar de nuevo es la respuesta entera.
      setError(errorMessage(caught));
    } finally {
      setWorking(false);
    }
  }

  const columns: Column<PlatformIdentityRow>[] = [
    { key: "name", header: "Nombre", render: (row) => row.name },
    { key: "email", header: "Correo", render: (row) => row.email },
    {
      key: "status",
      header: "Estado de la cuenta",
      render: (row) => (
        <Badge tone={TONE[row.accountStatus]}>{ACCOUNT_STATUS_LABEL[row.accountStatus]}</Badge>
      ),
    },
    { key: "orgs", header: "Organizaciones", render: (row) => row.orgsCount, end: true },
    { key: "seen", header: "Último ingreso", render: (row) => (row.lastSeenAt ? shortDate(row.lastSeenAt) : "—") },
    {
      key: "actions",
      header: "Acciones",
      end: true,
      render: (row) => {
        const transition = mayManage ? transitionFor(row.accountStatus) : null;
        if (transition === "suspend") {
          return (
            <button type="button" className="btn btn--text" onClick={() => arm("suspend", row)}>
              Detener
            </button>
          );
        }
        if (transition === "reactivate") {
          return (
            <button type="button" className="btn btn--text" onClick={() => arm("reactivate", row)}>
              Levantar suspensión
            </button>
          );
        }
        return <span className="muted">—</span>;
      },
    },
  ];

  const overSelfDeactivation = pending?.row.accountStatus === "administratively_suspended" && !acknowledged;

  return (
    <>
      <PageHeader
        title="Identidades"
        description="Directorio operativo. No hay suplantación ni acceso a datos privados de las organizaciones: sólo el estado de la cuenta y las dos transiciones que Platform puede hacer."
      />

      {notice && <Alert variant="success">{notice}</Alert>}
      {error && !pending && <Alert variant="error">{error}</Alert>}
      {!mayManage && (
        <Alert variant="info">
          Tu rol lee el directorio y no lo cambia. Detener y reactivar cuentas es un permiso aparte.
        </Alert>
      )}

      {list.status === "loading" && <StateBlock kind="loading" title="Cargando identidades…" />}
      {list.status === "denied" && <StateBlock kind="denied" title="Sin permiso" description={list.message} />}
      {list.status === "error" && (
        <StateBlock
          kind="error"
          title="No pudimos traer el directorio"
          description={list.message}
          action={<Button onClick={() => list.reload()}>Reintentar</Button>}
        />
      )}
      {list.status === "ready" && list.rows.length === 0 && (
        <StateBlock kind="empty" title="No hay identidades" description="Todavía no se registró nadie." />
      )}
      {list.status === "ready" && list.rows.length > 0 && (
        <>
          <DataTable caption="Directorio de identidades" columns={columns} rows={list.rows} getKey={(row) => row.id} />
          <LoadMore
            shown={list.rows.length}
            total={list.total}
            hasMore={list.hasMore}
            loading={list.loadingMore}
            onMore={list.more}
          />
        </>
      )}

      {pending?.kind === "suspend" && (
        <Modal
          title={`Detener ${pending.row.email}`}
          description="Termina todas sus sesiones y la deja fuera hasta que alguien levante la suspensión. No borra nada."
          onClose={() => {
            setPending(null);
            setError("");
          }}
          footer={
            <>
              <Button variant="ghost" className="btn--inline" onClick={() => setPending(null)} disabled={working}>
                Cancelar
              </Button>
              <Button className="btn--inline" onClick={() => void run()} loading={working}>
                Detener la cuenta
              </Button>
            </>
          }
        >
          <div className="form">
            <label className="field__label" htmlFor="suspension-reason">
              Razón
            </label>
            <select
              id="suspension-reason"
              className="select"
              value={reason}
              onChange={(event) => setReason(event.target.value as SuspensionReason)}
            >
              {SUSPENSION_REASONS.map((value) => (
                <option key={value} value={value}>
                  {SUSPENSION_REASON_LABEL[value]}
                </option>
              ))}
            </select>
            <p className="sim-note">
              La razón es un conjunto cerrado y queda solamente en auditoría: no se guarda en la cuenta ni se
              muestra en ninguna respuesta.
            </p>
            <p className="sim-note">
              Estado leído en el directorio: <strong>{ACCOUNT_STATUS_LABEL[pending.expectedStatus]}</strong>. Si la
              cuenta se movió mientras mirabas, el cambio no se aplica.
            </p>
            {error && <Alert variant="error">{error}</Alert>}
          </div>
        </Modal>
      )}

      {pending?.kind === "reactivate" && (
        <Modal
          title={`Levantar la suspensión de ${pending.row.email}`}
          description="La cuenta vuelve al estado en el que estaba cuando la suspensión la interrumpió. Las sesiones no vuelven."
          onClose={() => {
            setPending(null);
            setError("");
          }}
          footer={
            <>
              <Button variant="ghost" className="btn--inline" onClick={() => setPending(null)} disabled={working}>
                Cancelar
              </Button>
              <Button className="btn--inline" onClick={() => void run()} loading={working}>
                Levantar la suspensión
              </Button>
            </>
          }
        >
          <div className="form">
            {/* Sin tildar de entrada, y no lo tilda ningún camino de código: es
                quien opera diciendo que sabe dónde va a quedar la cuenta. */}
            <label className="check">
              <input
                type="checkbox"
                checked={acknowledged}
                onChange={(event) => setAcknowledged(event.target.checked)}
              />
              <span>
                Entiendo que si la persona se había dado de baja sola, la cuenta vuelve a esa baja y no a activa.
              </span>
            </label>
            {/* Una sola cosa a la vez: la advertencia mientras no haya respuesta, y
                después el rechazo que el servidor escribió. Dos textos diciendo
                lo mismo es como alguien contesta el que no era. */}
            {overSelfDeactivation && !error && (
              <p className="sim-note">
                Sin ese reconocimiento el servidor rechaza el cambio cuando la baja fue propia: la decisión de esa
                persona no es de quien opera para deshacerla.
              </p>
            )}
            {error && <Alert variant="error">{error}</Alert>}
          </div>
        </Modal>
      )}

      <section className="panel">
        <h2 className="panel__title">Qué dice cada estado</h2>
        <ul className="minilist">
          {(Object.keys(ACCOUNT_STATUS_LABEL) as AccountStatus[]).map((status) => (
            <li key={status} className="minilist__item">
              <span>{ACCOUNT_STATUS_HELP[status]}</span>
              <Badge tone={TONE[status]}>{ACCOUNT_STATUS_LABEL[status]}</Badge>
            </li>
          ))}
        </ul>
      </section>

      {stepUp && (
        <StepUpModal
          onClose={() => setStepUp(false)}
          onDone={() => {
            setStepUp(false);
            reloadPlatform();
            list.reload();
            // La revalidación no termina el cambio que interrumpió: volver a
            // pedirlo es de quien opera, a propósito.
            setNotice("MFA revalidada. Volvé a pedir el cambio si todavía lo querés.");
          }}
        />
      )}
    </>
  );
}
