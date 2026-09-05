import { useCallback, useEffect, useState } from 'react';
import { useIdentity } from '../identity/context/IdentityProvider';
import { ProblemMessage } from '../identity/ProblemMessage';
import { useSubmit } from '../identity/useSubmit';
import { usePlatformClient } from './invitations/PlatformInvitationPages';

/**
 * The Platform panel (IA-REQ-045).
 *
 * It renders only for a session whose active tenant is Platform and which holds the matching read permission. The
 * check is for the user's benefit rather than a control — the API reauthorizes every call — but rendering a panel
 * a caller cannot use would produce a screen of refusals instead of an answer.
 *
 * Everything it shows is an allowlisted projection. There is no impersonation control, no delete action, and no
 * way to choose a tenant to act as: the acting tenant comes from the session, which is why the panel never sends
 * one (IA-REQ-046).
 */
const REASONS = ['PolicyViolation', 'SecurityIncident', 'BillingHold', 'OperatorRequest'];

function useDirectory(load) {
  const [page, setPage] = useState(null);
  const [problem, setProblem] = useState(null);

  const refresh = useCallback(async (cursor) => {
    try {
      setPage(await load({ cursor }));
      setProblem(null);
    } catch (failure) {
      // A directory that cannot be read shows why rather than an empty table, which would read as "there is
      // nothing here" — a very different statement from "you were refused".
      setProblem(failure?.problem ?? { code: 'internal_server_error', status: 0 });
      setPage(null);
    }
  }, [load]);

  // The first page is loaded inside an async body rather than from the effect directly, so nothing is set
  // synchronously while the component is still rendering.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!cancelled) await refresh(undefined);
    })();
    return () => { cancelled = true; };
  }, [refresh]);

  return { page, problem, refresh };
}

export function PlatformPanel() {
  const identity = useIdentity();
  const platform = usePlatformClient();
  const [stepUpCode, setStepUpCode] = useState('');
  const [reason, setReason] = useState(REASONS[0]);
  const [inviteEmail, setInviteEmail] = useState('');
  const [pendingAction, setPendingAction] = useState(null);
  const { submit, problem: actionProblem, isBusy } = useSubmit(async (action) => action());

  const organizations = useDirectory(useCallback((options) => platform.listOrganizations(options), [platform]));
  const administrators = useDirectory(useCallback((options) => platform.listAdministrators(options), [platform]));
  const audit = useDirectory(useCallback((options) => platform.listAudit(options), [platform]));

  const permissions = identity?.context?.permissions ?? [];
  const isPlatform = identity?.context?.activeTenant?.type === 'Platform';

  if (!identity?.isAuthenticated || !isPlatform || !permissions.includes('platform.organizations.read')) {
    return (
      <section aria-labelledby="platform-panel-heading">
        <h1 id="platform-panel-heading">Platform</h1>
        <p>This area is for an MFA-authenticated Platform administrator.</p>
      </section>
    );
  }

  const run = async (action) => {
    const outcome = await submit(action);
    if (outcome !== undefined) {
      setPendingAction(null);
      await Promise.all([organizations.refresh(undefined), administrators.refresh(undefined), audit.refresh(undefined)]);
    }
  };

  return (
    <section aria-labelledby="platform-panel-heading">
      <h1 id="platform-panel-heading">Platform</h1>
      <ProblemMessage problem={actionProblem} />

      {/* A Platform change needs a recent proof of the second factor, so the panel offers one rather than
          letting the administrator discover the refusal after composing an action. */}
      <form
        aria-label="Step up"
        onSubmit={(event) => { event.preventDefault(); run(() => platform.stepUp(stepUpCode)); }}
      >
        <label htmlFor="platform-step-up">Authenticator code</label>
        <input id="platform-step-up" type="text" inputMode="numeric" value={stepUpCode} onChange={(event) => setStepUpCode(event.target.value)} required />
        <button type="submit" disabled={isBusy}>Step up</button>
      </form>

      <h2>Organizations</h2>
      <ProblemMessage problem={organizations.problem} />
      <table>
        <thead>
          <tr><th scope="col">Slug</th><th scope="col">Status</th><th scope="col">Suspended for</th><th scope="col">Actions</th></tr>
        </thead>
        <tbody>
          {(organizations.page?.items ?? []).map((organization) => (
            <tr key={organization.tenantId}>
              <td>{organization.slug}</td>
              <td>{organization.status}</td>
              <td>{organization.suspensionReason ?? '—'}</td>
              <td>
                {organization.status === 'Suspended' ? (
                  <button type="button" disabled={isBusy} onClick={() => run(() => platform.reactivateOrganization(organization.tenantId))}>
                    {`Reactivate ${organization.slug}`}
                  </button>
                ) : (
                  <button type="button" disabled={isBusy} onClick={() => setPendingAction({ kind: 'suspend', organization })}>
                    {`Suspend ${organization.slug}`}
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {organizations.page?.nextCursor && (
        <button type="button" onClick={() => organizations.refresh(organizations.page.nextCursor)}>More organizations</button>
      )}

      {/* Suspension and revocation are confirmed rather than done on a single click: both are visible to everyone
          inside the affected tenant, and neither is undone by simply clicking again. */}
      {pendingAction?.kind === 'suspend' && (
        <form
          aria-label="Confirm suspension"
          onSubmit={(event) => { event.preventDefault(); run(() => platform.suspendOrganization(pendingAction.organization.tenantId, reason)); }}
        >
          <p>{`Suspend ${pendingAction.organization.slug}?`}</p>
          <label htmlFor="platform-suspension-reason">Reason</label>
          <select id="platform-suspension-reason" value={reason} onChange={(event) => setReason(event.target.value)}>
            {REASONS.map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
          <button type="submit" disabled={isBusy}>Confirm suspension</button>
          <button type="button" onClick={() => setPendingAction(null)}>Cancel</button>
        </form>
      )}

      <h2>Administrators</h2>
      <ProblemMessage problem={administrators.problem} />
      <table>
        <thead>
          <tr><th scope="col">Address</th><th scope="col">Status</th><th scope="col">Second factor</th><th scope="col">Actions</th></tr>
        </thead>
        <tbody>
          {(administrators.page?.items ?? []).map((administrator) => (
            <tr key={administrator.membershipId}>
              <td>{administrator.normalizedEmail}</td>
              <td>{administrator.isOwner ? `${administrator.membershipStatus} (owner)` : administrator.membershipStatus}</td>
              <td>{administrator.mfaStatus}</td>
              <td>
                <button type="button" disabled={isBusy} onClick={() => setPendingAction({ kind: 'revoke', administrator })}>
                  {`Revoke ${administrator.normalizedEmail}`}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {pendingAction?.kind === 'revoke' && (
        <form
          aria-label="Confirm revocation"
          onSubmit={(event) => { event.preventDefault(); run(() => platform.revokeAdministrator(pendingAction.administrator.membershipId)); }}
        >
          <p>{`Revoke ${pendingAction.administrator.normalizedEmail}?`}</p>
          <button type="submit" disabled={isBusy}>Confirm revocation</button>
          <button type="button" onClick={() => setPendingAction(null)}>Cancel</button>
        </form>
      )}

      {permissions.includes('platform.admins.manage') && (
        <form aria-label="Invite an administrator" onSubmit={(event) => { event.preventDefault(); run(() => platform.inviteAdministrator(inviteEmail)); }}>
          <label htmlFor="platform-invite-email">Invite an administrator</label>
          <input id="platform-invite-email" type="email" value={inviteEmail} onChange={(event) => setInviteEmail(event.target.value)} required />
          <button type="submit" disabled={isBusy}>Invite</button>
        </form>
      )}

      <h2>Audit</h2>
      <ProblemMessage problem={audit.problem} />
      <ul aria-label="Audit">
        {(audit.page?.items ?? []).map((event) => (
          <li key={event.eventId}>{`${event.eventType} — ${event.outcome ?? 'recorded'}`}</li>
        ))}
      </ul>
    </section>
  );
}
