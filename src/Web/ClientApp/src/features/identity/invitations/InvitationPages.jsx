import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useFragmentToken } from '../useFragmentToken';
import { useSubmit } from '../useSubmit';

/**
 * Registering from an invitation answers with a neutral bodyless 202 whether the token was live, dead, or
 * already belonged to an account, so this page says the same thing in every case.
 */
export function RegisterFromInvitationPage() {
  const identity = useIdentity();
  const token = useFragmentToken();
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
  const token = useFragmentToken();
  const { submit, problem, isBusy, result } = useSubmit(async (secret) => {
    const accepted = await identity.client.acceptInvitation(secret);
    // reload returns null on a read failure; acceptance still succeeded and must not be submitted again.
    await identity.reload();
    return accepted;
  });

  return (
    <section aria-labelledby="invitation-accept-heading">
      <h1 id="invitation-accept-heading">Accept your invitation</h1>
      <ProblemMessage problem={problem} />
      {result ? (
        <>
          <p role="status">You are a member now.</p>
          {identity.isAuthenticated ? <button type="button" onClick={() => navigate('/identity')}>Continue</button> : (
            <>
              <ProblemMessage problem={identity.contextProblem} />
              <p>Your membership was saved, but your access could not be refreshed. <Link to="/login">Sign in to continue</Link>.</p>
            </>
          )}
        </>
      ) : (
        <button type="button" disabled={isBusy || !identity?.isAuthenticated} onClick={() => submit(token ?? '')}>
          Accept
        </button>
      )}
      {isBusy && <p role="status">Accepting and refreshing your access…</p>}
      {!identity?.isAuthenticated && <p>Sign in with the invited address first.</p>}
    </section>
  );
}
