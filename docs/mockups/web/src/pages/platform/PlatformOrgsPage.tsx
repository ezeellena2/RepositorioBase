import { useCallback, useState } from "react";
import { Alert, Badge, Button, DataTable, Field, LoadMore, Modal, PageHeader, StateBlock, type Column } from "../../components";
import { api, ApiError, type PlatformOrgRow } from "../../lib/api";
import { shortDate, STATUS_LABEL } from "../../lib/format";
import { errorMessage } from "../../lib/session";
import { usePaged } from "../../lib/usePaged";
import { StepUpModal } from "./StepUpModal";
import { usePlatform } from "./PlatformShell";

type Pending = { org: PlatformOrgRow; action: "suspend" | "reactivate" } | null;

export function PlatformOrgsPage() {
  const { reloadPlatform } = usePlatform();
  const load = useCallback((cursor: string | null) => api.platformOrgs(cursor), []);
  const list = usePaged<PlatformOrgRow>(load);

  const [pending, setPending] = useState<Pending>(null);
  const [reason, setReason] = useState("");
  const [working, setWorking] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [stepUp, setStepUp] = useState(false);

  async function run() {
    if (!pending) return;
    setWorking(true);
    setError("");
    try {
      if (pending.action === "suspend") {
        await api.suspendOrg(pending.org.id, { reason: reason.trim(), version: pending.org.version });
        setNotice(`${pending.org.name} quedó suspendida.`);
      } else {
        await api.reactivateOrg(pending.org.id, { version: pending.org.version });
        setNotice(`${pending.org.name} volvió a estar activa.`);
      }
      setPending(null);
      setReason("");
      list.reload();
    } catch (caught) {
      if (caught instanceof ApiError && caught.code === "step_up_required") {
        setPending(null);
        setStepUp(true);
        return;
      }
      setError(errorMessage(caught));
    } finally {
      setWorking(false);
    }
  }

  const columns: Column<PlatformOrgRow>[] = [
    { key: "name", header: "Organización", render: (row) => row.name },
    {
      key: "status",
      header: "Estado",
      render: (row) => (
        <Badge tone={row.status === "suspended" ? "danger" : "ok"}>{STATUS_LABEL[row.status]}</Badge>
      ),
    },
    { key: "members", header: "Integrantes", render: (row) => row.membersCount, end: true },
    { key: "created", header: "Alta", render: (row) => shortDate(row.createdAt) },
    {
      key: "actions",
      header: "Acciones",
      end: true,
      render: (row) =>
        row.status === "active" ? (
          <button type="button" className="btn btn--text" onClick={() => setPending({ org: row, action: "suspend" })}>
            Suspender
          </button>
        ) : (
          <button type="button" className="btn btn--text" onClick={() => setPending({ org: row, action: "reactivate" })}>
            Reactivar
          </button>
        ),
    },
  ];

  return (
    <>
      <PageHeader
        title="Organizaciones"
        description="Sólo datos operativos. No se muestran CUIT ni información privada de negocio."
      />

      {notice && <Alert variant="success">{notice}</Alert>}
      {error && !pending && <Alert variant="error">{error}</Alert>}

      {list.status === "loading" && <StateBlock kind="loading" title="Cargando organizaciones…" />}
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
        <StateBlock kind="empty" title="No hay organizaciones" description="Cuando se registre una va a aparecer acá." />
      )}
      {list.status === "ready" && list.rows.length > 0 && (
        <>
          <DataTable caption="Directorio de organizaciones" columns={columns} rows={list.rows} getKey={(row) => row.id} />
          <LoadMore
            shown={list.rows.length}
            total={list.total}
            hasMore={list.hasMore}
            loading={list.loadingMore}
            onMore={list.more}
          />
        </>
      )}

      {pending && (
        <Modal
          title={pending.action === "suspend" ? `Suspender ${pending.org.name}` : `Reactivar ${pending.org.name}`}
          description={
            pending.action === "suspend"
              ? "Mientras esté suspendida, la organización no puede emitir invitaciones ni operar. No se borra nada."
              : "La organización vuelve a operar con normalidad."
          }
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
                {pending.action === "suspend" ? "Suspender" : "Reactivar"}
              </Button>
            </>
          }
        >
          <div className="form">
            {pending.action === "suspend" && (
              <Field
                label="Razón de la suspensión"
                value={reason}
                onChange={(event) => setReason(event.target.value)}
                placeholder="Falta de documentación impositiva."
                autoFocus
              />
            )}
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
            setNotice("MFA revalidada. Volvé a ejecutar la operación.");
          }}
        />
      )}
    </>
  );
}
