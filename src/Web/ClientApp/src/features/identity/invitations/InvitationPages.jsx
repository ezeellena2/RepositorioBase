import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useSubmit } from '../useSubmit';

/**
 * The invitation token arrives in the URL fragment, which browsers never send to a server and proxies never log.
 * It is read once into memory and the fragment is erased with replaceState, so it does not survive in history,
 * in a bookmark, or in whatever the next page decides to log (IA-REQ-025/029).
 */
function useInvitationToken() {
  const [token] = useState(() => new URLSearchParams(window.location.hash.replace(/^#/, '')).get('token'));

  useEffect(() => {
    if (window.location.hash) {
      window.history.replaceState({}, '', `${window.location.pathname}${window.location.search}`);
    }
  }, []);

  return token;
}

/**
 * Registering from an invitation answers with a neutral bodyless 202 whether the token was live, dead, or
 * already belonged to an account, so this page says the same thing in every case.
 */
export function RegisterFromInvitationPage() {
  const identity = useIdentity();
  const token = useInvitationToken();
  const [password, setPassword] = useState('');
  const { submit, problem, isBusy, result } = useSubmit((secret, chosen) =>
    identity.client.registerFromInvitation(secret, chosen));

  if (result) {
    return (
      <section aria-labelledby="invitation-register-heading">
        <h1 id="invitation-register-heading">Set up your account</h1>
        <p role="status">Check your email. If that invitation is still open, we have sent you what you need to continue.</p>
      </section>
    );
  }

  return (
    <section aria-labelledby="invitation-register-heading">
      <h1 id="invitation-register-heading">Set up your account</h1>
      <ProblemMessage problem={problem} />
      <form onSubmit={(event) => { event.preventDefault(); submit(token ?? '', password); }}>
        <label htmlFor="invitation-password">Choose a password</label>
        <input id="invitation-password" type="password" autoComplete="new-password" value={password} onChange={(event) => setPassword(event.target.value)} required />
        <button type="submit" disabled={isBusy}>Continue</button>
      </form>
    </section>
  );
}

/** Accepting is idempotent: the same token answers with the same membership, so a retry is safe to offer. */
export function AcceptInvitationPage() {
  const identity = useIdentity();
  const navigate = useNavigate();
  const token = useInvitationToken();
  const { submit, problem, isBusy, result } = useSubmit((secret) => identity.client.acceptInvitation(secret));

  return (
    <section aria-labelledby="invitation-accept-heading">
      <h1 id="invitation-accept-heading">Accept your invitation</h1>
      <ProblemMessage problem={problem} />
      {result ? (
        <>
          <p role="status">You are a member now.</p>
          <button type="button" onClick={() => navigate('/identity')}>Continue</button>
        </>
      ) : (
        <button type="button" disabled={isBusy || !identity?.isAuthenticated} onClick={() => submit(token ?? '')}>
          Accept
        </button>
      )}
      {!identity?.isAuthenticated && <p>Sign in with the invited address first.</p>}
    </section>
  );
}
