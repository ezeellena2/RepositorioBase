import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useFragmentToken } from '../useFragmentToken';
import { useSubmit } from '../useSubmit';

/**
 * "I forgot my password." The answer is the same whatever address is typed, so this page says the same thing
 * whatever happened — telling a visitor whether an address has an account would make this the enumeration route
 * the rest of the system is careful not to be (IA-REQ-029).
 */
export function ForgotPasswordPage() {
  const identity = useIdentity();
  const [email, setEmail] = useState('');
  const { submit, problem, isBusy, result } = useSubmit((address) => identity.client.requestPasswordRecovery(address));

  if (result) {
    return (
      <section aria-labelledby="forgot-heading">
        <h1 id="forgot-heading">Reset your password</h1>
        <p role="status">If that address can sign in, we have sent it a reset link. Check the inbox.</p>
      </section>
    );
  }

  return (
    <section aria-labelledby="forgot-heading">
      <h1 id="forgot-heading">Reset your password</h1>
      <ProblemMessage problem={problem} />
      <form onSubmit={(event) => { event.preventDefault(); submit(email); }}>
        <label htmlFor="forgot-email">Email</label>
        <input id="forgot-email" type="email" autoComplete="username" value={email} onChange={(event) => setEmail(event.target.value)} required />
        <button type="submit" disabled={isBusy}>Send the link</button>
      </form>
      <p>Remembered it? <Link to="/login">Sign in</Link>.</p>
    </section>
  );
}

/**
 * The screen the reset mail opens. The token arrives in the fragment and is erased from the address bar before
 * anything else happens, and the reset issues no session: the person signs in afterwards with what they just chose.
 */
export function ResetPasswordPage() {
  const identity = useIdentity();
  const token = useFragmentToken();
  const [password, setPassword] = useState('');
  const { submit, problem, isBusy, result } = useSubmit((secret, next) => identity.client.resetPassword(secret, next));

  if (result !== null && result !== undefined) {
    return (
      <section aria-labelledby="reset-heading">
        <h1 id="reset-heading">Choose a new password</h1>
        <p role="status">Your password is set. Sign in to continue.</p>
        <Link to="/login">Sign in</Link>
      </section>
    );
  }

  return (
    <section aria-labelledby="reset-heading">
      <h1 id="reset-heading">Choose a new password</h1>
      <ProblemMessage problem={problem} />
      <form onSubmit={(event) => { event.preventDefault(); submit(token ?? '', password); }}>
        <label htmlFor="reset-password">New password</label>
        <input id="reset-password" type="password" autoComplete="new-password" value={password} onChange={(event) => setPassword(event.target.value)} required />
        <button type="submit" disabled={isBusy || !token}>Set my password</button>
      </form>
      {!token && <p>Open the link from the reset email; this page needs the token it carries.</p>}
    </section>
  );
}

/**
 * Changing a password from inside the account. It asks for the current one to buy a server-side proof, then sends
 * the change — the API never receives a `currentPassword` field, and this page keeps neither value.
 */
export function ChangePasswordPage() {
  const identity = useIdentity();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [problem, setProblem] = useState(null);
  const [isBusy, setIsBusy] = useState(false);
  const [done, setDone] = useState(false);

  const change = async () => {
    setIsBusy(true);
    setProblem(null);
    try {
      await identity.client.reauthenticate('credentials.password.change', current);
      await identity.client.changePassword(next);
      setCurrent('');
      setNext('');
      setDone(true);
    } catch (error) {
      setProblem(error.problem ?? { code: 'unexpected' });
    } finally {
      setIsBusy(false);
    }
  };

  return (
    <section aria-labelledby="change-heading">
      <h1 id="change-heading">Change your password</h1>
      <ProblemMessage problem={problem} />
      {done && <p role="status">Your password is changed. Your other devices have been signed out.</p>}
      <form onSubmit={(event) => { event.preventDefault(); change(); }}>
        <label htmlFor="change-current">Current password</label>
        <input id="change-current" type="password" autoComplete="current-password" value={current} onChange={(event) => setCurrent(event.target.value)} required />
        <label htmlFor="change-next">New password</label>
        <input id="change-next" type="password" autoComplete="new-password" value={next} onChange={(event) => setNext(event.target.value)} required />
        <button type="submit" disabled={isBusy}>Change it</button>
      </form>
    </section>
  );
}
