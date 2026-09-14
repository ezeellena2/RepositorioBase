import { sendPage } from '../../../api/pagination';

/**
 * The Platform half of the API surface (IA-REQ-045).
 *
 * It shares the identity client's transport rather than opening its own, so there is one session cookie, one
 * antiforgery request token, and one place that knows how to read a problem document. A second token holder would
 * be stale from the moment the first one signed in.
 *
 * Every directory is one offset page, read through the shared page reader, which declares `items` and the page
 * metadata as the expected members. `items` is on the identity contract's forbidden list precisely because identity
 * endpoints must not grow a pagination envelope; declaring it here is what makes a Platform directory a distinct
 * resource rather than that envelope leaking sideways. The page asked for is bounded before it is sent, and the
 * server clamps it again (D21).
 */

export function createPlatformClient(transport) {
  if (!transport) throw new Error('The Platform client shares the identity transport.');
  const { send } = transport;

  return {
    // Public onboarding. A recipient answering a Platform invitation has no session, and may have no account.
    registerFromInvitation: (token, password) =>
      send('/api/platform/invitations/register', { method: 'POST', body: { token, password } }),

    confirmInvitation: (confirmationToken) =>
      send('/api/platform/invitations/confirm', { method: 'POST', body: { confirmationToken } }),

    // Bodyless by contract: it accepts no email, identity or replacement recipient (IA-REQ-040).
    recoverBootstrapInvitation: () => send('/api/platform/bootstrap/recover', { method: 'POST' }),

    // The MFA gates. Authenticated, confirmed, and bound to the invitation whose token is presented.
    beginMfaEnrollment: (token) => send('/api/platform/mfa/enroll', {
      method: 'POST',
      body: { token },
      expect: ['sharedKey', 'provisioningUri', 'recoveryCodes'],
    }),

    verifyMfaEnrollment: (token, code) => send('/api/platform/mfa/verify', { method: 'POST', body: { token, code } }),

    acknowledgeRecoveryCodes: (token) =>
      send('/api/platform/mfa/recovery-acknowledge', { method: 'POST', body: { token } }),

    stepUp: (code) => send('/api/platform/mfa/step-up', { method: 'POST', body: { code } }),

    // Replacing a lost factor. The answer is shown once and there is no route that reads it back, so nothing
    // here caches it and nothing stores it (IA-REQ-025, IA-REQ-041).
    recoverMfa: (recoveryCode) => send('/api/platform/mfa/recover', {
      method: 'POST',
      body: { recoveryCode },
      expect: ['sharedKey', 'provisioningUri', 'recoveryCodes'],
    }),

    listOrganizations: (page, options) => sendPage(send, '/api/platform/organizations', page, { signal: options?.signal }),
    listIdentities: (page, options) => sendPage(send, '/api/platform/identities', page, { signal: options?.signal }),
    listAdministrators: (page, options) => sendPage(send, '/api/platform/admins', page, { signal: options?.signal }),
    listAudit: (page, options) => sendPage(send, '/api/platform/audit', page, { signal: options?.signal }),

    // Stopping and restarting one account. `expectedStatus` is the state the operator read in the directory, and
    // the server only lands the write if the account is still in it (IA-REQ-054): it is a precondition the client
    // must carry, not a hint it may drop.
    suspendIdentity: (identityId, reason, expectedStatus) =>
      send(`/api/platform/identities/${encodeURIComponent(identityId)}/suspend`, {
        method: 'POST',
        body: { reason, expectedStatus },
      }),

    reactivateIdentity: (identityId, expectedStatus, acknowledgeSelfDeactivation) =>
      send(`/api/platform/identities/${encodeURIComponent(identityId)}/reactivate`, {
        method: 'POST',
        body: { expectedStatus, acknowledgeSelfDeactivation },
      }),

    // Only the three members a policy always has are declared. A deployment with no configured policy answers with
    // `policyId`, `version`, `owner`, `approvedOn` and `source` all null, and that is the honest description of
    // "this system will not delete anything" (IA-REQ-056). Declaring them would make every such deployment read as
    // contract drift instead, because a declared member has to be present. `getContext` omits `activeTenant` for
    // the same reason.
    readRetentionPolicy: (options) => send('/api/platform/retention/policy', {
      expect: ['personalDataMode', 'activeHoldCount', 'categories'],
      signal: options?.signal,
    }),

    // `releasedAt` is absent for the same reason: a hold that was just placed has not been released, so the field
    // is null on the only answer this call can receive.
    placeRetentionHold: (subjectIdentityId, reasonCode, reference) =>
      send('/api/platform/retention/holds', {
        method: 'POST',
        body: { subjectIdentityId, reasonCode, reference },
        expect: ['holdId', 'subjectIdentityId', 'reasonCode', 'reference', 'placedAt', 'placedByMembershipId', 'version'],
      }),

    // Bodyless: the route is idempotent and deliberately silent about whether the hold existed, so there is
    // nothing to send beyond which one to release.
    releaseRetentionHold: (holdId) =>
      send(`/api/platform/retention/holds/${encodeURIComponent(holdId)}`, { method: 'DELETE' }),

    suspendOrganization: (tenantId, reason) =>
      send(`/api/platform/organizations/${encodeURIComponent(tenantId)}/suspend`, { method: 'POST', body: { reason } }),

    reactivateOrganization: (tenantId) =>
      send(`/api/platform/organizations/${encodeURIComponent(tenantId)}/reactivate`, { method: 'POST', body: {} }),

    inviteAdministrator: (email) =>
      send('/api/platform/admins/invitations', { method: 'POST', body: { email } }),

    // Only the membership identifier the protected directory handed over. Nothing else about an administrator
    // takes part in revoking them (IA-REQ-044).
    revokeAdministrator: (membershipId) =>
      send(`/api/platform/admins/${encodeURIComponent(membershipId)}/revoke`, { method: 'POST', body: {} }),
  };
}
