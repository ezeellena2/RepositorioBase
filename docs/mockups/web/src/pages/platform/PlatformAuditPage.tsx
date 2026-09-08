import { useCallback } from "react";
import { Badge, Button, DataTable, LoadMore, PageHeader, StateBlock, type Column } from "../../components";
import { api, type AuditRow } from "../../lib/api";
import { shortDateTime } from "../../lib/format";
import { usePaged } from "../../lib/usePaged";

export function PlatformAuditPage() {
  const load = useCallback((cursor: string | null) => api.platformAudit(cursor), []);
  const list = usePaged<AuditRow>(load);

  const columns: Column<AuditRow>[] = [
    { key: "at", header: "Cuándo", render: (row) => shortDateTime(row.at) },
    { key: "actor", header: "Quién", render: (row) => row.actorEmail },
    { key: "action", header: "Acción", render: (row) => <code className="code">{row.action}</code> },
    { key: "target", header: "Sobre", render: (row) => row.target },
    { key: "detail", header: "Detalle", render: (row) => row.detail },
    {
      key: "result",
      header: "Resultado",
      end: true,
      render: (row) => <Badge tone={row.result === "ok" ? "ok" : "danger"}>{row.result}</Badge>,
    },
  ];

  return (
    <>
      <PageHeader
        title="Auditoría"
        description="Registro simulado de operaciones sensibles. Es una maqueta: no reemplaza la auditoría del producto."
      />

      {list.status === "loading" && <StateBlock kind="loading" title="Cargando auditoría…" />}
      {list.status === "denied" && <StateBlock kind="denied" title="Sin permiso" description={list.message} />}
      {list.status === "error" && (
        <StateBlock
          kind="error"
          title="No pudimos traer la auditoría"
          description={list.message}
          action={<Button onClick={() => list.reload()}>Reintentar</Button>}
        />
      )}
      {list.status === "ready" && list.rows.length === 0 && (
        <StateBlock kind="empty" title="Sin registros" description="Las operaciones sensibles van a aparecer acá." />
      )}
      {list.status === "ready" && list.rows.length > 0 && (
        <>
          <DataTable caption="Auditoría de Platform" columns={columns} rows={list.rows} getKey={(row) => row.id} />
          <LoadMore
            shown={list.rows.length}
            total={list.total}
            hasMore={list.hasMore}
            loading={list.loadingMore}
            onMore={list.more}
          />
        </>
      )}
    </>
  );
}
