import { useCallback, useEffect, useRef, useState } from 'react';
import { useIdentity } from './context/IdentityProvider';
import { externalNavigation } from './externalNavigation';

/**
 * Where the interrupted operation waits.
 *
 * Proving through a provider is a full navigation away from this application and back, so every piece of React
 * state the operation was holding is gone by the time the person returns. What is kept is the least that can
 * name the operation again — who, where, which action, which target, and the draft they had already filled in.
 * Never a password, never a token, never anything the server would treat as authority (IA-REQ-025): the proof
 * itself lives on the server, and this record cannot buy one.
 *
 * `sessionStorage`, so it dies with the tab rather than waiting for the next person to open the browser.
 */
const PendingKey = 'identity.pending-proof';

const store = () => {
  try {
    return typeof window === 'undefined' ? null : window.sessionStorage;
  } catch {
    // A browser with site data blocked. The operation is simply not resumable; nothing else breaks.
    return null;
  }
};

const readPending = () => {
  try {
    const raw = store()?.getItem(PendingKey);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
};

const writePending = (record) => {
  try {
    store()?.setItem(PendingKey, JSON.stringify(record));
  } catch {
    // Nothing to do: without somewhere to keep it, the round trip simply will not resume by itself.
  }
};

/** Forgetting is the important half. It runs before the operation is retried, so a refresh cannot repeat it. */
export const forgetPendingProof = () => {
  try {
    store()?.removeItem(PendingKey);
  } catch {
    // Ignored for the same reason.
  }
};

/**
 * The provider round trip came back and the SERVER accepted it. Only then is the record allowed to describe an
 * operation that may run: a return address saying `proved` is a string in a URL, and the completion request is
 * what actually decides. Answers where to send the person, if anywhere.
 */
export const markPendingProofProved = () => {
  const record = readPending();
  if (record === null) return null;
  writePending({ ...record, proved: true });
  return record.returnTo ?? null;
};

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

  // Read once, on mount, and never claimed here: a screen that does not own this operation must be able to look
  // at it without destroying it for the screen that does.
  const [resumed] = useState(() => {
    const record = readPending();
    return record?.proved === true ? record : null;
  });

  // Claimed once, in a ref rather than in state, because forgetting must not itself cause a render: the screen
  // that resumes reloads its own data, and a cascading render there is how one operation becomes many.
  const claimed = useRef(false);

  /**
   * Taken by the screen that owns it, before the operation is retried. It forgets in both places: the stored
   * record so a refresh finds nothing, and this one so the screen's own reload — which is part of finishing the
   * operation — cannot present the same operation again and run it a second time.
   */
  const forget = useCallback(() => {
    claimed.current = true;
    forgetPendingProof();
  }, []);

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
  const prove = useCallback(async (action, password, intent = null) => {
    if (hasPassword) {
      await client.reauthenticate(action, password);
      return true;
    }

    if (provider === null) throw new Error('This identity has nothing to prove with.');
    const { authorizationRequestUri } = await client.startExternalProof(provider, action);

    // Written before leaving, because after `leaveFor` this application is no longer running. It is bound to the
    // identity, the tenant and the action so that a record left by one context cannot be picked up by another.
    if (intent !== null) {
      writePending({
        ...intent,
        action,
        userId: identity?.context?.user?.id ?? null,
        tenantId: identity?.context?.activeTenant?.id ?? null,
      });
    }

    externalNavigation.leaveFor(authorizationRequestUri);
    return false;
  }, [client, hasPassword, provider, identity]);

  /**
   * The operation this screen left behind, if the round trip really came back proved and it belongs here.
   * Everything is compared: a record for another screen, another identity or another organization is not this
   * screen's to resume.
   */
  const resumable = useCallback((returnTo) => {
    if (claimed.current || resumed === null || resumed.returnTo !== returnTo) return null;
    if ((resumed.userId ?? null) !== (identity?.context?.user?.id ?? null)) return null;
    if ((resumed.tenantId ?? null) !== (identity?.context?.activeTenant?.id ?? null)) return null;
    return resumed;
  }, [resumed, identity]);

  return {
    isReady,
    hasPassword,
    provider,
    /** Whether this identity can prove anything at all. False means the screen must offer no sensitive control. */
    canProve: hasPassword || provider !== null,
    /** Whether one can be begun right now: a password account needs the field filled, a provider account does not. */
    canBegin: (password) => (hasPassword ? password.length > 0 : provider !== null),
    prove,
    resumable,
    forget,
  };
}
