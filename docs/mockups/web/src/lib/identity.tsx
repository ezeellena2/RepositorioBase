import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { api, ApiError, type IdentityContext } from "../api/client";

interface IdentityState {
  context: IdentityContext | null;
  loading: boolean;
  refresh: () => Promise<IdentityContext | null>;
  selectTenant: (tenantId: string) => Promise<IdentityContext>;
  signOut: () => Promise<void>;
}

const Ctx = createContext<IdentityState | null>(null);

export function IdentityProvider({ children }: { children: ReactNode }) {
  const [context, setContext] = useState<IdentityContext | null>(null);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    try {
      const next = await api.context();
      setContext(next);
      return next;
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        setContext(null);
        return null;
      }
      throw error;
    } finally {
      setLoading(false);
    }
  }, []);

  const selectTenant = useCallback(async (tenantId: string) => {
    const next = await api.selectTenant(tenantId);
    setContext(next);
    return next;
  }, []);

  const signOut = useCallback(async () => {
    try {
      await api.signOut();
    } finally {
      setContext(null);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const value = useMemo(
    () => ({ context, loading, refresh, selectTenant, signOut }),
    [context, loading, refresh, selectTenant, signOut],
  );

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useIdentity(): IdentityState {
  const value = useContext(Ctx);
  if (!value) throw new Error("useIdentity must be used inside IdentityProvider");
  return value;
}
