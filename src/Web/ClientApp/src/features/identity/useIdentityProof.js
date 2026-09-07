import { useCallback, useEffect, useState } from 'react';
import { useIdentity } from './context/IdentityProvider';
import { externalNavigation } from './externalNavigation';

/**
 * The two ways a sensitive change can be bought, in one place (IA-REQ-051).
 *
 * A screen asks this what it may offer and hands it the action; it never decides for itself. That matters
 * because an identity that arrived through a provider has no password to type, and a screen that gates its
 * buttons on a typed password locks that person out of every operation they hold the permission for — not
 * because the server refused them, but because the interface never let them ask.
 *
 * Both reads are optional. Neither may take a screen down with it: a deployment with no provider configured, or
 * a session that may not make one of these reads, means there is nothing here to prove with — not that the page
 * is broken. Until they land, `hasPassword` is true, which is exactly the behaviour every screen had before.
 */
export function useIdentityProof() {
  const identity = useIdentity();
  const client = identity?.client ?? null;
  const [state, setState] = useState({ isReady: false, hasPassword: true, provider: null });

  useEffect(() => {
    if (client === null) return undefined;
    let cancelled = false;
    (async () => {
      const [credentials, links] = await Promise.all([
        client.getOwnCredentials().catch(() => null),
        client.listExternalLinks().catch(() => null),
      ]);
      if (cancelled) return;
      // A linked account is only usable if the deployment still offers that provider: one linked to a provider
      // no longer configured has no route back, and offering it would be a button that cannot work.
      const available = links?.available ?? [];
      const linked = (links?.items ?? []).find((row) => available.includes(row.provider));
      setState({ isReady: true, hasPassword: credentials?.hasPassword ?? true, provider: linked?.provider ?? null });
    })();
    return () => { cancelled = true; };
  }, [client]);

  const { isReady, hasPassword, provider } = state;

  /**
   * Buys the proof for one action. `true` means a proof is held and the caller may send its change; `false`
   * means the browser has been handed to the provider, so the change must not be sent — it waits for the round
   * trip exactly as the password path waits for the reauthentication to answer.
   */
  const prove = useCallback(async (action, password) => {
    if (hasPassword) {
      await client.reauthenticate(action, password);
      return true;
    }

    if (provider === null) throw new Error('This identity has nothing to prove with.');
    const { authorizationRequestUri } = await client.startExternalProof(provider, action);
    externalNavigation.leaveFor(authorizationRequestUri);
    return false;
  }, [client, hasPassword, provider]);

  return {
    isReady,
    hasPassword,
    provider,
    /** Whether this identity can prove anything at all. False means the screen must offer no sensitive control. */
    canProve: hasPassword || provider !== null,
    /** Whether one can be begun right now: a password account needs the field filled, a provider account does not. */
    canBegin: (password) => (hasPassword ? password.length > 0 : provider !== null),
    prove,
  };
}
