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

  const run = async (action, act) => {
    setIsBusy(true);
    setProblem(null);
    try {
      // A provider proof leaves for the provider instead of answering, so there is nothing to do here but stop:
      // what this operation was going to write waits for the round trip to come back.
      if (!await proof.prove(action, password)) return;
      await act();
      setPassword('');
      await load();
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
    } finally {
      setIsBusy(false);
    }
  };

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
                onClick={() => run('sessions.revoke-one', () => identity.client.revokeSession(session.sessionRef))}
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
          onClick={() => run('sessions.revoke-others', () => identity.client.revokeOtherSessions())}
        >
          End every other device
        </button>
      )}
    </section>
  );
}
