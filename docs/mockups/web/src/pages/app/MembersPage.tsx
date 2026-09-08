import { useCallback, useEffect, useState } from "react";
import { Badge, Button, DataTable, PageHeader, StateBlock, type Column } from "../../components";
import { api, ApiError, NetworkError, type Member } from "../../lib/api";
import { useSession } from "../../lib/session";

export function MembersPage() {
  const { me } = useSession();
  const orgId = me?.activeOrg?.id ?? null;
  const [members, setMembers] = useState<Member[] | null>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "error" | "denied">("loading");
  const [message, setMessage] = useState("");

  const load = useCallback(async () => {
    if (!orgId) return;
    setStatus("loading");
    try {
      const data = await api.members(orgId);
      setMembers(data.members);
      setStatus("ready");
    } catch (error) {
      if (error instanceof ApiError && error.status === 403) {
        setMessage(error.message);
        setStatus("denied");
        return;
      }
      setMessage(error instanceof NetworkError ? error.message : "No pudimos traer la lista.");
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
        description="Los integrantes se listan por organización. Elegí una en el selector del menú lateral."
      />
    );
  }

  const columns: Column<Member>[] = [
    { key: "name", header: "Nombre", render: (row) => row.name },
    { key: "email", header: "Correo", render: (row) => row.email },
    { key: "role", header: "Rol", render: (row) => <Badge>{row.roleLabel}</Badge>, end: true },
  ];

  return (
    <>
      <PageHeader title="Integrantes" description={`Personas que integran ${me.activeOrg.name}.`} />

      {status === "loading" && <StateBlock kind="loading" title="Cargando integrantes…" />}

      {status === "denied" && (
        <StateBlock kind="denied" title="No podés ver esta sección" description={message} />
      )}

      {status === "error" && (
        <StateBlock
          kind="error"
          title="No pudimos traer la lista"
          description={message}
          action={<Button onClick={() => void load()}>Reintentar</Button>}
        />
      )}

      {status === "ready" && members && members.length === 0 && (
        <StateBlock
          kind="empty"
          title="Todavía no hay integrantes"
          description="Cuando alguien acepte una invitación va a aparecer acá."
        />
      )}

      {status === "ready" && members && members.length > 0 && (
        <DataTable caption={`Integrantes de ${me.activeOrg.name}`} columns={columns} rows={members} getKey={(row) => row.id} />
      )}
    </>
  );
}
