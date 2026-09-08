import type { ReactNode } from "react";

interface PageHeaderProps {
  title: string;
  description?: ReactNode;
  actions?: ReactNode;
}

export function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <div className="page-header">
      <div className="page-header__text">
        <h1 className="page-header__title">{title}</h1>
        {description && <p className="page-header__description">{description}</p>}
      </div>
      {actions && <div className="page-header__actions">{actions}</div>}
    </div>
  );
}

/** Paginación por cursor simulada: acumula páginas y muestra cuánto queda. */
export function LoadMore({
  shown,
  total,
  hasMore,
  loading,
  onMore,
}: {
  shown: number;
  total: number;
  hasMore: boolean;
  loading: boolean;
  onMore: () => void;
}) {
  return (
    <div className="load-more">
      <span className="load-more__count">
        {shown} de {total}
      </span>
      {hasMore && (
        <button type="button" className="btn btn--ghost btn--inline" onClick={onMore} disabled={loading}>
          {loading ? "Cargando…" : "Mostrar más"}
        </button>
      )}
    </div>
  );
}
