import { useCallback, useEffect, useState } from 'react';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';

/**
 * The devices an identity is signed in on, and the two ways to end one (IA-REQ-049).
 *
 * Ending somebody else's device is a sensitive change, so the page asks for the password first and spends it
 * against the reauthentication endpoint. What comes back is nothing: the proof lives on the server and this page
 * never holds it, which is why the password is typed into a field that is cleared the moment it is used and is
 * never written anywhere (IA-REQ-025, IA-REQ-051).
 */
export function SessionsPage() {
  const identity = useIdentity();
  const [sessions, setSessions] = useState(null);
  const [problem, setProblem] = useState(null);
  const [password, setPassword] = useState('');
  const [isBusy, setIsBusy] = useState(false);

  const load = useCallback(async () => {
    try {
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
      await identity.client.reauthenticate(action, password);
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

      <label htmlFor="sessions-password">Password</label>
      <input
        id="sessions-password"
        type="password"
        autoComplete="current-password"
        value={password}
        onChange={(event) => setPassword(event.target.value)}
      />

      <ul>
        {(sessions ?? []).map((session) => (
          <li key={session.sessionRef}>
            <span>{session.deviceLabel}</span>
            {session.isCurrent && <span> — this device</span>}
            <span> · last seen {session.lastSeenAt}</span>
            {!session.isCurrent && (
              <button
                type="button"
                disabled={isBusy || password.length === 0}
                onClick={() => run('sessions.revoke-one', () => identity.client.revokeSession(session.sessionRef))}
              >
                End this device
              </button>
            )}
          </li>
        ))}
      </ul>

      <button
        type="button"
        disabled={isBusy || password.length === 0}
        onClick={() => run('sessions.revoke-others', () => identity.client.revokeOtherSessions())}
      >
        End every other device
      </button>
    </section>
  );
}
