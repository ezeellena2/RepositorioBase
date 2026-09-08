import { useCallback } from "react";
import { Badge, Button, DataTable, LoadMore, PageHeader, StateBlock, type Column } from "../../components";
import { api, type PlatformIdentityRow } from "../../lib/api";
import { shortDate } from "../../lib/format";
import { usePaged } from "../../lib/usePaged";

export function PlatformIdentitiesPage() {
  const load = useCallback((cursor: string | null) => api.platformIdentities(cursor), []);
  const list = usePaged<PlatformIdentityRow>(load);

  const columns: Column<PlatformIdentityRow>[] = [
    { key: "name", header: "Nombre", render: (row) => row.name },
    { key: "email", header: "Correo", render: (row) => row.email },
    {
      key: "confirmed",
      header: "Correo confirmado",
      render: (row) => <Badge tone={row.confirmed ? "ok" : "warn"}>{row.confirmed ? "Sí" : "Pendiente"}</Badge>,
    },
    { key: "orgs", header: "Organizaciones", render: (row) => row.orgsCount, end: true },
    { key: "created", header: "Alta", render: (row) => shortDate(row.createdAt) },
  ];

  return (
    <>
      <PageHeader
        title="Identidades"
        description="Directorio operativo. No hay acciones de suplantación ni acceso a datos privados de las organizaciones."
      />

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
    </>
  );
}
