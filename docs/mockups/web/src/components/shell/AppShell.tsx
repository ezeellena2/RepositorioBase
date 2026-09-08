import { useState } from "react";
import { NavLink, Outlet } from "react-router-dom";
import { Button, StateBlock } from "../index";
import { useSession } from "../../lib/session";
import { ContextBar } from "./ContextBar";
import { OrgSwitcher } from "./OrgSwitcher";
import { UserMenu } from "./UserMenu";

/**
 * Shell del cliente. El menú lateral se colapsa y despliega con el botón de
 * flecha sobre el borde, igual que antes; ahora además contiene el selector de
 * organización y una navegación que depende de los permisos del contexto.
 */
export function AppShell() {
  const { me, loading, networkError, reload, logout, can } = useSession();
  const [navOpen, setNavOpen] = useState(true);

  if (loading) {
    return (
      <div className="app">
        <main className="app__main">
          <StateBlock kind="loading" title="Cargando tu contexto…" />
        </main>
      </div>
    );
  }

  if (networkError) {
    return (
      <div className="app">
        <main className="app__main">
          <StateBlock
            kind="error"
            title="No pudimos cargar tu contexto"
            description="El servidor del mockup no respondió. Reintentá; si el escenario armó una falla de red, ya quedó consumida."
            action={<Button onClick={() => void reload()}>Reintentar</Button>}
          />
        </main>
      </div>
    );
  }

  if (!me) return null;

  const toggleLabel = navOpen ? "Colapsar el menú" : "Desplegar el menú";
  const hasOrg = Boolean(me.activeOrg);

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar__side">
          <span className="brand brand--sm">Plataforma</span>
        </div>
        <div className="topbar__side topbar__side--end">
          <UserMenu name={me.user.name} email={me.user.email} onLogout={() => void logout()} />
        </div>
      </header>

      <div className="app__body">
        <aside className={`sidenav${navOpen ? "" : " is-collapsed"}`} aria-label="Contexto y navegación">
          <button
            type="button"
            className="sidenav__toggle"
            onClick={() => setNavOpen((open) => !open)}
            aria-expanded={navOpen}
            aria-controls="sidenav-panel"
            aria-label={toggleLabel}
            title={toggleLabel}
          >
            <svg className="sidenav__arrow" width="14" height="14" viewBox="0 0 16 16" aria-hidden="true">
              <path d="M10 3.5 5.5 8 10 12.5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </button>

          <div className="sidenav__panel" id="sidenav-panel" hidden={!navOpen}>
            <OrgSwitcher />

            <nav className="sidenav__nav" aria-label="Secciones">
              <NavLink to="/app" end className={({ isActive }) => `navitem${isActive ? " is-active" : ""}`}>
                Inicio
              </NavLink>
              {hasOrg && (
                <NavLink to="/app/miembros" className={({ isActive }) => `navitem${isActive ? " is-active" : ""}`}>
                  Integrantes
                </NavLink>
              )}
              {hasOrg && can("members.invite") && (
                <NavLink to="/app/invitaciones" className={({ isActive }) => `navitem${isActive ? " is-active" : ""}`}>
                  Invitaciones
                </NavLink>
              )}
              <NavLink
                to="/app/organizaciones/nueva"
                className={({ isActive }) => `navitem${isActive ? " is-active" : ""}`}
              >
                Crear organización
              </NavLink>
            </nav>

            {hasOrg && !can("members.invite") && (
              <p className="sidenav__note">
                En este contexto tu rol no incluye invitar integrantes, así que la sección no aparece.
              </p>
            )}
          </div>
        </aside>

        <main className="app__main">
          <ContextBar />
          <Outlet />
        </main>
      </div>
    </div>
  );
}
