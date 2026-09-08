import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Alert, Badge, Button, DataTable, Field, Modal, PageHeader, StateBlock, type Column } from "../../components";
import { api, ApiError, NetworkError, type OrgInvitation, type OrgRole } from "../../lib/api";
import { INVITATION_STATUS_LABEL, ROLE_LABEL, shortDate } from "../../lib/format";
import { errorMessage, useSession } from "../../lib/session";

const TONE: Record<string, "ok" | "warn" | "danger" | "neutral"> = {
  pending: "warn",
  accepted: "ok",
  cancelled: "neutral",
  expired: "danger",
};

export function InvitationsPage() {
  const { me, reload } = useSession();
  const orgId = me?.activeOrg?.id ?? null;
  const suspended = me?.activeOrg?.status === "suspended";

  const [invitations, setInvitations] = useState<OrgInvitation[]>([]);
  const [roles, setRoles] = useState<OrgRole[]>([]);
  const [status, setStatus] = useState<"loading" | "ready" | "error" | "denied">("loading");
  const [message, setMessage] = useState("");

  const [inviteOpen, setInviteOpen] = useState(false);
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<OrgRole>("member");
  const [sending, setSending] = useState(false);
  const [inviteError, setInviteError] = useState("");
  const [notice, setNotice] = useState("");
  const [toCancel, setToCancel] = useState<OrgInvitation | null>(null);
  const [cancelling, setCancelling] = useState(false);

  const load = useCallback(async () => {
    if (!orgId) return;
    setStatus("loading");
    try {
      const data = await api.invitations(orgId);
      setInvitations(data.invitations);
      setRoles(data.assignableRoles);
      setRole(data.assignableRoles.includes("member") ? "member" : (data.assignableRoles[0] ?? "member"));
      setStatus("ready");
    } catch (error) {
      if (error instanceof ApiError && error.status === 403) {
        setMessage(error.message);
        setStatus("denied");
        return;
      }
      setMessage(error instanceof NetworkError ? error.message : "No pudimos traer las invitaciones.");
      setStatus("error");
    }
  }, [orgId]);

  useEffect(() => {
    void load();
  }, [load]);

  if (!me) return null;
  if (!me.activeOrg) {
    return (
      <StateBlock
        kind="empty"
        title="Elegí una organización"
        description="Las invitaciones se emiten por organización. Elegí una en el selector del menú lateral."
      />
    );
  }

  async function submitInvite(event: FormEvent) {
    event.preventDefault();
    if (!orgId) return;
    setInviteError("");
    setSending(true);
    try {
      await api.invite(orgId, { email, role });
      setInviteOpen(false);
      setEmail("");
      setNotice(`Invitación emitida a ${email}. El correo quedó en la bandeja simulada del panel de demostración.`);
      await load();
      await reload();
    } catch (error) {
      setInviteError(errorMessage(error));
    } finally {
      setSending(false);
    }
  }

  async function confirmCancel() {
    if (!orgId || !toCancel) return;
    setCancelling(true);
    try {
      await api.cancelInvitation(orgId, toCancel.id);
      setNotice(`La invitación a ${toCancel.email} quedó cancelada.`);
      setToCancel(null);
      await load();
    } catch (error) {
      setNotice(errorMessage(error));
      setToCancel(null);
    } finally {
      setCancelling(false);
    }
  }

  const columns: Column<OrgInvitation>[] = [
    { key: "email", header: "Correo", render: (row) => row.email },
    { key: "role", header: "Rol", render: (row) => ROLE_LABEL[row.role] },
    {
      key: "status",
      header: "Estado",
      render: (row) => <Badge tone={TONE[row.status] ?? "neutral"}>{INVITATION_STATUS_LABEL[row.status]}</Badge>,
    },
    { key: "expires", header: "Vence", render: (row) => shortDate(row.expiresAt) },
    {
      key: "actions",
      header: "Acciones",
      end: true,
      render: (row) =>
        row.status === "pending" ? (
          <button type="button" className="btn btn--text" onClick={() => setToCancel(row)}>
            Cancelar
          </button>
        ) : (
          <span className="muted">—</span>
        ),
    },
  ];

  return (
    <>
      <PageHeader
        title="Invitaciones"
        description={`Invitaciones emitidas desde ${me.activeOrg.name}.`}
        actions={
          // Si no hay permiso la acción no existe: no se muestra un botón que
          // aparente funcionar. El 403 de abajo explica por qué.
          status === "ready" ? (
            <Button className="btn--inline" onClick={() => setInviteOpen(true)} disabled={suspended}>
              Invitar integrante
            </Button>
          ) : null
        }
      />

      {suspended && (
        <Alert variant="error">
          La organización está suspendida: no se pueden emitir invitaciones hasta que Platform la reactive.
        </Alert>
      )}
      {notice && <Alert variant="success">{notice}</Alert>}

      {status === "loading" && <StateBlock kind="loading" title="Cargando invitaciones…" />}

      {status === "denied" && (
        <StateBlock
          kind="denied"
          title="No tenés permiso para gestionar invitaciones"
          description={
            <>
              {message} Cambiá de organización en el selector del menú lateral si querés operar donde sí tenés el permiso.
            </>
          }
        />
      )}

      {status === "error" && (
        <StateBlock
          kind="error"
          title="No pudimos traer las invitaciones"
          description={message}
          action={<Button onClick={() => void load()}>Reintentar</Button>}
        />
      )}

      {status === "ready" && invitations.length === 0 && (
        <StateBlock
          kind="empty"
          title="Todavía no emitiste invitaciones"
          description="Cuando invites a alguien vas a ver acá el estado de cada invitación."
        />
      )}

      {status === "ready" && invitations.length > 0 && (
        <DataTable
          caption={`Invitaciones de ${me.activeOrg.name}`}
          columns={columns}
          rows={invitations}
          getKey={(row) => row.id}
        />
      )}

      {inviteOpen && (
        <Modal
          title="Invitar integrante"
          description={`La invitación se emite en nombre de ${me.activeOrg.name}. Sólo podés otorgar roles de autoridad igual o menor a la tuya.`}
          onClose={() => setInviteOpen(false)}
          footer={
            <>
              <Button variant="ghost" className="btn--inline" onClick={() => setInviteOpen(false)} disabled={sending}>
                Cancelar
              </Button>
              <Button type="submit" form="invite-form" className="btn--inline" loading={sending}>
                Enviar invitación
              </Button>
            </>
          }
        >
          <form id="invite-form" className="form" onSubmit={submitInvite}>
            <Field
              label="Correo de la persona invitada"
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              placeholder="nombre@empresa.com.ar"
              required
            />
            <label className="field">
              <span className="field__label">Rol</span>
              <select className="select" value={role} onChange={(event) => setRole(event.target.value as OrgRole)}>
                {roles.map((option) => (
                  <option key={option} value={option}>
                    {ROLE_LABEL[option]}
                  </option>
                ))}
              </select>
            </label>
            {inviteError && <Alert variant="error">{inviteError}</Alert>}
          </form>
        </Modal>
      )}

      {toCancel && (
        <Modal
          title="Cancelar invitación"
          description={`Si cancelás, el enlace enviado a ${toCancel.email} deja de servir. Podés volver a invitar después.`}
          onClose={() => setToCancel(null)}
          footer={
            <>
              <Button variant="ghost" className="btn--inline" onClick={() => setToCancel(null)} disabled={cancelling}>
                Volver
              </Button>
              <Button className="btn--inline" onClick={() => void confirmCancel()} loading={cancelling}>
                Cancelar la invitación
              </Button>
            </>
          }
        />
      )}
    </>
  );
}
