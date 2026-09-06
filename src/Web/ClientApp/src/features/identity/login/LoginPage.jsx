import { useState } from 'react';
import { Link, Navigate, useLocation, useSearchParams } from 'react-router-dom';
import { useIdentity } from '../context/IdentityProvider';
import { ProblemMessage } from '../ProblemMessage';
import { useSubmit } from '../useSubmit';

/**
 * Where a visitor lands after signing in is taken from the query string, so it is treated as untrusted input: a
 * value that is not a same-origin path is discarded rather than followed, which is what stops a crafted link
 * from bouncing someone off-origin the moment they hold a session.
 */
export function safeReturnUrl(candidate) {
  if (typeof candidate !== 'string' || !candidate.startsWith('/') || candidate.startsWith('//')) return '/';
  return candidate;
}

export function LoginPage() {
  const identity = useIdentity();
  const location = useLocation();
  const [params] = useSearchParams();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const returnUrl = safeReturnUrl(params.get('returnUrl'));
  const { submit, problem, isBusy } = useSubmit((...args) => identity.signIn(...args));

  if (identity?.isAuthenticated) return <Navigate to={returnUrl} replace state={{ from: location }} />;

  return (
    <section aria-labelledby="login-heading">
      <h1 id="login-heading">Sign in</h1>
      <p data-testid="return-url" hidden>{returnUrl}</p>
      <p data-testid="context-problem" hidden>{identity?.contextProblem?.code ?? ''}</p>
      <ProblemMessage problem={problem ?? identity?.contextProblem} />
      <form onSubmit={(event) => { event.preventDefault(); submit(email, password); }}>
        <label htmlFor="login-email">Email</label>
        <input id="login-email" type="email" autoComplete="username" value={email} onChange={(event) => setEmail(event.target.value)} required />
        <label htmlFor="login-password">Password</label>
        <input id="login-password" type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} required />
        <button type="submit" disabled={isBusy}>Sign in</button>
      </form>
      <p><Link to="/credentials/forgot">Forgot your password?</Link></p>
    </section>
  );
}
