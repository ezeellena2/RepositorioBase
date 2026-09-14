import { useCallback, useEffect, useState } from "react";
import { NavLink, Navigate, Outlet, useNavigate, useOutletContext } from "react-router-dom";
import { Badge, Button, StateBlock } from "../../components";
import { api, ApiError, type PlatformMe, type PlatformPermission } from "../../lib/api";
import { initials } from "../../lib/format";
import { useSession } from "../../lib/session";

export interface PlatformOutlet {
  platform: PlatformMe;
  reloadPlatform: () => void;
  /**
   * Para quien opera, no como control: la API vuelve a autorizar cada llamada.
   * Sirve para no ofrecer un botón cuya única respuesta posible es un rechazo.
   */
  can: (permission: PlatformPermission) => boolean;
}

export function usePlatform(): PlatformOutlet {
  return useOutletContext<PlatformOutlet>();
}

/**
 * Contexto operativo de Platform. Es independiente del cliente: no comparte
 * menú, selector de organización ni navegación, y Platform nunca aparece como
 * una organización más.
 */
export function PlatformShell() {
  const navigate = useNavigate();
  const { me, loading, logout } = useSession();
  const [platform, setPlatform] = useState<PlatformMe | null>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "denied" | "error">("loading");
  const [message, setMessage] = useState("");

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      setPlatform(await api.platformMe());
      setStatus("ready");
    } catch (error) {
      if (error instanceof ApiError && error.status === 403) {
        setMessage(error.message);
        setStatus("denied");
        return;
      }
      if (error instanceof ApiError && error.status === 401) return;
      setMessage("No pudimos leer tu acceso a Platform.");
      setStatus("error");
    }
  }, []);

  useEffect(() => {
    if (!loading && me) void load();
  }, [loading, me, load]);

  // Sólo la primera lectura ocupa la pantalla. Una revalidación vuelve a leer el
  // acceso sin desmontar lo que está debajo: si lo desmontara, cada step-up se
  // llevaría puesto el aviso que explica qué pasó con la operación interrumpida.
  if (loading || (status === "loading" && !platform)) {
    return (
      <div className="plat">
        <main className="plat__main">
          <StateBlock kind="loading" title="Verificando tu acceso a Platform…" />
        </main>
      </div>
    );
  }

  if (!me) return null;

  if (status === "denied") {
    return (
      <div className="plat">
        <main className="plat__main">
          <StateBlock
            kind="denied"
            title="Sin acceso a Platform"
            description={message}
            action={
              <Button variant="ghost" onClick={() => navigate("/app")}>
                Volver a la aplicación
              </Button>
            }
          />
        </main>
      </div>
    );
  }

  if (status === "error" || !platform) {
    return (
      <div className="plat">
        <main className="plat__main">
          <StateBlock kind="error" title="No pudimos verificar tu acceso" description={message} action={<Button onClick={() => void load()}>Reintentar</Button>} />
        </main>
      </div>
    );
  }

  // La membresía Platform se activa recién al terminar la secuencia de MFA simulada.
  if (!platform.activated || !platform.mfa.enrolled) {
    return <Navigate to="/platform/mfa" replace />;
  }

  return (
    <div className="plat">
      <header className="plat__top">
        <div className="plat__brand">
          <span className="plat__mark">Platform</span>
          <span className="plat__tag">Contexto operativo · separado del producto</span>
        </div>
        <nav className="plat__nav" aria-label="Secciones de Platform">
          <NavLink to="/platform" end className={({ isActive }) => `platnav${isActive ? " is-active" : ""}`}>
            Resumen
          </NavLink>
          <NavLink to="/platform/organizaciones" className={({ isActive }) => `platnav${isActive ? " is-active" : ""}`}>
            Organizaciones
          </NavLink>
          <NavLink to="/platform/identidades" className={({ isActive }) => `platnav${isActive ? " is-active" : ""}`}>
            Identidades
          </NavLink>
          <NavLink to="/platform/retencion" className={({ isActive }) => `platnav${isActive ? " is-active" : ""}`}>
            Retención
          </NavLink>
          <NavLink to="/platform/administradores" className={({ isActive }) => `platnav${isActive ? " is-active" : ""}`}>
            Administradores
          </NavLink>
          <NavLink to="/platform/auditoria" className={({ isActive }) => `platnav${isActive ? " is-active" : ""}`}>
            Auditoría
          </NavLink>
        </nav>
        <div className="plat__identity">
          <Badge tone={platform.stepUpFresh ? "ok" : "warn"}>
            MFA {platform.stepUpFresh ? "vigente" : "sin step-up"}
          </Badge>
          <span className="plat__avatar" aria-hidden="true">
            {initials(me.user.name)}
          </span>
          <span className="plat__who">{me.user.email}</span>
          <button type="button" className="plat__logout" onClick={() => void logout()}>
            Cerrar sesión
          </button>
        </div>
      </header>

      <main className="plat__main">
        <Outlet
          context={{
            platform,
            reloadPlatform: load,
            can: (permission: PlatformPermission) => platform.permissions.includes(permission),
          }}
        />
      </main>
    </div>
  );
}
