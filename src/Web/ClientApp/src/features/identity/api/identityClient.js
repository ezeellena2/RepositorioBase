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
      expect: ['user', 'availableTenants', 'permissions', 'session'],
    }),

    signIn: (email, password) => send('/api/identity/sessions', { method: 'POST', body: { email, password } }),
    signOut: () => send('/api/identity/sessions/current', { method: 'DELETE' }),

    selectTenant: (tenantId) => send('/api/identity/context/tenant', {
      method: 'PUT',
      body: { tenantId },
      expect: ['user', 'availableTenants', 'permissions', 'session'],
    }),

    registerOrganization: (request) => send('/api/identity/organizations/register', { method: 'POST', body: request }),
    confirmEmail: (token) => send('/api/identity/confirm-email', { method: 'POST', body: { token } }),

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
