import { useEffect } from "react";
import { useMenu } from "../../hooks/useMenu";
import { useMediaQuery } from "../../hooks/useMediaQuery";
import { ROLE_LABEL, typeLabel } from "../../lib/labels";
import { useShell } from "../../lib/shell-context";
import { Spinner } from "../Spinner";
import { Icon } from "./Icons";
import { Sigil } from "./Sigil";

const POP_ID = "tenant-pop";

export function TenantSwitch() {
  const { me, loading, switching, tenantPopOpen, setTenantPopOpen, tenantPopRequest, selectTenant } = useShell();
  const compact = useMediaQuery("(max-width: 480px)");
  const { triggerRef, panelRef, toggle, onPanelKeyDown } = useMenu({ open: tenantPopOpen, setOpen: setTenantPopOpen });

  // Pedido de apertura desde otra superficie (sello del Inicio, menú de perfil): foco al panel.
  useEffect(() => {
    if (tenantPopRequest === 0) return;
    requestAnimationFrame(() => {
      const checked = panelRef.current?.querySelector<HTMLElement>('[aria-checked="true"]');
      (checked ?? panelRef.current?.querySelector<HTMLElement>('[role="menuitemradio"]'))?.focus();
    });
  }, [tenantPopRequest, panelRef]);

  if (loading || !me) {
    return (
      <div className="tenant-switch is-loading" aria-busy="true" aria-label="Cargando contexto">
        <span className="sigil sigil--26 sigil--skeleton" />
        <span className="tenant-switch__lines">
          <span className="skeleton-bar" style={{ width: 96 }} />
          <span className="skeleton-bar" style={{ width: 72 }} />
        </span>
      </div>
    );
  }

  const active = me.activeTenant;
  if (!active) return null;

  const lines = (
    <span className="tenant-switch__lines">
      <span className="tenant-name">{active.name}</span>
      <span className="tenant-cuit">{active.cuit ?? ROLE_LABEL[active.role]}</span>
    </span>
  );

  if (me.tenants.length <= 1) {
    return (
      <div className={`tenant-badge${compact ? " is-compact" : ""}`}>
        <Sigil name={active.name} scope={active.scope} type={active.type} />
        {lines}
      </div>
    );
  }

  return (
    <div className="tenant-switch-wrap">
      <button
        ref={triggerRef}
        type="button"
        className={`tenant-switch${compact ? " is-compact" : ""}`}
        aria-haspopup="menu"
        aria-expanded={tenantPopOpen}
        aria-controls={POP_ID}
        aria-label={`Contexto activo: ${active.name}${active.cuit ? `, CUIT ${active.cuit}` : ""}. Cambiar de contexto`}
        onClick={toggle}
      >
        <Sigil name={active.name} scope={active.scope} type={active.type} />
        {lines}
        <Icon name="chevron" size={12} className={`tenant-switch__chevron${tenantPopOpen ? " is-open" : ""}`} />
      </button>

      {tenantPopOpen && (
        <div id={POP_ID} ref={panelRef} className="tenant-pop" role="menu" aria-label="Operando con" onKeyDown={onPanelKeyDown}>
          <div className="tenant-pop__label">Operando con</div>
          <div className="tenant-pop__list">
            {me.tenants.map((t) => {
              const isActive = t.id === active.id;
              const inFlight = switching === t.id;
              return (
                <button
                  key={t.id}
                  type="button"
                  role="menuitemradio"
                  aria-checked={isActive}
                  className={`tenant-opt${isActive ? " is-active" : ""}`}
                  disabled={switching !== null}
                  onClick={() => void selectTenant(t.id)}
                >
                  <Sigil name={t.name} scope={t.scope} type={t.type} size={30} dot={!isActive && t.hasDegraded} />
                  <span className="tenant-opt__lines">
                    <span className="tenant-opt__name">{t.name}</span>
                    <span className="tenant-opt__cuit">{t.cuit ?? "Sin CUIT"}</span>
                  </span>
                  <span className="tenant-opt__meta">
                    <span className="badge">{typeLabel(t)}</span>
                    <span className="tenant-opt__role">{ROLE_LABEL[t.role]}</span>
                  </span>
                  <span className="tenant-opt__state">
                    {inFlight ? <Spinner /> : isActive ? <Icon name="check" size={16} /> : null}
                  </span>
                </button>
              );
            })}
          </div>
          <div className="tenant-pop__foot">Cambiar de contexto cambia el menú y con qué CUIT se factura.</div>
        </div>
      )}
    </div>
  );
}
