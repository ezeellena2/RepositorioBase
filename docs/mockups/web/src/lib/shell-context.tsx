import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { api, ApiError, type Me, type Resumen } from "./api";

const NAV_KEY = "shell.nav";

interface ShellState {
  me: Me | null;
  resumen: Resumen | null;
  loading: boolean;
  /** id del tenant cuyo cambio está en vuelo */
  switching: string | null;
  flash: string | null;

  navMin: boolean;
  toggleNavMin: () => void;
  drawerOpen: boolean;
  setDrawerOpen: (open: boolean) => void;

  tenantPopOpen: boolean;
  setTenantPopOpen: (open: boolean) => void;
  /** Único control de contexto: las otras superficies invocan este popover. */
  openTenantPop: () => void;
  tenantPopRequest: number;

  selectTenant: (tenantId: string) => Promise<void>;
  logout: () => Promise<void>;
}

const Ctx = createContext<ShellState | null>(null);

function readNavPreference(): boolean | null {
  try {
    const value = window.localStorage.getItem(NAV_KEY);
    if (value === "min") return true;
    if (value === "full") return false;
  } catch {
    // sin localStorage: sin preferencia
  }
  return null;
}

export function ShellProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const location = useLocation();

  const [me, setMe] = useState<Me | null>(null);
  const [resumen, setResumen] = useState<Resumen | null>(null);
  const [loading, setLoading] = useState(true);
  const [switching, setSwitching] = useState<string | null>(null);
  const [flash, setFlash] = useState<string | null>(null);
  const flashTimer = useRef<number | null>(null);

  // Preferencia de la persona, no del contexto. Inicialización perezosa.
  const [navMin, setNavMin] = useState<boolean>(() => {
    const saved = readNavPreference();
    if (saved !== null) return saved;
    return window.matchMedia("(min-width: 1024px) and (max-width: 1279px)").matches;
  });
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [tenantPopOpen, setTenantPopOpen] = useState(false);
  const [tenantPopRequest, setTenantPopRequest] = useState(0);

  const loadResumen = useCallback(async (current: Me | null) => {
    if (!current?.activeTenant) {
      setResumen(null);
      return;
    }
    try {
      setResumen(await api.resumen());
    } catch {
      setResumen(null);
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const next = await api.me();
        if (cancelled) return;
        setMe(next);
        await loadResumen(next);
      } catch (error) {
        if (!cancelled && error instanceof ApiError && error.status === 401) {
          navigate("/login", { replace: true });
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [navigate, loadResumen]);

  // El drawer cierra al navegar.
  useEffect(() => {
    setDrawerOpen(false);
  }, [location.pathname]);

  const toggleNavMin = useCallback(() => {
    setNavMin((v) => {
      const next = !v;
      try {
        window.localStorage.setItem(NAV_KEY, next ? "min" : "full");
      } catch {
        // sin persistencia
      }
      return next;
    });
  }, []);

  const openTenantPop = useCallback(() => {
    setTenantPopOpen(true);
    setTenantPopRequest((n) => n + 1);
  }, []);

  const showFlash = useCallback((text: string) => {
    setFlash(text);
    if (flashTimer.current) window.clearTimeout(flashTimer.current);
    flashTimer.current = window.setTimeout(() => setFlash(null), 4000);
  }, []);

  const selectTenant = useCallback(
    async (tenantId: string) => {
      if (!me) return;
      if (me.activeTenant?.id === tenantId) {
        setTenantPopOpen(false);
        return;
      }
      setSwitching(tenantId);
      try {
        // La navegación cambia recién cuando la respuesta llegó.
        const next = await api.selectTenant(tenantId);
        let nextResumen: Resumen | null = null;
        if (next.activeTenant) {
          try {
            nextResumen = await api.resumen();
          } catch {
            nextResumen = null;
          }
        }
        setMe(next);
        setResumen(nextResumen);
        setTenantPopOpen(false);
        if (next.activeTenant) {
          const cuit = next.activeTenant.cuit ? ` · CUIT ${next.activeTenant.cuit}` : "";
          showFlash(`Ahora operás con ${next.activeTenant.name}${cuit}`);
        }
      } finally {
        setSwitching(null);
      }
    },
    [me, showFlash],
  );

  const logout = useCallback(async () => {
    try {
      await api.logout();
    } finally {
      navigate("/login", { replace: true });
    }
  }, [navigate]);

  const value = useMemo<ShellState>(
    () => ({
      me,
      resumen,
      loading,
      switching,
      flash,
      navMin,
      toggleNavMin,
      drawerOpen,
      setDrawerOpen,
      tenantPopOpen,
      setTenantPopOpen,
      openTenantPop,
      tenantPopRequest,
      selectTenant,
      logout,
    }),
    [me, resumen, loading, switching, flash, navMin, toggleNavMin, drawerOpen, tenantPopOpen, openTenantPop, tenantPopRequest, selectTenant, logout],
  );

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useShell(): ShellState {
  const value = useContext(Ctx);
  if (!value) throw new Error("useShell debe usarse dentro de ShellProvider");
  return value;
}
