/**
 * The Platform half of the API surface (IA-REQ-045).
 *
 * It shares the identity client's transport rather than opening its own, so there is one session cookie, one
 * antiforgery request token, and one place that knows how to read a problem document. A second token holder would
 * be stale from the moment the first one signed in.
 *
 * Every directory declares `items` and `nextCursor` as its expected members. Those two names are on the identity
 * contract's forbidden list precisely because identity endpoints must not grow a pagination envelope; declaring
 * them here is what makes a Platform directory a distinct resource rather than that envelope leaking sideways.
 */
const DIRECTORY = ['items', 'nextCursor'];

/** The bounds the API enforces. Sending something outside them would simply be clamped, so the client does not. */
const MINIMUM_LIMIT = 1;
const MAXIMUM_LIMIT = 100;

const page = (path, { limit = 25, cursor } = {}) => {
  const query = new URLSearchParams();
  query.set('limit', String(Math.min(Math.max(limit, MINIMUM_LIMIT), MAXIMUM_LIMIT)));
  if (cursor) query.set('cursor', cursor);
  return `${path}?${query.toString()}`;
};

export function createPlatformClient(transport) {
  if (!transport) throw new Error('The Platform client shares the identity transport.');
  const { send } = transport;

  return {
    // Public onboarding. A recipient answering a Platform invitation has no session, and may have no account.
    registerFromInvitation: (token, password) =>
      send('/api/platform/invitations/register', { method: 'POST', body: { token, password } }),

    confirmInvitation: (token, confirmationToken) =>
      send('/api/platform/invitations/confirm', { method: 'POST', body: { token, confirmationToken } }),

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

    listOrganizations: (options) => send(page('/api/platform/organizations', options), { expect: DIRECTORY }),
    listIdentities: (options) => send(page('/api/platform/identities', options), { expect: DIRECTORY }),
    listAdministrators: (options) => send(page('/api/platform/admins', options), { expect: DIRECTORY }),
    listAudit: (options) => send(page('/api/platform/audit', options), { expect: DIRECTORY }),

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
