import { useEffect, useRef, useState, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { useIdentity } from "../lib/identity";
import { TenantAvatar, UserAvatar } from "./Avatar";
import { CheckIcon, ChevronDownIcon } from "./Icons";
import { Wordmark } from "./Wordmark";
import { describeTenant } from "../pages/ChooseWorkspacePage";

function useClickOutside<T extends HTMLElement>(onOutside: () => void) {
  const ref = useRef<T>(null);
  useEffect(() => {
    function handle(event: MouseEvent) {
      if (ref.current && !ref.current.contains(event.target as Node)) onOutside();
    }
    document.addEventListener("mousedown", handle);
    return () => document.removeEventListener("mousedown", handle);
  }, [onOutside]);
  return ref;
}

function TenantMenu() {
  const { context, selectTenant } = useIdentity();
  const [open, setOpen] = useState(false);
  const ref = useClickOutside<HTMLDivElement>(() => setOpen(false));
  if (!context?.activeTenant) return null;
  const active = context.activeTenant;

  return (
    <div className="menu" ref={ref}>
      <button type="button" className="menu__trigger" aria-expanded={open} aria-haspopup="menu" onClick={() => setOpen((o) => !o)}>
        <TenantAvatar name={active.name} type={active.type} size="sm" />
        <span className="menu__trigger-text">
          <span className="menu__trigger-title">{active.name}</span>
          <span className="menu__trigger-sub">{describeTenant(active)}</span>
        </span>
        <span className="menu__chevron">
          <ChevronDownIcon />
        </span>
      </button>
      {open && (
        <div className="menu__panel" role="menu">
          <div className="menu__heading">Cambiar de espacio</div>
          {context.availableTenants.map((t) => (
            <button
              key={t.id}
              type="button"
              role="menuitemradio"
              aria-checked={t.id === active.id}
              className="menu__item"
              onClick={async () => {
                setOpen(false);
                if (t.id !== active.id) await selectTenant(t.id);
              }}
            >
              <TenantAvatar name={t.name} type={t.type} size="sm" />
              <span className="menu__item-body">
                <span className="menu__item-title">{t.name}</span>
                <span className="menu__item-sub">{describeTenant(t)}</span>
              </span>
              {t.id === active.id && (
                <span className="menu__check">
                  <CheckIcon />
                </span>
              )}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function UserMenu() {
  const { context, signOut } = useIdentity();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const ref = useClickOutside<HTMLDivElement>(() => setOpen(false));
  if (!context) return null;

  async function leave() {
    await signOut();
    navigate("/iniciar-sesion?salida=1", { replace: true });
  }

  return (
    <div className="menu" ref={ref}>
      <button type="button" className="menu__trigger" aria-expanded={open} aria-haspopup="menu" aria-label="Menú de la cuenta" onClick={() => setOpen((o) => !o)}>
        <UserAvatar name={context.user.displayName} />
        <span className="menu__chevron">
          <ChevronDownIcon />
        </span>
      </button>
      {open && (
        <div className="menu__panel" role="menu">
          <div className="menu__user">
            <div className="menu__user-name">{context.user.displayName}</div>
            <div className="menu__user-email">{context.user.email}</div>
          </div>
          <div className="menu__separator" />
          <button type="button" role="menuitem" className="menu__item" onClick={() => setOpen(false)}>
            Mi cuenta
          </button>
          <button type="button" role="menuitem" className="menu__item" onClick={leave}>
            Cerrar sesión
          </button>
        </div>
      )}
    </div>
  );
}

export function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="shell">
      <header className="topbar">
        <Wordmark to="/inicio" />
        <span className="topbar__spacer" />
        <TenantMenu />
        <span className="topbar__divider" />
        <UserMenu />
      </header>
      {children}
    </div>
  );
}
