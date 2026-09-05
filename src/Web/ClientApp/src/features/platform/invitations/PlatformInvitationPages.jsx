import { useEffect, useMemo, useState } from 'react';
import { useIdentity } from '../../identity/context/IdentityProvider';
import { ProblemMessage } from '../../identity/ProblemMessage';
import { useSubmit } from '../../identity/useSubmit';
import { createPlatformClient } from '../api/platformClient';

/**
 * The Platform invitation token arrives in the URL fragment, which browsers never send to a server and proxies
 * never log. It is read once into memory and the fragment is erased with replaceState, so it does not survive in
 * history, in a bookmark, or in whatever the next page decides to log (IA-REQ-025/029).
 */
function usePlatformInvitationToken() {
  const [token] = useState(() => new URLSearchParams(window.location.hash.replace(/^#/, '')).get('token'));

  useEffect(() => {
    if (window.location.hash) {
      window.history.replaceState({}, '', `${window.location.pathname}${window.location.search}`);
    }
  }, []);

  return token;
}

export function usePlatformClient() {
  const identity = useIdentity();
  return useMemo(() => createPlatformClient(identity.client.transport), [identity]);
}

/**
 * Registering from a Platform invitation answers with a neutral bodyless 202 whether the token was live, dead, or
 * already belonged to an account, so this page says the same thing in every case. The password is used only when
 * the matching identity is missing; for one that already exists it is ignored and cannot take over the account.
 */
export function RegisterPlatformInviteePage() {
  const platform = usePlatformClient();
  const token = usePlatformInvitationToken();
  const [password, setPassword] = useState('');
  const { submit, problem, isBusy, result } = useSubmit((secret, chosen) => platform.registerFromInvitation(secret, chosen));

  if (result) {
    return (
      <section aria-labelledby="platform-register-heading">
        <h1 id="platform-register-heading">Set up your Platform account</h1>
        <p role="status">Check your email. If that invitation is still open, we have sent you what you need to continue.</p>
      </section>
    );
  }

  return (
    <section aria-labelledby="platform-register-heading">
      <h1 id="platform-register-heading">Set up your Platform account</h1>
      <ProblemMessage problem={problem} />
      <form onSubmit={(event) => { event.preventDefault(); submit(token ?? '', password); }}>
        <label htmlFor="platform-password">Choose a password</label>
        <input
          id="platform-password"
          type="password"
          autoComplete="new-password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          required
        />
        <button type="submit" disabled={isBusy}>Continue</button>
      </form>
    </section>
  );
}

/**
 * Confirmation consumes the token the confirmation mail carried. The token arrives in the fragment, exactly as
 * the invitation token does and for the same reason, and is erased from the address bar on arrival.
 *
 * It is one button rather than a field to paste into: the recipient followed a link from their own mailbox, and
 * asking them to transcribe a code out of it would be friction that proves nothing extra. The offer being
 * confirmed comes from the envelope the server sealed, not from anything typed here.
 */
export function ConfirmPlatformInviteePage() {
  const platform = usePlatformClient();
  const confirmationToken = usePlatformInvitationToken();
  const { submit, problem, isBusy, result } = useSubmit((secret) => platform.confirmInvitation(secret));

  return (
    <section aria-labelledby="platform-confirm-heading">
      <h1 id="platform-confirm-heading">Confirm your Platform address</h1>
      <ProblemMessage problem={problem} />
      {result ? (
        <p role="status">Your address is confirmed. Sign in to continue.</p>
      ) : (
        <button type="button" disabled={isBusy} onClick={() => submit(confirmationToken ?? '')}>
          Confirm my address
        </button>
      )}
    </section>
  );
}

/**
 * The invitation-bound MFA ceremony: enrol, verify, acknowledge. The key and the recovery codes are shown once
 * and never again — the secret is stored encrypted and the codes only as hashes, so nothing can serve them a
 * second time. Only after acknowledgement does the Platform membership become active (IA-REQ-041).
 */
export function PlatformMfaEnrollmentPage() {
  const identity = useIdentity();
  const platform = usePlatformClient();
  const token = usePlatformInvitationToken();
  const [enrollment, setEnrollment] = useState(null);
  const [code, setCode] = useState('');
  const [stage, setStage] = useState('start');
  const { submit, problem, isBusy } = useSubmit(async (action) => action());

  if (!identity?.isAuthenticated) {
    return (
      <section aria-labelledby="platform-mfa-heading">
        <h1 id="platform-mfa-heading">Set up your second factor</h1>
        <p>Sign in with the invited address first.</p>
      </section>
    );
  }

  return (
    <section aria-labelledby="platform-mfa-heading">
      <h1 id="platform-mfa-heading">Set up your second factor</h1>
      <ProblemMessage problem={problem} />

      {stage === 'start' && (
        <button
          type="button"
          disabled={isBusy}
          onClick={async () => {
            const details = await submit(() => platform.beginMfaEnrollment(token ?? ''));
            if (details) {
              setEnrollment(details);
              setStage('verify');
            }
          }}
        >
          Begin enrollment
        </button>
      )}

      {stage !== 'start' && enrollment && (
        <>
          <dl>
            <dt>Shared key</dt>
            <dd data-testid="platform-shared-key">{enrollment.sharedKey}</dd>
          </dl>
          <ul aria-label="Recovery codes">
            {enrollment.recoveryCodes.map((recoveryCode) => <li key={recoveryCode}>{recoveryCode}</li>)}
          </ul>
        </>
      )}

      {stage === 'verify' && (
        <form
          onSubmit={async (event) => {
            event.preventDefault();
            const verified = await submit(() => platform.verifyMfaEnrollment(token ?? '', code));
            if (verified !== undefined) setStage('acknowledge');
          }}
        >
          <label htmlFor="platform-mfa-code">Code from your authenticator</label>
          <input id="platform-mfa-code" type="text" inputMode="numeric" value={code} onChange={(event) => setCode(event.target.value)} required />
          <button type="submit" disabled={isBusy}>Verify</button>
        </form>
      )}

      {stage === 'acknowledge' && (
        <button
          type="button"
          disabled={isBusy}
          onClick={async () => {
            const acknowledged = await submit(() => platform.acknowledgeRecoveryCodes(token ?? ''));
            if (acknowledged !== undefined) {
              setStage('done');
              // The membership only exists as of this moment, so the session that completed the ceremony still
              // has no active tenant. Selecting it here is what makes the ceremony end somewhere rather than
              // leaving the new administrator to work out that they must go and choose one.
              const reloaded = await identity.reload();
              const platformTenant = reloaded?.availableTenants?.find((tenant) => tenant.type === 'Platform');
              if (platformTenant) await identity.selectTenant(platformTenant.id);
            }
          }}
        >
          I have saved my recovery codes
        </button>
      )}

      {stage === 'done' && <p role="status">Your second factor is active.</p>}
    </section>
  );
}

/**
 * Bootstrap recovery, as a page anyone can reach before an owner exists. It sends nothing at all: no address, no
 * identity, no replacement recipient. Every valid state answers the same way, so the page says the same thing.
 */
export function RecoverPlatformBootstrapPage() {
  const platform = usePlatformClient();
  const { submit, problem, isBusy, result } = useSubmit(() => platform.recoverBootstrapInvitation());

  return (
    <section aria-labelledby="platform-recover-heading">
      <h1 id="platform-recover-heading">Resend the Platform owner invitation</h1>
      <ProblemMessage problem={problem} />
      {result ? (
        <p role="status">If an owner invitation is waiting and could not be delivered, a new one is on its way.</p>
      ) : (
        <button type="button" disabled={isBusy} onClick={() => submit()}>Resend</button>
      )}
    </section>
  );
}
