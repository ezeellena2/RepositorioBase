import { createContext, startTransition, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { isSessionLostProblem, toProblem } from '../../../api/apiTransport';
import { createIdentityClient, IdentityProblem } from '../api/identityClient';
import {
  isPseudoLanguageOverrideActive,
  isSupportedLanguage,
  PSEUDO_LANGUAGE,
  setLanguage,
} from '../../../i18n';

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
  const [pendingLanguage, setPendingLanguage] = useState(null);
  const [languageProblem, setLanguageProblem] = useState(null);
  const languageRequestRevision = useRef(0);
  const languageMutationPending = useRef(false);
  const languageMutationQueue = useRef(Promise.resolve());
  const acknowledgedLanguage = useRef(null);
  const contextRequestRevision = useRef(0);
  const sessionRevision = useRef(0);
  const mounted = useRef(true);

  useEffect(() => () => { mounted.current = false; }, []);

  const cancelLanguageChange = useCallback(() => {
    languageRequestRevision.current += 1;
    languageMutationPending.current = false;
    acknowledgedLanguage.current = null;
    setPendingLanguage(null);
    setLanguageProblem(null);
  }, []);

  const commitContext = useCallback((loaded, requestRevision, languageRevision, session) => {
    if (!mounted.current
      || requestRevision !== contextRequestRevision.current
      || session !== sessionRevision.current) return;

    const languageIsCurrent = !languageMutationPending.current
      && languageRevision === languageRequestRevision.current;
    if (languageIsCurrent) {
      setContext(loaded);
      if (loaded?.preferredLanguage) setLanguage(loaded.preferredLanguage);
    } else {
      // A full context response still owns its tenant/session projections, but not a language choice that was
      // made after that read began. Merging only that field prevents a delayed reload or tenant response from
      // reverting the catalog and cookie after the preference write has succeeded.
      setContext((current) => current === null
        ? loaded
        : { ...loaded, preferredLanguage: current.preferredLanguage });
    }
    setContextProblem(null);
  }, []);

  const loadContext = useCallback(async ({ suppressAuthenticationRequired = true } = {}) => {
    const requestRevision = ++contextRequestRevision.current;
    const languageRevision = languageRequestRevision.current;
    const session = sessionRevision.current;
    try {
      const loaded = await identityClient.getContext();
      commitContext(loaded, requestRevision, languageRevision, session);
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
      const problem = toProblem(failure);
      const authenticationRequired = problem.code === 'authentication_required';
      if (mounted.current
        && requestRevision === contextRequestRevision.current
        && session === sessionRevision.current
        && !languageMutationPending.current
        && languageRevision === languageRequestRevision.current) {
        cancelLanguageChange();
        setContext(null);
        setContextProblem(suppressAuthenticationRequired && authenticationRequired ? null : problem);
      }
      if (!suppressAuthenticationRequired && authenticationRequired) throw failure;
      return null;
    }
  }, [identityClient, commitContext, cancelLanguageChange]);

  useEffect(function subscribeToSessionLoss() {
    return identityClient.transport?.onSessionLost?.((problem) => {
      if (!mounted.current) return;
      sessionRevision.current += 1;
      contextRequestRevision.current += 1;
      cancelLanguageChange();
      setContext(null);
      setContextProblem(problem);
    });
  }, [identityClient, cancelLanguageChange]);

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
    sessionRevision.current += 1;
    contextRequestRevision.current += 1;
    cancelLanguageChange();
    await identityClient.signIn(email, password);
    await identityClient.bootstrapAntiforgery().catch(() => undefined);
    return loadContext({ suppressAuthenticationRequired: false });
  }, [identityClient, loadContext, cancelLanguageChange]);

  const signOut = useCallback(async () => {
    const session = ++sessionRevision.current;
    contextRequestRevision.current += 1;
    cancelLanguageChange();
    let sessionLostProblem = null;
    try {
      await identityClient.signOut();
    } catch (failure) {
      if (!(failure instanceof IdentityProblem) || !isSessionLostProblem(failure.problem)) throw failure;
      sessionLostProblem = failure.problem;
    }

    if (mounted.current && session === sessionRevision.current) {
      setContext(null);
      setContextProblem(sessionLostProblem);
    }
    await identityClient.bootstrapAntiforgery().catch(() => undefined);
    return true;
  }, [identityClient, cancelLanguageChange]);

  const selectTenant = useCallback(async (tenantId) => {
    // Tenant selection does not change the authentication state, so the server keeps the pair and so does this.
    const requestRevision = ++contextRequestRevision.current;
    const languageRevision = languageRequestRevision.current;
    const session = sessionRevision.current;
    const selected = await identityClient.selectTenant(tenantId);
    commitContext(selected, requestRevision, languageRevision, session);
    return selected;
  }, [identityClient, commitContext]);

  const changeLanguage = useCallback(async (language) => {
    if (isLoading) return null;
    if (isPseudoLanguageOverrideActive()) {
      setLanguage(PSEUDO_LANGUAGE);
      return null;
    }
    if (!isSupportedLanguage(language)) return null;

    const revision = ++languageRequestRevision.current;
    const session = sessionRevision.current;
    setLanguageProblem(null);

    if (context === null) {
      languageMutationPending.current = false;
      setPendingLanguage(null);
      setLanguage(language);
      return null;
    }

    if (!languageMutationPending.current) acknowledgedLanguage.current = null;
    languageMutationPending.current = true;
    setPendingLanguage(language);
    try {
      // The revision guard below owns what the browser applies, but it cannot order writes that have already
      // reached the server. Keep one queue across session transitions so an in-flight write from the old session
      // cannot finish after a new-session choice. A rejected write does not poison the queue, and the captured
      // session revision makes queued work from a superseded session stop before it is sent.
      const update = languageMutationQueue.current
        .catch(() => undefined)
        .then(() => {
          if (!mounted.current || session !== sessionRevision.current) return null;
          return identityClient.updatePreferredLanguage(language);
        });
      languageMutationQueue.current = update;
      const updated = await update;
      if (updated === null) return null;
      if (mounted.current && session === sessionRevision.current) {
        acknowledgedLanguage.current = updated.preferredLanguage;
      }
      if (mounted.current
        && revision === languageRequestRevision.current
        && session === sessionRevision.current) {
        languageRequestRevision.current += 1;
        languageMutationPending.current = false;
        setContext((current) => current === null
          ? current
          : { ...current, preferredLanguage: updated.preferredLanguage });
        setLanguage(updated.preferredLanguage);
        setPendingLanguage(null);
        setLanguageProblem(null);
        acknowledgedLanguage.current = null;
      }
      return updated;
    } catch (failure) {
      if (mounted.current
        && revision === languageRequestRevision.current
        && session === sessionRevision.current) {
        const acknowledged = acknowledgedLanguage.current;
        acknowledgedLanguage.current = null;
        languageRequestRevision.current += 1;
        languageMutationPending.current = false;
        if (acknowledged) {
          setContext((current) => current === null
            ? current
            : { ...current, preferredLanguage: acknowledged });
          setLanguage(acknowledged);
        }
        setPendingLanguage(null);
        setLanguageProblem(failure?.problem ?? { code: 'unknown', status: 0 });
      }
      throw failure;
    }
  }, [context, identityClient, isLoading]);

  const deactivateAccount = useCallback(async (onDeactivated) => {
    await identityClient.deactivateAccount();
    const session = ++sessionRevision.current;
    contextRequestRevision.current += 1;
    cancelLanguageChange();
    // Only a successful deactivation ends this session. The endpoint also deleted the antiforgery cookie.
    // Finish the pair and let the caller leave its protected route before clearing context unmounts it.
    await identityClient.bootstrapAntiforgery().catch(() => undefined);
    if (mounted.current && session === sessionRevision.current) {
      // Router navigation is a transition too; commit the destination and sign-out together.
      startTransition(() => {
        onDeactivated();
        setContext(null);
        setContextProblem(null);
      });
    }
  }, [identityClient, cancelLanguageChange]);

  const value = useMemo(() => ({
    client: identityClient,
    context,
    contextProblem,
    isLoading,
    isAuthenticated: context !== null,
    signIn,
    signOut,
    selectTenant,
    changeLanguage,
    pendingLanguage,
    languageProblem,
    deactivateAccount,
    reload: loadContext,
  }), [identityClient, context, contextProblem, isLoading, signIn, signOut, selectTenant, changeLanguage, pendingLanguage, languageProblem, deactivateAccount, loadContext]);

  return <IdentityContext.Provider value={value}>{children}</IdentityContext.Provider>;
}

export function useIdentity() {
  return useContext(IdentityContext);
}
