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

  // A directory answers one page and, when there is more, an opaque cursor for the next. The cursor is the only
  // thing that continues a listing: `limit` is deliberately never sent, so the server's own page size stays the
  // contract rather than something a caller can widen.
  const continued = (path, cursor) => (cursor ? `${path}?cursor=${encodeURIComponent(cursor)}` : path);
  const identityContextMembers = [
    'user',
    'preferredLanguage',
    'availableTenants',
    'permissions',
    'session',
    'personalData',
  ];

  return {
    transport,
    bootstrapAntiforgery,
    hasRequestToken,

    // activeTenant is genuinely absent for an identity that holds no membership yet, so it is not required.
    // Demanding it would read "you belong to nothing" as "the contract drifted".
    getContext: () => send('/api/identity/context', {
      expect: identityContextMembers,
    }),

    signIn: (email, password) => send('/api/identity/sessions', { method: 'POST', body: { email, password } }),
    signOut: () => send('/api/identity/sessions/current', { method: 'DELETE' }),

    selectTenant: (tenantId) => send('/api/identity/context/tenant', {
      method: 'PUT',
      body: { tenantId },
      expect: identityContextMembers,
    }),

    updatePreferredLanguage: (language) => send('/api/identity/context/language', {
      method: 'PUT',
      body: { language },
      expect: identityContextMembers,
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
    requestPasswordRecovery: (email) => send('/api/identity/credentials/password/recovery', { method: 'POST', body: { email } }),
    resetPassword: (token, newPassword) => send('/api/identity/credentials/password/reset', { method: 'POST', body: { token, newPassword } }),
    changePassword: (newPassword) => send('/api/identity/credentials/password', { method: 'PUT', body: { newPassword } }),
    deactivateAccount: () => send('/api/identity/account/deactivate', { method: 'POST' }),
    requestAccountReactivation: (email) => send('/api/identity/account/reactivation-requests', { method: 'POST', body: { email } }),
    reactivateAccount: (reactivationToken, password) => send('/api/identity/account/reactivate', { method: 'POST', body: { reactivationToken, password } }),
    // Whether there is a password at all, and when it last changed. Two screens are dishonest without it: an
    // account whose only way in is a provider must not be offered an unlink that can only be refused.
    getOwnCredentials: () => send('/api/identity/credentials', {
      expect: ['hasPassword', 'passwordUpdatedAt'],
    }),

    // Provider accounts. Each start answers only where to send the browser next; which round trip it is stays in
    // a cookie the server sealed, so nothing here holds an identifier a caller could swap for somebody else's.
    startExternalLogin: (provider) => send(`/api/identity/external/${encodeURIComponent(provider)}/login/start`, {
      method: 'POST',
      expect: ['authorizationRequestUri'],
    }),
    startExternalLink: (provider) => send(`/api/identity/external/${encodeURIComponent(provider)}/link/start`, {
      method: 'POST',
      body: { consent: true },
      expect: ['authorizationRequestUri'],
    }),
    startExternalProof: (provider, action) => send(`/api/identity/external/${encodeURIComponent(provider)}/proof/start`, {
      method: 'POST',
      body: { action },
      expect: ['authorizationRequestUri'],
    }),
    // One completion, whatever the round trip was for. Which one it was is in a cookie the server sealed, so
    // there is nothing here for a caller to choose.
    completeExternalRoundTrip: () => send('/api/identity/external/complete', { method: 'POST' }),
    // `available` is the deployment's own answer about which providers exist. Only the server knows: one with
    // no client configured has no middleware and no route that can succeed.
    listExternalLinks: () => send('/api/identity/external', {
      expect: ['items', 'available'],
    }),
    unlinkExternal: (provider) => send(`/api/identity/external/${encodeURIComponent(provider)}`, { method: 'DELETE' }),

    // Custom roles inside one Organization. Every route is addressed by tenant and the server compares that
    // address against the session's own active tenant, so naming another one is refused rather than honoured.
    listPermissionCatalog: (tenantId) => send(`/api/tenants/${encodeURIComponent(tenantId)}/permission-catalog`, {
      expectArray: true,
      expect: ['code', 'grantable'],
    }),
    listRoles: (tenantId, cursor = null) => send(continued(`/api/tenants/${encodeURIComponent(tenantId)}/roles`, cursor), {
      expect: ['items', 'nextCursor'],
    }),
    createRole: (tenantId, name, permissions) => send(`/api/tenants/${encodeURIComponent(tenantId)}/roles`, {
      method: 'POST',
      body: { name, permissions },
      expect: ['roleId', 'name', 'isSystem', 'isRetired', 'permissions', 'version'],
    }),
    updateRole: (tenantId, roleId, name, permissions, version) => send(`/api/tenants/${encodeURIComponent(tenantId)}/roles/${encodeURIComponent(roleId)}`, {
      method: 'PUT',
      body: { name, permissions, version },
      expect: ['roleId', 'name', 'isSystem', 'isRetired', 'permissions', 'version'],
    }),
    retireRole: (tenantId, roleId, version) => send(`/api/tenants/${encodeURIComponent(tenantId)}/roles/${encodeURIComponent(roleId)}/retire`, {
      method: 'POST',
      body: { version },
    }),

    // Member administration. `version` is the row's own concurrency token, echoed back so a change made against
    // a member somebody else has since altered is refused rather than silently applied over theirs.
    listMembers: (tenantId, cursor = null) => send(continued(`/api/tenants/${encodeURIComponent(tenantId)}/members`, cursor), {
      expect: ['items', 'nextCursor'],
    }),
    listTenantInvitations: (tenantId, cursor = null) => send(continued(`/api/tenants/${encodeURIComponent(tenantId)}/invitations`, cursor), {
      expect: ['items', 'nextCursor'],
    }),
    updateMemberRoles: (tenantId, membershipId, roleIds, version) =>
      send(`/api/tenants/${encodeURIComponent(tenantId)}/members/${encodeURIComponent(membershipId)}/roles`, {
        method: 'PUT',
        body: { roleIds, version },
        expect: ['membershipId', 'identityId', 'displayName', 'normalizedEmail', 'status', 'roleIds', 'isOwner', 'version'],
      }),
    changeMemberStatus: (tenantId, membershipId, change, version) =>
      send(`/api/tenants/${encodeURIComponent(tenantId)}/members/${encodeURIComponent(membershipId)}/${encodeURIComponent(change)}`, {
        method: 'POST',
        body: { version },
      }),
    transferOwnership: (tenantId, toMembershipId, version) =>
      send(`/api/tenants/${encodeURIComponent(tenantId)}/ownership/transfer`, {
        method: 'POST',
        body: { toMembershipId, version },
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

    // Saying the recorded document is wrong. The claimed number leaves the browser once, to a route that
    // protects it on arrival, and the answer carries only an opaque identifier back (IA-REQ-058).
    openDocumentDispute: (request) => send('/api/identity/profile/document/disputes', {
      method: 'POST',
      body: request,
      expect: ['disputeId'],
    }),

    inviteMember: (tenantId, email, roleIds) => send(`/api/tenants/${encodeURIComponent(tenantId)}/invitations`, {
      method: 'POST',
      body: { email, roleIds },
      expect: ['invitationId', 'expiresAt'],
    }),

    // Reissuing rotates the token in the recipient's envelope and hands nothing back here; withdrawing ends the
    // offer. Both name the invitation in the route and carry no body (IA-REQ-015/017/018).
    resendInvitation: (tenantId, invitationId) =>
      send(`/api/tenants/${encodeURIComponent(tenantId)}/invitations/${encodeURIComponent(invitationId)}/resend`, { method: 'POST' }),
    cancelInvitation: (tenantId, invitationId) =>
      send(`/api/tenants/${encodeURIComponent(tenantId)}/invitations/${encodeURIComponent(invitationId)}/cancel`, { method: 'POST' }),

    registerFromInvitation: (token, password) => send('/api/invitations/register', { method: 'POST', body: { token, password } }),

    acceptInvitation: (token) => send('/api/invitations/accept', {
      method: 'POST',
      body: { token },
      expect: ['tenantId', 'membershipId'],
    }),
  };
}
