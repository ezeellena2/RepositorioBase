import { useCallback, useState, type FormEvent } from "react";
import { Alert, Badge, Button, DataTable, Field, LoadMore, Modal, PageHeader, StateBlock, type Column } from "../../components";
import { api, ApiError, type PlatformAdminRow } from "../../lib/api";
import { errorMessage } from "../../lib/session";
import { usePaged } from "../../lib/usePaged";
import { StepUpModal } from "./StepUpModal";
import { usePlatform } from "./PlatformShell";

export function PlatformAdminsPage() {
  const { reloadPlatform } = usePlatform();
  const load = useCallback((cursor: string | null) => api.platformAdmins(cursor), []);
  const list = usePaged<PlatformAdminRow>(load);

  const [inviteOpen, setInviteOpen] = useState(false);
  const [email, setEmail] = useState("");
  const [toRevoke, setToRevoke] = useState<PlatformAdminRow | null>(null);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [stepUp, setStepUp] = useState(false);

  function handleFailure(caught: unknown, close: () => void) {
    if (caught instanceof ApiError && caught.code === "step_up_required") {
      close();
      setStepUp(true);
      return;
    }
    setError(errorMessage(caught));
  }

  async function invite(event: FormEvent) {
    event.preventDefault();
    setWorking(true);
    setError("");
    try {
      await api.invitePlatformAdmin(email.trim());
      setInviteOpen(false);
      setNotice(`Invitación enviada a ${email.trim()}. El correo quedó en la bandeja simulada.`);
      setEmail("");
      list.reload();
    } catch (caught) {
      handleFailure(caught, () => setInviteOpen(false));
    } finally {
      setWorking(false);
    }
  }

  async function revoke() {
    if (!toRevoke) return;
    setWorking(true);
    setError("");
    try {
      await api.revokePlatformAdmin(toRevoke.id);
      setNotice(`${toRevoke.email} ya no administra Platform.`);
      setToRevoke(null);
      list.reload();
    } catch (caught) {
      handleFailure(caught, () => setToRevoke(null));
    } finally {
      setWorking(false);
    }
  }

  const columns: Column<PlatformAdminRow>[] = [
    { key: "name", header: "Nombre", render: (row) => row.name },
    { key: "email", header: "Correo", render: (row) => row.email },
    {
      key: "role",
      header: "Rol",
      render: (row) => <Badge tone={row.role === "owner" ? "info" : "neutral"}>{row.role === "owner" ? "Titular" : "Administradora"}</Badge>,
    },
    {
      key: "state",
      header: "Estado",
      render: (row) => (
        <Badge tone={row.activated && row.mfaEnrolled ? "ok" : "warn"}>
          {row.activated && row.mfaEnrolled ? "Activa" : "MFA pendiente"}
        </Badge>
      ),
    },
    {
      key: "actions",
      header: "Acciones",
      end: true,
      render: (row) => (
        <button type="button" className="btn btn--text" onClick={() => setToRevoke(row)}>
          Revocar
        </button>
      ),
    },
  ];

  return (
    <>
      <PageHeader
        title="Administradores de Platform"
        description="Alta por invitación y baja por revocación. Toda mutación pide confirmación y MFA simulada reciente."
        actions={
          <Button className="btn--inline" onClick={() => setInviteOpen(true)}>
            Invitar administrador
          </Button>
        }
      />

      {notice && <Alert variant="success">{notice}</Alert>}
      {error && !inviteOpen && !toRevoke && <Alert variant="error">{error}</Alert>}

      {list.status === "loading" && <StateBlock kind="loading" title="Cargando administradores…" />}
      {list.status === "denied" && <StateBlock kind="denied" title="Sin permiso" description={list.message} />}
      {list.status === "error" && (
        <StateBlock
          kind="error"
          title="No pudimos traer el directorio"
          description={list.message}
          action={<Button onClick={() => list.reload()}>Reintentar</Button>}
        />
      )}
      {list.status === "ready" && list.rows.length > 0 && (
        <>
          <DataTable caption="Administradores de Platform" columns={columns} rows={list.rows} getKey={(row) => row.id} />
          <LoadMore
            shown={list.rows.length}
            total={list.total}
            hasMore={list.hasMore}
            loading={list.loadingMore}
            onMore={list.more}
          />
        </>
      )}

      {inviteOpen && (
        <Modal
          title="Invitar administrador de Platform"
          description="Va a recibir un enlace y tendrá que completar la secuencia de MFA simulada antes de activarse."
          onClose={() => {
            setInviteOpen(false);
            setError("");
          }}
          footer={
            <>
              <Button variant="ghost" className="btn--inline" onClick={() => setInviteOpen(false)} disabled={working}>
                Cancelar
              </Button>
              <Button type="submit" form="platform-invite" className="btn--inline" loading={working}>
                Enviar invitación
              </Button>
            </>
          }
        >
          <form id="platform-invite" className="form" onSubmit={invite}>
            <Field
              label="Correo"
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              placeholder="nombre@plataforma.com"
              autoFocus
            />
            {error && <Alert variant="error">{error}</Alert>}
          </form>
        </Modal>
      )}

      {toRevoke && (
        <Modal
          title={`Revocar a ${toRevoke.name}`}
          description="Pierde el acceso a Platform. No se borra la identidad ni sus organizaciones."
          onClose={() => {
            setToRevoke(null);
            setError("");
          }}
          footer={
            <>
              <Button variant="ghost" className="btn--inline" onClick={() => setToRevoke(null)} disabled={working}>
                Cancelar
              </Button>
              <Button className="btn--inline" onClick={() => void revoke()} loading={working}>
                Revocar acceso
              </Button>
            </>
          }
        >
          {error && <Alert variant="error">{error}</Alert>}
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
