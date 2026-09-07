import { useState } from 'react';
import { useIdentity } from '../../identity/context/IdentityProvider';
import { usePlatformClient } from './PlatformInvitationPages';
import { ProblemMessage } from '../../identity/ProblemMessage';
import { useSubmit } from '../../identity/useSubmit';

/**
 * Getting a second factor back after losing the authenticator that held it (IA-REQ-041, C6).
 *
 * The page takes two things and stores neither: the password, re-typed to buy a proof for this action alone, and
 * one recovery code. Both leave the browser once. What comes back — a shared key and a new set of codes — is
 * shown once and is never readable again, here or anywhere else, so the page says so rather than letting somebody
 * assume they can come back for it (IA-REQ-025).
 *
 * There is no token in the URL, unlike the enrollment ceremony: the invitation was consumed at activation, and
 * what authorizes this is the caller's own session plus the code.
 */
export function MfaRecoveryPage() {
  const identity = useIdentity();
  const platform = usePlatformClient();
  const [password, setPassword] = useState('');
  const [recoveryCode, setRecoveryCode] = useState('');
  const [replacement, setReplacement] = useState(null);
  const { submit, problem, isBusy } = useSubmit(async (action) => action());

  if (!identity?.isAuthenticated) {
    return (
      <section aria-labelledby="mfa-recovery-heading">
        <h1 id="mfa-recovery-heading">Replace your second factor</h1>
        <p>Sign in first. Replacing a second factor needs your password as well as a recovery code.</p>
      </section>
    );
  }

  if (replacement) {
    return (
      <section aria-labelledby="mfa-recovery-heading">
        <h1 id="mfa-recovery-heading">Replace your second factor</h1>
        <p role="status">
          Add this key to your authenticator and save the codes. They are shown once and cannot be shown again.
        </p>
        <dl>
          <dt>Shared key</dt>
          <dd data-testid="recovered-shared-key">{replacement.sharedKey}</dd>
        </dl>
        <ul aria-label="Recovery codes">
          {replacement.recoveryCodes.map((code) => <li key={code}>{code}</li>)}
        </ul>
        <p>Prove the new factor before making any Platform change: nobody has proved it yet, including you.</p>
      </section>
    );
  }

  return (
    <section aria-labelledby="mfa-recovery-heading">
      <h1 id="mfa-recovery-heading">Replace your second factor</h1>
      <p>Use this if you have lost the authenticator. You need your password and one unused recovery code.</p>
      <ProblemMessage problem={problem} />
      <form
        onSubmit={async (event) => {
          event.preventDefault();

          // The proof first, for this action alone: a proof bought to change a password does not pay for
          // replacing a second factor. Nothing is kept between the two calls but what the person typed.
          const proved = await submit(async () => {
            await identity.client.reauthenticate('platform.mfa.recover', password);
            return platform.recoverMfa(recoveryCode);
          });

          setPassword('');
          setRecoveryCode('');
          if (proved) setReplacement(proved);
        }}
      >
        <label htmlFor="mfa-recovery-password">Your password</label>
        <input
          id="mfa-recovery-password"
          type="password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          required
        />
        <label htmlFor="mfa-recovery-code">A recovery code</label>
        <input
          id="mfa-recovery-code"
          type="text"
          value={recoveryCode}
          onChange={(event) => setRecoveryCode(event.target.value)}
          required
        />
        <button type="submit" disabled={isBusy}>Replace my second factor</button>
      </form>
    </section>
  );
}
