import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { api, ApiError, NetworkError, type Me, type Permission } from "./api";

// Identidad autenticada + organización activa. Cambiar de organización usa la
// misma sesión: nunca cambia la identidad ni suplanta a nadie.

interface SessionValue {
  me: Me | null;
  loading: boolean;
  networkError: boolean;
  switching: boolean;
  reload: () => Promise<void>;
  switchOrg: (orgId: string) => Promise<void>;
  logout: () => Promise<void>;
  can: (permission: Permission) => boolean;
}

const SessionContext = createContext<SessionValue | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const [me, setMe] = useState<Me | null>(null);
  const [loading, setLoading] = useState(true);
  const [networkError, setNetworkError] = useState(false);
  const [switching, setSwitching] = useState(false);

  const reload = useCallback(async () => {
    setNetworkError(false);
    try {
      setMe(await api.me());
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        setMe(null);
        navigate("/entrar?motivo=sesion", { replace: true });
        return;
      }
      if (error instanceof NetworkError) setNetworkError(true);
    } finally {
      setLoading(false);
    }
  }, [navigate]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const switchOrg = useCallback(
    async (orgId: string) => {
      setSwitching(true);
      try {
        setMe(await api.selectOrg(orgId));
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) {
          setMe(null);
          navigate("/entrar?motivo=sesion", { replace: true });
          return;
        }
        if (error instanceof NetworkError) setNetworkError(true);
      } finally {
        setSwitching(false);
      }
    },
    [navigate],
  );

  const logout = useCallback(async () => {
    try {
      await api.logout();
    } finally {
      setMe(null);
      navigate("/entrar", { replace: true });
    }
  }, [navigate]);

  const value = useMemo<SessionValue>(
    () => ({
      me,
      loading,
      networkError,
      switching,
      reload,
      switchOrg,
      logout,
      can: (permission) => Boolean(me?.activeOrg?.permissions.includes(permission)),
    }),
    [me, loading, networkError, switching, reload, switchOrg, logout],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionValue {
  const value = useContext(SessionContext);
  if (!value) throw new Error("useSession fuera de SessionProvider");
  return value;
}

/** Traduce un error de la API al aviso que se muestra en pantalla. */
export function errorMessage(error: unknown, fallback = "Algo no salió bien. Probá de nuevo."): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof NetworkError) return error.message;
  return fallback;
}
