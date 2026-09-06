import { ApiProblem, createApiTransport } from './apiTransport';

/**
 * The identity half of the API surface.
 *
 * It owns no transport of its own: the request token lives in one place for the whole application, because the
 * server rotates the pair on every authentication change and a second cached copy would be stale from that
 * moment on. What this module owns is the endpoints and the members each one is contractually allowed to
 * answer with.
 */

/** Kept as the identity-facing name for the error the transport throws, so existing callers still catch it. */
export { ApiProblem as IdentityProblem } from './apiTransport';

export function createIdentityClient(transport = createApiTransport()) {
  const { bootstrapAntiforgery, hasRequestToken, send } = transport;

  return {
    transport,
    bootstrapAntiforgery,
    hasRequestToken,

    // activeTenant is genuinely absent for an identity that holds no membership yet, so it is not required.
    // Demanding it would read "you belong to nothing" as "the contract drifted".
    getContext: () => send('/api/identity/context', {
      expect: ['user', 'availableTenants', 'permissions', 'session', 'personalData'],
    }),

    signIn: (email, password) => send('/api/identity/sessions', { method: 'POST', body: { email, password } }),
    signOut: () => send('/api/identity/sessions/current', { method: 'DELETE' }),

    selectTenant: (tenantId) => send('/api/identity/context/tenant', {
      method: 'PUT',
      body: { tenantId },
      expect: ['user', 'availableTenants', 'permissions', 'session', 'personalData'],
    }),

    registerOrganization: (request) => send('/api/identity/organizations/register', { method: 'POST', body: request }),
    confirmEmail: (token) => send('/api/identity/confirm-email', { method: 'POST', body: { token } }),

    // A person's own context. The signup is neutral and bodyless like the organization one; the two authenticated
    // calls name the members they are allowed to read, so a response that grew a field would be refused here.
    // A person's own devices. The list is a bare array by contract, so nothing here declares an envelope.
    listSessions: () => send('/api/identity/sessions', {
      expectArray: true,
      expect: ['sessionRef', 'isCurrent', 'deviceLabel', 'createdAt', 'lastSeenAt', 'expiresAt'],
    }),
    revokeSession: (sessionRef) => send(`/api/identity/sessions/${encodeURIComponent(sessionRef)}`, { method: 'DELETE' }),
    revokeOtherSessions: () => send('/api/identity/sessions/others', { method: 'DELETE' }),
    reauthenticate: (action, password) => send('/api/identity/credentials/reauthenticate', {
      method: 'POST',
      body: { action, password },
    }),

    registerPersonal: (request) => send('/api/identity/personal/register', { method: 'POST', body: request }),
    createPersonalContext: (request) => send('/api/identity/personal', { method: 'POST', body: request }),
    getPersonalProfile: () => send('/api/identity/profile', {
      expect: ['fullName', 'displayName', 'email', 'personalTenantId', 'document', 'version', 'updatedAt'],
    }),
    updatePersonalProfile: (request) => send('/api/identity/profile', {
      method: 'PUT',
      body: request,
      expect: ['fullName', 'displayName', 'email', 'personalTenantId', 'document', 'version', 'updatedAt'],
    }),

    inviteMember: (tenantId, email, roleIds) => send(`/api/tenants/${encodeURIComponent(tenantId)}/invitations`, {
      method: 'POST',
      body: { email, roleIds },
      expect: ['invitationId', 'expiresAt'],
    }),

    registerFromInvitation: (token, password) => send('/api/invitations/register', { method: 'POST', body: { token, password } }),

    acceptInvitation: (token) => send('/api/invitations/accept', {
      method: 'POST',
      body: { token },
      expect: ['tenantId', 'membershipId'],
    }),
  };
}
