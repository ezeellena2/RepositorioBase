import { useCallback, useState } from 'react';
import { useIdentity } from '../../identity/context/IdentityProvider';
import { ProblemMessage } from '../../identity/ProblemMessage';
import { useSubmit } from '../../identity/useSubmit';
import { usePlatformClient } from '../invitations/PlatformInvitationPages';
import { PlatformStepUpForm } from '../shared/PlatformStepUpForm';
import { usePlatformRead } from '../shared/usePlatformRead';
import { usePlatformStepUp } from '../shared/usePlatformStepUp';

/**
 * The shape the server accepts for both a reason code and a reference, mirrored so a value it would refuse never
 * becomes a request. It mirrors and never replaces: the server stays the authority, and a value this lets through
 * can still be refused there.
 */
const REFERENCE_FORMAT = /^[A-Za-z0-9._:-]{1,64}$/;
const REFERENCE_SHAPE = 'Letters, digits, dot, underscore, colon or hyphen — 1 to 64 characters.';

/**
 * Retention: what this deployment's policy says, and the legal holds that stop an erasure (IA-REQ-056, C7).
 *
 * There is no purge control here and there never will be. Erasure belongs to the maintenance worker, driven by
 * policy, with no endpoint and no permission behind it — so what an operator can do on this screen is read the
 * rules and stop a deletion, never order one.
 *
 * One problem code answers two different situations. The policy READ needs the second factor proved in this
 * session; a CHANGE needs it proved recently. Both are refused with `recent_mfa_required` and nothing on the
 * problem document tells them apart, so the screen decides from the context it already holds: a session that still
 * owes the factor is gated before it asks for anything, and a session that has proved it keeps everything it read
 * while it proves it again.
 */
export function PlatformRetentionPage() {
  const identity = useIdentity();
  const platform = usePlatformClient();

  const permissions = identity?.context?.permissions ?? [];
  const mayRead = permissions.includes('platform.retention.read');
  // Reading the rules and stopping an erasure are separately trusted, and manage deliberately does not imply read.
  const mayManage = permissions.includes('platform.retention.manage');
  const owesFactor = identity?.context?.session?.requiresTwoFactor === true;

  // Nothing is asked for while the factor is owed or the permission is missing: both produce only refusals the
  // visitor can do nothing about from here.
  const policy = usePlatformRead(
    useCallback(() => platform.readRetentionPolicy(), [platform]),
    mayRead && !owesFactor,
  );

  const [subjectIdentityId, setSubjectIdentityId] = useState('');
  const [reasonCode, setReasonCode] = useState('');
  const [reference, setReference] = useState('');
  const [holdId, setHoldId] = useState('');
  const [pendingRelease, setPendingRelease] = useState(null);
  const [receipt, setReceipt] = useState(null);
  const [releaseNotice, setReleaseNotice] = useState(false);
  const [shapeRefusal, setShapeRefusal] = useState(null);

  const { submit, problem: actionProblem, isBusy } = useSubmit(async (action) => action());

  const refreshPolicy = policy.refresh;
  // What a proof is worth here is a fresh read, and nothing else. It is routed through the same submission that
  // holds the refusal, so the refusal the gate is made of is cleared by the act of proving rather than by a second
  // flag somebody has to remember to reset — and so that this callback can close over `refresh` alone. The refused
  // change is deliberately not in reach of it: a change replayed by the proof that unblocked it is one nobody
  // asked for twice, and the operator repeats it themselves.
  const onProved = useCallback(() => submit(() => refreshPolicy(undefined)), [submit, refreshPolicy]);
  const stepUp = usePlatformStepUp(onProved);

  const run = async (action) => {
    setShapeRefusal(null);
    const outcome = await submit(action);
    if (outcome === undefined) {
      // Whatever was staged for confirmation is not the thing being answered any more. Tearing it down here is
      // what keeps a step-up gate from rendering behind a confirmation the operator never got an answer to.
      setPendingRelease(null);
      return undefined;
    }
    // The active hold count is part of what this screen states, so a change that moved it is read back.
    await refreshPolicy(undefined);
    return outcome;
  };

  const placeHold = async () => {
    setReleaseNotice(false);
    if (!REFERENCE_FORMAT.test(reasonCode) || !REFERENCE_FORMAT.test(reference)) {
      setShapeRefusal(`A reason code and a reference are each: ${REFERENCE_SHAPE} Nothing was sent.`);
      return;
    }
    const placed = await run(() => platform.placeRetentionHold(subjectIdentityId, reasonCode, reference));
    if (placed) setReceipt(placed);
  };

  const releaseHold = async () => {
    setReceipt(null);
    const released = await run(() => platform.releaseRetentionHold(pendingRelease));
    if (released !== undefined) {
      setPendingRelease(null);
      setReleaseNotice(true);
    }
  };

  if (!mayRead) {
    return (
      <section aria-labelledby="platform-retention-heading">
        <h1 id="platform-retention-heading">Retention</h1>
        <p>This screen needs the platform.retention.read permission. Ask a Platform owner to grant it.</p>
      </section>
    );
  }

  // The entry gate. The read is refused to a session that has not proved the factor, so the screen asks for the
  // proof instead of asking for a policy it would only be refused. It renders in place of the data, not beside it.
  if (owesFactor) {
    return (
      <section aria-labelledby="platform-retention-heading">
        <h1 id="platform-retention-heading">Retention</h1>
        <ProblemMessage problem={stepUp.problem} />
        <p>This session has not proved your second factor yet. Enter a code from your authenticator to read the retention policy.</p>
        <PlatformStepUpForm
          inputId="platform-retention-step-up"
          code={stepUp.code}
          onCodeChange={stepUp.onCodeChange}
          isBusy={stepUp.isBusy}
          onSubmit={stepUp.onSubmit}
        />
      </section>
    );
  }

  const page = policy.page;
  // Null members and no categories is a deployment with no policy at all, which is a loaded answer rather than an
  // empty one: an empty table would read as "no categories", and the truth is "nothing here will be erased".
  const hasNoPolicy = page !== null && page.policyId == null && (page.categories ?? []).length === 0;
  // The mutation gate. The session HAS proved the factor — that is what tells this apart from the entry gate — so
  // what is missing is a recent proof, and everything already read stays where it is.
  const changeNeedsRecentProof = actionProblem?.code === 'recent_mfa_required';

  return (
    <section aria-labelledby="platform-retention-heading">
      <h1 id="platform-retention-heading">Retention</h1>

      {/* One refusal at a time, most recent first: a rejected step-up is what just happened, a value this screen
          would not send is what happened before it, and the server's refusal of the change is the oldest of the
          three. Two alerts saying different things is how somebody answers the wrong one. */}
      {shapeRefusal
        ? <p role="alert">{shapeRefusal}</p>
        : <ProblemMessage problem={stepUp.problem ?? actionProblem} />}

      {changeNeedsRecentProof && (
        <>
          <p>That change needs a recent proof of your second factor. Confirm it, then make the change again.</p>
          <PlatformStepUpForm
            inputId="platform-retention-step-up"
            code={stepUp.code}
            onCodeChange={stepUp.onCodeChange}
            isBusy={stepUp.isBusy}
            onSubmit={stepUp.onSubmit}
          />
        </>
      )}

      {policy.status === 'loading' && page === null && <p role="status">Reading the retention policy.</p>}

      {page !== null && (
        <>
          <dl>
            <dt>Personal data</dt>
            <dd data-testid="retention-personal-data-mode">{page.personalDataMode}</dd>
            <dt>Active holds</dt>
            <dd data-testid="retention-active-holds">{page.activeHoldCount}</dd>
          </dl>

          {hasNoPolicy ? (
            <p>No retention policy is configured for this deployment, so nothing will be erased.</p>
          ) : (
            <table>
              <thead>
                <tr>
                  <th scope="col">Category</th>
                  <th scope="col">Retention period</th>
                  <th scope="col">Trigger</th>
                  <th scope="col">Action</th>
                  <th scope="col">Evidence required</th>
                </tr>
              </thead>
              <tbody>
                {page.categories.map((rule) => (
                  <tr key={rule.category}>
                    <td>{rule.category}</td>
                    <td>{rule.retentionPeriod}</td>
                    <td>{rule.trigger}</td>
                    <td>{rule.action}</td>
                    <td>{rule.evidenceRequired ? 'Yes' : 'No'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}

      {policy.status === 'refused' && <ProblemMessage problem={policy.problem} />}

      {/* A refusal the server put into words is answered by reading it, not by asking again. Something that went
          wrong without any such words is the other screen, and that one is worth retrying. */}
      {policy.status === 'errored' && (
        <>
          <ProblemMessage problem={policy.problem} />
          <button type="button" onClick={() => policy.refresh(undefined)}>Try again</button>
        </>
      )}

      {mayManage && (
        <>
          <h2>Place a hold</h2>
          <form aria-label="Place a hold" onSubmit={(event) => { event.preventDefault(); placeHold(); }}>
            <label htmlFor="retention-subject">Subject identity</label>
            <input id="retention-subject" type="text" value={subjectIdentityId} onChange={(event) => setSubjectIdentityId(event.target.value)} required />

            <label htmlFor="retention-reason-code">Reason code</label>
            <input
              id="retention-reason-code"
              type="text"
              aria-describedby="retention-reference-shape"
              value={reasonCode}
              onChange={(event) => setReasonCode(event.target.value)}
              required
            />

            <label htmlFor="retention-reference">Reference</label>
            <input
              id="retention-reference"
              type="text"
              aria-describedby="retention-reference-shape"
              value={reference}
              onChange={(event) => setReference(event.target.value)}
              required
            />

            <p id="retention-reference-shape">{REFERENCE_SHAPE}</p>
            <button type="submit" disabled={isBusy}>Place hold</button>
          </form>

          {/* The only time a hold id is ever shown. No route lists holds, so an operator who does not keep this
              has no way to name the hold again. */}
          {receipt && (
            <p role="status">
              {`Hold ${receipt.holdId} is placed for ${receipt.reasonCode} under reference ${receipt.reference}, at ${receipt.placedAt}. Keep that hold id: nothing lists holds, so this is the only time it is shown.`}
            </p>
          )}

          <h2>Release a hold</h2>
          <form aria-label="Release a hold" onSubmit={(event) => { event.preventDefault(); setPendingRelease(holdId); }}>
            <label htmlFor="retention-hold-id">Hold id</label>
            <input id="retention-hold-id" type="text" value={holdId} onChange={(event) => setHoldId(event.target.value)} required />
            <button type="submit" disabled={isBusy}>Release</button>
          </form>

          {/* Confirmed rather than done on one click: releasing a hold is what lets the maintenance worker erase
              the rows it was protecting, and clicking again does not put them back. */}
          {pendingRelease && (
            <form aria-label="Confirm release" onSubmit={(event) => { event.preventDefault(); releaseHold(); }}>
              <p>Release this hold? Nothing about it is read back first — the route answers the same way whether it stands, was already released, or never existed.</p>
              <button type="submit" disabled={isBusy}>Confirm release</button>
              <button type="button" onClick={() => setPendingRelease(null)}>Cancel</button>
            </form>
          )}

          {/* A statement about the resulting state, because that is the only thing the 204 said. Claiming this
              request released it, or that the hold existed, would be the screen answering a question the route
              deliberately does not answer. */}
          {releaseNotice && (
            <p role="status">
              This hold is released. A hold that was already released and one that never existed answer exactly the
              same way, so this says what is true now — not that this request changed anything.
            </p>
          )}
        </>
      )}
    </section>
  );
}
