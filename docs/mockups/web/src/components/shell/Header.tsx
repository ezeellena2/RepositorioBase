import { useEffect, useState, type RefObject } from "react";
import { useNavigate } from "react-router-dom";
import { degradedTarget } from "../../lib/nav";
import { useShell } from "../../lib/shell-context";
import { Icon } from "./Icons";
import { ProfileMenu } from "./ProfileMenu";
import { TenantSwitch } from "./TenantSwitch";

interface HeaderProps {
  isDrawer: boolean;
  hamburgerRef: RefObject<HTMLButtonElement | null>;
}

export function Header({ isDrawer, hamburgerRef }: HeaderProps) {
  const navigate = useNavigate();
  const { me, navMin, toggleNavMin, drawerOpen, setDrawerOpen } = useShell();
  const [scrolled, setScrolled] = useState(false);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 2);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  const tenant = me?.activeTenant ?? null;
  const target = tenant ? degradedTarget(tenant) : undefined;
  const expanded = isDrawer ? drawerOpen : !navMin;

  return (
    <header className={`shell-header${scrolled ? " is-scrolled" : ""}`}>
      <button
        ref={hamburgerRef}
        type="button"
        className="icon-btn"
        aria-label={expanded ? "Cerrar el menú" : "Abrir el menú"}
        aria-expanded={expanded}
        aria-controls="shell-sidebar"
        onClick={() => (isDrawer ? setDrawerOpen(!drawerOpen) : toggleNavMin())}
      >
        <Icon name="menu" />
      </button>

      <span className="shell-wordmark">Plataforma</span>
      <span className="shell-header__sep" aria-hidden="true" />

      <TenantSwitch />

      <span className="shell-header__spacer" />

      {tenant?.degraded.map((d) => (
        <button
          key={d.code}
          type="button"
          className={`shell-status ${d.severity}`}
          onClick={() => target && navigate(target.to)}
          aria-label={`${d.text}. Ir a ${target?.label ?? "la pantalla correspondiente"}`}
        >
          <span className="shell-status__dot" aria-hidden="true" />
          {d.text}
        </button>
      ))}

      <ProfileMenu />
    </header>
  );
}
