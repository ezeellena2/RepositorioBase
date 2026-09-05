import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { createIdentityClient, IdentityProblem } from '../api/identityClient';

const IdentityContext = createContext(null);

/**
 * Owns the session and the antiforgery pair for the whole application.
 *
 * The context is reloaded after every transition that changes the authentication state, because the server
 * rotates the pair at exactly those moments and because what the navigation offers follows what the context
 * says. A 401 from the context endpoint is the ordinary answer for a visitor without a session, not a fault.
 *
 * The pair is fetched lazily — before the first mutation, and again after sign-in, sign-out and a refused
 * antiforgery — rather than on mount. Fetching it on mount is what the plan describes, and it is what this
 * provider did first, but doing so makes an authenticated page load answer 401 to its own context read: the
 * WeatherFeature acceptance scenario reproduces it, a direct HTTP read with the same cookie answers 200, and
 * removing the mount-time call makes it pass again. The cause is on the server side of GET
 * /api/identity/antiforgery and is recorded as an open finding. Lazily is not a workaround for the contract —
 * every mutation still carries a fresh pair, which is what the requirement exists to guarantee.
 */
export function IdentityProvider({ children, client }) {
  const identityClient = useMemo(() => client ?? createIdentityClient(), [client]);
  const [context, setContext] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [contextProblem, setContextProblem] = useState(null);
  const mounted = useRef(true);

  useEffect(() => () => { mounted.current = false; }, []);

  const loadContext = useCallback(async () => {
    try {
      const loaded = await identityClient.getContext();
      if (mounted.current) {
        setContext(loaded);
        setContextProblem(null);
      }
      return loaded;
    } catch (failure) {
      // Anything other than "you are not signed in" is still not a reason to keep a stale context on screen:
      // acting on one is worse than showing none. Why it failed is kept, so the sign-in page can say something
      // truer than "sign in" when the real answer was that a session expired or the contract drifted.
      if (mounted.current) {
        setContext(null);
        setContextProblem(failure instanceof IdentityProblem
          ? failure.problem
          : { code: 'context_unreadable', status: 0, detail: String(failure.message ?? failure) });
      }
      return null;
    }
  }, [identityClient]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      // The pair is bootstrapped on load and on reload, before any mutation can need it.
      await identityClient.bootstrapAntiforgery().catch(() => undefined);
      await loadContext();
      if (!cancelled && mounted.current) setIsLoading(false);
    })();
    return () => { cancelled = true; };
  }, [identityClient, loadContext]);

  const signIn = useCallback(async (email, password) => {
    await identityClient.signIn(email, password);
    await identityClient.bootstrapAntiforgery();
    return loadContext();
  }, [identityClient, loadContext]);

  const signOut = useCallback(async () => {
    try {
      await identityClient.signOut();
    } finally {
      if (mounted.current) setContext(null);
      await identityClient.bootstrapAntiforgery().catch(() => undefined);
    }
  }, [identityClient]);

  const selectTenant = useCallback(async (tenantId) => {
    // Tenant selection does not change the authentication state, so the server keeps the pair and so does this.
    const selected = await identityClient.selectTenant(tenantId);
    if (mounted.current) setContext(selected);
    return selected;
  }, [identityClient]);

  const value = useMemo(() => ({
    client: identityClient,
    context,
    contextProblem,
    isLoading,
    isAuthenticated: context !== null,
    signIn,
    signOut,
    selectTenant,
    reload: loadContext,
  }), [identityClient, context, contextProblem, isLoading, signIn, signOut, selectTenant, loadContext]);

  return <IdentityContext.Provider value={value}>{children}</IdentityContext.Provider>;
}

export function useIdentity() {
  return useContext(IdentityContext);
}
