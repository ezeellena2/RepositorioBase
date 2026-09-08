import { useCallback, useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useFragmentToken } from '../useFragmentToken';
import { useIdentityProof } from '../useIdentityProof';
import { useSubmit } from '../useSubmit';

const AccountPath = '/identity/account';
const DeactivateAction = 'identity.account.deactivate';

export function AccountPage() {
  const identity = useIdentity();
  const proof = useIdentityProof();
  const navigate = useNavigate();
  const [password, setPassword] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [isBusy, setIsBusy] = useState(false);
  const [problem, setProblem] = useState(null);
  // Identity-wide self-service permissions are authorized by the API, not projected as tenant permissions.
  const allowed = identity.isAuthenticated;

  const deactivate = useCallback(async () => {
    await identity.deactivateAccount(() => navigate('/account/reactivation-request', { replace: true, state: { deactivated: true } }));
  }, [identity, navigate]);

  const run = useCallback(async (act) => {
    setIsBusy(true);
    setProblem(null);
    try { await act(); }
    catch (error) { setProblem(error.problem ?? { code: 'unexpected' }); }
    finally { setPassword(''); setIsBusy(false); }
  }, []);

  const waiting = proof.resumable(AccountPath);
  useEffect(() => {
    if (waiting === null || !proof.isReady) return;
    proof.forget();
    if (!allowed || waiting.action !== DeactivateAction || waiting.operation !== 'deactivate' || waiting.confirmed !== true) return;
    void Promise.resolve().then(() => run(deactivate));
  }, [waiting, proof, allowed, run, deactivate]);

  return (
    <section aria-labelledby="account-heading">
      <h1 id="account-heading">Your account</h1>
      <p>Deactivating ends all your sessions and prevents sign-in. Your memberships stay recorded. To return, request an email link and enter your current password.</p>
      <p>If you are the last administrator or Platform owner, give someone else that responsibility first.</p>
      <ProblemMessage problem={problem} />
      {!allowed ? <p>You do not have permission to deactivate this account.</p> : (
        <form onSubmit={(event) => {
          event.preventDefault();
          if (!confirmed || !proof.isReady || isBusy) return;
          void run(async () => {
            if (await proof.prove(DeactivateAction, password, { returnTo: AccountPath, operation: 'deactivate', confirmed: true })) await deactivate();
          });
        }}>
          {proof.hasPassword ? (
            <>
              <label htmlFor="account-password">Current password</label>
              <input id="account-password" type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} required />
            </>
          ) : (
            <p>{proof.provider ? `${proof.provider} will confirm it is you.` : 'No proof method is available.'} To return after deactivation, you will need a password. <Link to="/credentials/forgot">Set a password</Link>.</p>
          )}
          <label>
            <input type="checkbox" checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} />
            I understand that this ends all my sessions and stops sign-in.
          </label>
          <button type="submit" disabled={isBusy || !confirmed || !proof.isReady || !proof.canBegin(password)}>Deactivate my account</button>
        </form>
      )}
    </section>
  );
}

export function RequestReactivationPage() {
  const identity = useIdentity();
  const location = useLocation();
  const [email, setEmail] = useState('');
  const { submit, problem, isBusy, result } = useSubmit((address) => identity.client.requestAccountReactivation(address));

  return (
    <section aria-labelledby="reactivation-request-heading">
      <h1 id="reactivation-request-heading">Reactivate your account</h1>
      {location.state?.deactivated && <p role="status">Your account is deactivated. All your sessions have ended.</p>}
      <p>Use this if you deactivated your own account. This cannot lift an administrative suspension.</p>
      <ProblemMessage problem={problem} />
      {result ? <p role="status">If that account can be reactivated, we have sent a link to its email address. Check the inbox.</p> : (
        <form onSubmit={(event) => { event.preventDefault(); submit(email); }}>
          <label htmlFor="reactivation-email">Email</label>
          <input id="reactivation-email" type="email" autoComplete="username" value={email} onChange={(event) => setEmail(event.target.value)} required />
          <button type="submit" disabled={isBusy || identity.isLoading}>Send reactivation link</button>
        </form>
      )}
      <p>Returning requires your current password. <Link to="/credentials/forgot">Reset or set a password</Link> if needed, then request a reactivation link.</p>
      <Link to="/login">Sign in</Link>
    </section>
  );
}

export function ReactivateAccountPage() {
  const identity = useIdentity();
  const token = useFragmentToken();
  const [password, setPassword] = useState('');
  const { submit, problem, isBusy, result } = useSubmit(async (chosen) => {
    try { await identity.client.reactivateAccount(token ?? '', chosen); }
    finally { setPassword(''); }
  });

  return (
    <section aria-labelledby="reactivate-heading">
      <h1 id="reactivate-heading">Reactivate your account</h1>
      <ProblemMessage problem={problem} />
      {result ? (
        <>
          <p role="status">Your account is active again. Sign in to continue.</p>
          <Link to="/login">Sign in</Link>
        </>
      ) : (
        <>
          <form onSubmit={(event) => { event.preventDefault(); submit(password); }}>
            <label htmlFor="reactivate-password">Current password</label>
            <input id="reactivate-password" type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} required />
            <button type="submit" disabled={isBusy || identity.isLoading || !token}>Reactivate my account</button>
          </form>
          {!token && <p>Open the link from your reactivation email; this page needs its token.</p>}
          <p><Link to="/credentials/forgot">Reset or set a password</Link> if needed. Then <Link to="/account/reactivation-request">request a new reactivation link</Link>.</p>
        </>
      )}
    </section>
  );
}
