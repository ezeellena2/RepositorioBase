import { http, HttpResponse } from 'msw';
import problemCodes from '../api/problemCodes.json';

export const ANTIFORGERY_TOKEN = 'request-token-1';

/** The signed-in context the API answers with, shaped exactly as SPEC section 7 declares it. */
export const signedInContext = (overrides = {}) => ({
  user: { id: 'user-1', displayName: 'Ana', emailConfirmed: true },
  preferredLanguage: null,
  activeTenant: { id: 'tenant-1', type: 'Organization', name: 'Acme' },
  availableTenants: [
    { id: 'tenant-1', type: 'Organization', name: 'Acme' },
    { id: 'tenant-2', type: 'Organization', name: 'Globex' },
  ],
  permissions: ['members.read', 'members.invite'],
  session: { expiresAt: '2026-12-31T00:00:00Z', requiresTwoFactor: false },
  personalData: { mode: 'Synthetic' },
  ...overrides,
});

export const antiforgery = (token = ANTIFORGERY_TOKEN) =>
  http.get('/api/identity/antiforgery', () => HttpResponse.json({ requestToken: token }));

export const contextIs = (body) =>
  http.get('/api/identity/context', () =>
    body ? HttpResponse.json(body) : HttpResponse.json({ code: 'authentication_required', traceId: 't' }, {
      status: 401,
      headers: { 'Content-Type': 'application/problem+json' },
    }));

export const problem = (status, code, extra = {}, headers = {}) => {
  if (problemCodes[code] !== status) {
    throw new Error(`Problem fixture ${status} ${code} is not declared in problemCodes.json.`);
  }

  return HttpResponse.json({ code, traceId: 'trace-1', ...extra }, {
    status,
    headers: { 'Content-Type': 'application/problem+json', ...headers },
  });
};
