import { createContext, startTransition, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { createIdentityClient, IdentityProblem } from '../api/identityClient';

const IdentityContext = createContext(null);

/**
 * Owns the session and the antiforgery pair for the whole application.
 *
 * The context is reloaded after every transition that changes the authentication state, because the server
 * rotates the pair at exactly those moments and because what the navigation offers follows what the context
 * says. A 401 from the context endpoint is the ordinary answer for a visitor without a session, not a fault.
 *
 * The pair is fetched on mount, and again after sign-in and sign-out, so every mutation carries a current one.
 * An earlier revision fetched it lazily instead, because doing it on mount appeared to make an authenticated
 * page load answer 401 to its own context read. That was never the server: the harness was injecting the
 * session cookie as an extra header, which the browser discards the moment its jar holds any cookie for the
 * origin — so bootstrapping was simply the first thing to put one there. With the harness corrected the
 * mount-time bootstrap is what the plan asks for and what runs.
 */
export function IdentityProvider({ children, client }) {
  const identityClient = useMemo(() => client ?? createIdentityClient(), [client]);
  const [context, setContext] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [contextProblem, setContextProblem] = useState(null);
  const mounted = useRef(true);

  useEffect(() => () => { mounted.current = false; }, []);

  const loadContext = useCallback(async ({ suppressAuthenticationRequired = true } = {}) => {
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
      //
      // `authentication_required` is not a failure during the initial anonymous bootstrap: it is what this
      // endpoint answers every visitor who opens /login without a session. After an explicit sign-in attempt it
      // is a refusal, though, and must reach the submit path so the sign-in card can explain the neutral outcome.
      // `invalid_session` still lands here, because an expired session is precisely what the paragraph above
      // wants the sign-in page to be able to say.
      const problem = failure instanceof IdentityProblem
        ? failure.problem
        : { code: 'context_unreadable', status: 0, detail: String(failure.message ?? failure) };
      const authenticationRequired = problem.code === 'authentication_required';
      if (mounted.current) {
        setContext(null);
        setContextProblem(suppressAuthenticationRequired && authenticationRequired ? null : problem);
      }
      if (!suppressAuthenticationRequired && authenticationRequired) throw failure;
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
    return loadContext({ suppressAuthenticationRequired: false });
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

  const deactivateAccount = useCallback(async (onDeactivated) => {
    await identityClient.deactivateAccount();
    // Only a successful deactivation ends this session. The endpoint also deleted the antiforgery cookie.
    // Finish the pair and let the caller leave its protected route before clearing context unmounts it.
    await identityClient.bootstrapAntiforgery().catch(() => undefined);
    if (mounted.current) {
      // Router navigation is a transition too; commit the destination and sign-out together.
      startTransition(() => {
        onDeactivated();
        setContext(null);
        setContextProblem(null);
      });
    }
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
    deactivateAccount,
    reload: loadContext,
  }), [identityClient, context, contextProblem, isLoading, signIn, signOut, selectTenant, deactivateAccount, loadContext]);

  return <IdentityContext.Provider value={value}>{children}</IdentityContext.Provider>;
}

export function useIdentity() {
  return useContext(IdentityContext);
}
