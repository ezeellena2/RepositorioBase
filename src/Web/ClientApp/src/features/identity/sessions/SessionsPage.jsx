import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useIdentityProof } from '../useIdentityProof';

/**
 * The devices an identity is signed in on, and the two ways to end one (IA-REQ-049).
 *
 * Ending somebody else's device is a sensitive change, so the page buys a proof first. Which proof depends on
 * what this identity has: a password is typed into a field that is cleared the moment it is used and is never
 * written anywhere, and an identity that arrived through a provider proves the same thing by being sent back to
 * that provider. Either way nothing comes back here — the proof lives on the server (IA-REQ-025, IA-REQ-051).
 */
const SessionsPath = '/identity/sessions';

export function SessionsPage() {
  const identity = useIdentity();
  const proof = useIdentityProof();
  const [sessions, setSessions] = useState(null);
  const [problem, setProblem] = useState(null);
  const [password, setPassword] = useState('');
  const [isBusy, setIsBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      // Only the devices. What this screen may offer at all is a question about the identity rather than about
      // this list, so it is asked once, in one place, by the proof seam every sensitive screen shares.
      setSessions(await identity.client.listSessions());
      setProblem(null);
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
    }
  }, [identity]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!cancelled) await load();
    })();
    return () => { cancelled = true; };
  }, [load]);

  const run = async (action, act, intent = null) => {
    setIsBusy(true);
    setProblem(null);
    try {
      // A provider proof leaves for the provider instead of answering, so there is nothing to do here but stop:
      // what this operation was going to write waits for the round trip to come back. A null action means the
      // proof is already held — which is how a resumed operation re-enters here.
      if (action !== null && !await proof.prove(action, password, intent)) return;
      await act();
      setPassword('');
      await load();
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
    } finally {
      setIsBusy(false);
    }
  };

  // What this screen left behind before leaving for the provider, once the server has accepted the return. The
  // record is forgotten before the request goes out, so refreshing or replaying the return cannot repeat it; the
  // proof is single-use on the server, which refuses a repeat anyway.
  const waiting = proof.resumable(SessionsPath);
  useEffect(() => {
    if (waiting === null || sessions === null || !proof.isReady) return;
    proof.forget();
    // Started after this effect returns, not during it: the operation sets this screen's busy state as its
    // first act, and doing that inside an effect body is what turns one render into a cascade.
    const resume = (act) => { void Promise.resolve().then(act); };

    if (waiting.operation === 'revoke-others') {
      resume(() => run(null, () => identity.client.revokeOtherSessions()));
      return;
    }

    // The device has to still be listed, and still be another one. Between leaving and coming back it may have
    // expired, been ended elsewhere, or become the one being used.
    const target = sessions.find((session) => session.sessionRef === waiting.target);
    if (waiting.operation !== 'revoke-one' || target === undefined || target.isCurrent) return;
    resume(() => run(null, () => identity.client.revokeSession(target.sessionRef)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [waiting, sessions, proof.isReady]);

  return (
    <section aria-labelledby="sessions-heading">
      <h1 id="sessions-heading">Your devices</h1>
      <ProblemMessage problem={problem} />
      <p>Signing in somewhere else does not sign you out here. Ending a device asks for your password first.</p>

      {proof.hasPassword ? (
        <>
          <label htmlFor="sessions-password">Password</label>
          <input
            id="sessions-password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </>
      ) : proof.provider !== null ? (
        <p>You have no password here. Ending a device asks {proof.provider} to confirm it is you.</p>
      ) : (
        // A mailed reset is the one way in that needs no proof, which is exactly why it is the way out of here.
        <p>
          You signed in with a provider and have no password yet, so there is nothing to prove with.{' '}
          <Link to="/credentials/forgot">Set a password</Link> and this page can end a device.
        </p>
      )}

      {/* The rows wait for the proof seam as well as for the list. Showing a device before this screen knows
          what it may offer would render the ending controls twice: once wrong, then again right. */}
      <ul>
        {(sessions === null || !proof.isReady ? [] : sessions).map((session) => (
          <li key={session.sessionRef}>
            <span>{session.deviceLabel}</span>
            {session.isCurrent && <span> — this device</span>}
            <span> · last seen {session.lastSeenAt}</span>
            {!session.isCurrent && proof.canProve && (
              <button
                type="button"
                disabled={isBusy || !proof.canBegin(password)}
                onClick={() => run(
                  'sessions.revoke-one',
                  () => identity.client.revokeSession(session.sessionRef),
                  { returnTo: SessionsPath, operation: 'revoke-one', target: session.sessionRef })}
              >
                End this device
              </button>
            )}
          </li>
        ))}
      </ul>

      {proof.canProve && (
        <button
          type="button"
          disabled={isBusy || !proof.canBegin(password)}
          onClick={() => run(
            'sessions.revoke-others',
            () => identity.client.revokeOtherSessions(),
            { returnTo: SessionsPath, operation: 'revoke-others' })}
        >
          End every other device
        </button>
      )}
    </section>
  );
}
