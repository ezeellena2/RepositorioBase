import { useEffect, useRef, type KeyboardEvent } from "react";
import { NavLink } from "react-router-dom";
import { degradedTarget, visibleNav } from "../../lib/nav";
import { useShell } from "../../lib/shell-context";
import { Icon } from "./Icons";

const FOCUSABLE = 'a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])';

interface SidebarProps {
  isDrawer: boolean;
  hamburgerRef: React.RefObject<HTMLButtonElement | null>;
}

export function Sidebar({ isDrawer, hamburgerRef }: SidebarProps) {
  const { me, navMin, drawerOpen, setDrawerOpen, openTenantPop } = useShell();
  const asideRef = useRef<HTMLElement>(null);
  const tenant = me?.activeTenant ?? null;
  const groups = visibleNav(tenant);
  const degradedItem = tenant && tenant.degraded.length > 0 ? degradedTarget(tenant) : undefined;
  const onlyHome = groups.length === 1 && groups[0].items.length === 1;

  // Drawer abierto: foco adentro, body sin scroll, Escape cierra y devuelve el foco.
  useEffect(() => {
    if (!isDrawer || !drawerOpen) return;
    const previous = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    requestAnimationFrame(() => asideRef.current?.querySelector<HTMLElement>(FOCUSABLE)?.focus());
    function onKey(e: globalThis.KeyboardEvent) {
      if (e.key === "Escape") {
        setDrawerOpen(false);
        hamburgerRef.current?.focus();
      }
    }
    document.addEventListener("keydown", onKey);
    return () => {
      document.body.style.overflow = previous;
      document.removeEventListener("keydown", onKey);
    };
  }, [isDrawer, drawerOpen, setDrawerOpen, hamburgerRef]);

  function trapFocus(e: KeyboardEvent<HTMLElement>) {
    if (!isDrawer || !drawerOpen || e.key !== "Tab") return;
    const nodes = Array.from(asideRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? []);
    if (nodes.length === 0) return;
    const first = nodes[0];
    const last = nodes[nodes.length - 1];
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  }

  return (
    <>
      {isDrawer && drawerOpen && <div className="shell-veil" onClick={() => setDrawerOpen(false)} aria-hidden="true" />}
      <aside
        id="shell-sidebar"
        ref={asideRef}
        className="shell-sidebar"
        aria-label="Navegación principal"
        aria-hidden={isDrawer && !drawerOpen ? true : undefined}
        onKeyDown={trapFocus}
      >
        <nav className="nav">
          {groups.map((g) => (
            <div key={g.group} className="nav-group">
              {g.label && (
                <div className="nav-group__label" aria-hidden={navMin && !isDrawer ? true : undefined}>
                  <span className="nav-group__text">{g.label}</span>
                </div>
              )}
              {g.items.map((item) => {
                const showDot = degradedItem?.id === item.id;
                return (
                  <NavLink
                    key={item.id}
                    to={item.to}
                    end={item.to === "/inicio"}
                    className={({ isActive }) => `nav-item${isActive ? " is-active" : ""}`}
                    aria-label={item.label}
                    data-tooltip={item.label}
                    tabIndex={isDrawer && !drawerOpen ? -1 : undefined}
                  >
                    <span className="nav-item__icon">
                      <Icon name={item.icon} />
                      {showDot && <span className="nav-item__dot nav-item__dot--icon" />}
                    </span>
                    <span className="nav-label">{item.label}</span>
                    {showDot && <span className="nav-item__dot" />}
                  </NavLink>
                );
              })}
            </div>
          ))}

          {onlyHome && me && (
            <div className="nav-empty">
              <p>En este contexto todavía no hay nada configurado.</p>
              {me.tenants.length > 1 && (
                <button type="button" className="btn btn--text" onClick={openTenantPop}>
                  Cambiar de contexto
                </button>
              )}
            </div>
          )}
        </nav>
      </aside>
    </>
  );
}
