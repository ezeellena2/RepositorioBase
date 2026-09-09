import { useCallback, useState } from 'react';
import { useIdentity } from '../../identity/context/IdentityProvider';
import { ProblemMessage } from '../../identity/ProblemMessage';
import { useSubmit } from '../../identity/useSubmit';
import { usePlatformClient } from '../invitations/PlatformInvitationPages';
import { PlatformStepUpForm } from '../shared/PlatformStepUpForm';
import { usePlatformRead } from '../shared/usePlatformRead';
import { usePlatformStepUp } from '../shared/usePlatformStepUp';

/**
 * The closed set the API accepts for an account suspension. It is declared here rather than shared with the
 * panel's tenant reasons: the two sets happen to hold the same four names today, and one changing is not the
 * other changing. A shared constant would make that coincidence into a coupling.
 */
const SUSPENSION_REASONS = ['PolicyViolation', 'SecurityIncident', 'BillingHold', 'OperatorRequest'];

/**
 * Which transition the server would accept from a given state, and therefore the only one worth offering. A
 * reactivation is accepted from `AdministrativelySuspended` and nowhere else; a suspension is accepted from
 * everywhere except that state and `Closed`, which is terminal. Rendering the other combinations would be
 * rendering a button whose only possible answer is a refusal.
 */
const transitionFor = (accountStatus) => {
  if (accountStatus === 'AdministrativelySuspended') return 'reactivate';
  if (accountStatus === 'Closed') return null;
  return 'suspend';
};

/**
 * The same question asked of the caller rather than of the row. Holding `platform.identities.read` without
 * `platform.identities.manage` is a real combination, and offering it a button whose only possible answer is
 * `permission_denied` is the same lie as offering a transition the state does not accept.
 */
const offeredTransition = (accountStatus, mayManage) => (mayManage ? transitionFor(accountStatus) : null);

/**
 * The operator directory of accounts, and the two lifecycle changes it offers (IA-REQ-054).
 *
 * Two different gates answer with `401 recent_mfa_required` and there is nothing on the problem document that
 * tells them apart. Reading the directory needs a session that proved its second factor at all; changing one
 * account needs a *recent* proof. The discriminator is `requiresTwoFactor` on the identity context: while it is
 * true this screen asks for nothing and renders the ceremony instead of the data, and once it is false a refusal
 * of that code can only have come from the recency rule — with the rows already on screen and worth keeping.
 *
 * What the gate must never do is finish the change it interrupted. `onProved` reloads the directory and closes
 * over no action, and the pending confirmation is taken down before the gate goes up, so after a step-up there is
 * nothing armed to fire: asking again is the operator's to do, deliberately.
 */
export function PlatformIdentitiesPage() {
  const identity = useIdentity();
  const platform = usePlatformClient();
  const [pending, setPending] = useState(null);
  const [reason, setReason] = useState(SUSPENSION_REASONS[0]);
  const [acknowledged, setAcknowledged] = useState(false);
  const [proofRefusal, setProofRefusal] = useState(null);

  const context = identity?.context;
  const permissions = context?.permissions ?? [];
  // For the operator's benefit rather than as a control — the API reauthorizes every call — but a screen that
  // renders a directory its caller may not read produces a refusal where an answer was expected.
  const mayRead = Boolean(identity?.isAuthenticated)
    && context?.activeTenant?.type === 'Platform'
    && permissions.includes('platform.identities.read');
  const mayManage = mayRead && permissions.includes('platform.identities.manage');
  const owesFactor = context?.session?.requiresTwoFactor === true;
  const mayLoad = mayRead && !owesFactor;

  const { page, problem: readProblem, refresh, status } = usePlatformRead(
    useCallback((options) => platform.listIdentities(options), [platform]),
    mayLoad,
  );
  const { submit, problem: actionProblem, isBusy } = useSubmit(async (action) => action());

  // Reload the rows, and nothing else. It holds no reference to whatever was refused, which is what makes "the
  // gate never replays the change" structural instead of a rule somebody has to keep remembering. The entry gate
  // needs no refresh from here: its read starts itself the moment the factor stops being owed, and asking again
  // would be a second request for the same page.
  const onProved = useCallback(async () => {
    setProofRefusal(null);
    if (mayLoad) await refresh(undefined);
  }, [mayLoad, refresh]);
  const stepUp = usePlatformStepUp(onProved);

  const run = (action) => submit(async () => {
    setProofRefusal(null);
    try {
      const outcome = await action();
      setPending(null);
      await refresh(undefined);
      return outcome;
    } catch (failure) {
      const code = failure?.problem?.code;
      // The confirmation comes down before the gate goes up: what it held was composed against a precondition the
      // server has now declined to act on, and leaving it armed is how a step-up ends in a change nobody re-asked
      // for. Every other refusal leaves it standing, because the operator can still act on it — ticking the
      // acknowledgement and confirming again is the whole answer to one of them.
      if (code === 'recent_mfa_required') {
        setPending(null);
        setProofRefusal(failure.problem);
      } else if (code === 'identity_concurrency_conflict') {
        setPending(null);
        await refresh(undefined);
      }
      throw failure;
    }
  });

  // `expectedStatus` is read once, here, off the row the operator is looking at. Re-deriving it when the form is
  // submitted would make the precondition agree with whatever arrived in between — which is exactly the
  // disagreement it exists to catch (IA-REQ-054).
  const arm = (kind, row) => {
    setReason(SUSPENSION_REASONS[0]);
    setAcknowledged(false);
    setPending({ kind, identityId: row.identityId, subject: row.normalizedEmail, expectedStatus: row.accountStatus });
  };

  if (!mayRead) {
    return (
      <section aria-labelledby="platform-identities-heading">
        <h1 id="platform-identities-heading">Identities</h1>
        <p>This screen is for a Platform administrator holding platform.identities.read.</p>
      </section>
    );
  }

  if (owesFactor) {
    return (
      <section aria-labelledby="platform-identities-heading">
        <h1 id="platform-identities-heading">Identities</h1>
        <ProblemMessage problem={stepUp.problem} />
        <p>This session has not proved your second factor yet. Enter a code from your authenticator to continue.</p>
        <PlatformStepUpForm
          inputId="platform-identities-step-up"
          code={stepUp.code}
          onCodeChange={stepUp.onCodeChange}
          onSubmit={stepUp.onSubmit}
          isBusy={stepUp.isBusy}
        />
      </section>
    );
  }

  const rows = page?.items ?? [];
  // `useSubmit` clears its problem when the next attempt starts, which is the wrong lifetime for this one: the
  // gate has to outlive the attempt and come down only once the factor is settled. So the screen holds that
  // refusal itself, and `useSubmit`'s copy of it is never the one rendered.
  const refusal = proofRefusal ?? (actionProblem?.code === 'recent_mfa_required' ? null : actionProblem);

  return (
    <section aria-labelledby="platform-identities-heading">
      <h1 id="platform-identities-heading">Identities</h1>
      <ProblemMessage problem={refusal} />

      {proofRefusal && (
        <>
          <ProblemMessage problem={stepUp.problem} />
          <p>That change needs a fresh proof of your second factor. Enter a code, then ask for it again.</p>
          <PlatformStepUpForm
            inputId="platform-identities-step-up"
            code={stepUp.code}
            onCodeChange={stepUp.onCodeChange}
            onSubmit={stepUp.onSubmit}
            isBusy={stepUp.isBusy}
          />
        </>
      )}

      {status === 'loading' && <p role="status">Loading the directory…</p>}
      {/* A refused read and a failed one are different statements, and neither is "there is nothing here". Only
          the second is worth offering a retry for: the first will answer the same way however often it is asked. */}
      {(status === 'refused' || status === 'errored') && <ProblemMessage problem={readProblem} />}
      {status === 'errored' && (
        <button type="button" onClick={() => refresh(undefined)}>Try again</button>
      )}
      {status === 'loaded' && rows.length === 0 && <p>No accounts are listed here.</p>}

      {rows.length > 0 && (
        <table>
          <thead>
            <tr>
              <th scope="col">Address</th>
              <th scope="col">Account status</th>
              <th scope="col">Identity</th>
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.identityId}>
                <td>{row.normalizedEmail}</td>
                <td>{row.accountStatus}</td>
                {/* Rendered so an operator can copy it: it is what every other record of this account is keyed
                    by, and an address is not a stable way to name one. */}
                <td>{row.identityId}</td>
                <td>
                  {offeredTransition(row.accountStatus, mayManage) === 'suspend' && (
                    <button type="button" disabled={isBusy} onClick={() => arm('suspend', row)}>
                      {`Suspend ${row.normalizedEmail}`}
                    </button>
                  )}
                  {offeredTransition(row.accountStatus, mayManage) === 'reactivate' && (
                    <button type="button" disabled={isBusy} onClick={() => arm('reactivate', row)}>
                      {`Reactivate ${row.normalizedEmail}`}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {/* The server answers one bounded page and names where the next one starts. Without this the directory is
          whatever the first page happened to contain, and an account past it cannot be reached at all — the
          screen offers no search either, so the cursor is the only way through. */}
      {page?.nextCursor && (
        <button type="button" disabled={isBusy} onClick={() => refresh(page.nextCursor)}>
          More accounts
        </button>
      )}

      {/* Both changes are confirmed rather than done on a click: each ends every session the account holds, and
          neither is undone by clicking the other one. */}
      {pending?.kind === 'suspend' && (
        <form
          aria-label="Confirm suspension"
          onSubmit={(event) => {
            event.preventDefault();
            run(() => platform.suspendIdentity(pending.identityId, reason, pending.expectedStatus));
          }}
        >
          <p>{`Suspend ${pending.subject}?`}</p>
          <label htmlFor="platform-identity-suspension-reason">Reason</label>
          <select
            id="platform-identity-suspension-reason"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          >
            {SUSPENSION_REASONS.map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
          <button type="submit" disabled={isBusy}>Confirm suspension</button>
          <button type="button" onClick={() => setPending(null)}>Cancel</button>
        </form>
      )}

      {pending?.kind === 'reactivate' && (
        <form
          aria-label="Confirm reactivation"
          onSubmit={(event) => {
            event.preventDefault();
            run(() => platform.reactivateIdentity(pending.identityId, pending.expectedStatus, acknowledged));
          }}
        >
          <p>{`Lift the suspension on ${pending.subject}?`}</p>
          {/* `invalid_platform_operation` means several different things on this screen, so the shared catalogue
              cannot say which. On a reactivation refused with the box unticked it means exactly one of them, and
              that is a thing the operator can act on — so the sentence is keyed on the code and the action, and
              lives here rather than in the catalogue. */}
          {actionProblem?.code === 'invalid_platform_operation' && (
            <p>
              This account was parked by the person who owns it. Lifting the suspension returns it there, not to
              active. Tick the acknowledgement if you mean to do that.
            </p>
          )}
          {/* Unticked to begin with and set by nothing but this box. It is the operator saying they know where
              the account will land, and no code path may say it on their behalf. */}
          <label htmlFor="platform-identity-acknowledge">
            <input
              id="platform-identity-acknowledge"
              type="checkbox"
              checked={acknowledged}
              onChange={(event) => setAcknowledged(event.target.checked)}
            />
            I understand this account may return to deactivated rather than active
          </label>
          <button type="submit" disabled={isBusy}>Confirm reactivation</button>
          <button type="button" onClick={() => setPending(null)}>Cancel</button>
        </form>
      )}
    </section>
  );
}
