import { isProblem, readProblem, readSuccess } from './problemDetails';

/**
 * The only place this application talks to the API.
 *
 * Every request is same-origin and carries the session cookie; only mutations carry the antiforgery request
 * token, which is held in memory and never written to storage (IA-REQ-025). The server rotates the
 * cookie/token pair whenever the authentication state changes, so the client bootstraps a fresh pair at exactly
 * those moments and after a stable `antiforgery_validation_failed`. It never replays the refused mutation: a
 * state change nobody asked for twice is how one sign-in becomes two sessions. The caller retries.
 */
const ANTIFORGERY = '/api/identity/antiforgery';

export class IdentityProblem extends Error {
  constructor(problem) {
    super(problem.code);
    this.name = 'IdentityProblem';
    this.problem = problem;
  }
}

export function createIdentityClient() {
  let requestToken = null;

  const bootstrapAntiforgery = async () => {
    const response = await fetch(ANTIFORGERY);
    if (!response.ok) throw new Error('Unable to establish the request token.');
    const payload = await readSuccess(response, ['requestToken']);
    requestToken = payload.requestToken;
    return requestToken;
  };

  const send = async (path, { method = 'GET', body, expect = [] } = {}) => {
    const mutation = method !== 'GET';
    if (mutation && requestToken === null) await bootstrapAntiforgery();

    const response = await fetch(path, {
      method,
      headers: {
        Accept: 'application/json, application/problem+json',
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(mutation ? { 'X-CSRF-TOKEN': requestToken } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });

    if (response.ok) return readSuccess(response, expect);
    if (!isProblem(response)) throw new Error(`The API answered ${response.status} without a problem document.`);

    const problem = await readProblem(response);
    // A rotated pair is the documented reason a mutation is refused. Replace it and let the caller decide
    // whether the action is still wanted; retrying here would repeat a state change on the user's behalf.
    if (problem.code === 'antiforgery_validation_failed') await bootstrapAntiforgery();
    throw new IdentityProblem(problem);
  };

  return {
    bootstrapAntiforgery,
    hasRequestToken: () => requestToken !== null,

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
