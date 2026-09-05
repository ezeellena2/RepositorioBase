import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { createApiTransport } from '../identity/api/apiTransport';
import { createPlatformClient } from './api/platformClient';
import { server } from '../../test/server';
import { ANTIFORGERY_TOKEN, antiforgery, problem } from '../../test/identityServer';

const clientWith = () => createPlatformClient(createApiTransport());

const captured = (method, path, respond) => {
  const seen = { calls: 0, headers: null, body: null, url: null };
  server.use(http[method](path, async ({ request }) => {
    seen.calls += 1;
    seen.headers = request.headers;
    seen.url = new URL(request.url);
    seen.body = request.body ? await request.clone().json().catch(() => null) : null;
    return respond();
  }));
  return seen;
};

/**
 * The Platform client's half of the contract (IA-REQ-045).
 *
 * What is worth testing here is not that a fetch happens but what it carries: the antiforgery token on every
 * mutation and none on a read, no email anywhere near recovery, and only the membership identifier the protected
 * directory handed over when revoking someone.
 */
describe('platform client', () => {
  it('carries the antiforgery token on a public onboarding submission', async () => {
    server.use(antiforgery());
    const seen = captured('post', '/api/platform/invitations/register', () => new HttpResponse(null, { status: 202 }));

    await clientWith().registerFromInvitation('token-1', 'Testing1234!');

    expect(seen.calls).toBe(1);
    expect(seen.headers.get('X-CSRF-TOKEN')).toBe(ANTIFORGERY_TOKEN);
    expect(seen.body).toEqual({ token: 'token-1', password: 'Testing1234!' });
  });

  it('sends nothing at all when recovering the bootstrap invitation', async () => {
    server.use(antiforgery());
    const seen = captured('post', '/api/platform/bootstrap/recover', () => new HttpResponse(null, { status: 202 }));

    await clientWith().recoverBootstrapInvitation();

    expect(seen.body).toBeNull();
    expect(seen.url.search).toBe('');
    expect(seen.headers.get('X-CSRF-TOKEN')).toBe(ANTIFORGERY_TOKEN);
  });

  it('reports a refused antiforgery on recovery as a typed problem rather than a neutral success', async () => {
    server.use(antiforgery());
    server.use(http.post('/api/platform/bootstrap/recover', () => problem(400, 'antiforgery_validation_failed')));

    await expect(clientWith().recoverBootstrapInvitation()).rejects.toMatchObject({
      problem: { status: 400, code: 'antiforgery_validation_failed' },
    });
  });

  it('reports an exhausted recovery limit with the wait the server asked for', async () => {
    server.use(antiforgery());
    server.use(http.post('/api/platform/bootstrap/recover', () =>
      problem(429, 'rate_limit_exceeded', {}, { 'Retry-After': '120' })));

    await expect(clientWith().recoverBootstrapInvitation()).rejects.toMatchObject({
      problem: { status: 429, code: 'rate_limit_exceeded', retryAfterSeconds: 120 },
    });
  });

  it('hands over the shared key and the recovery codes exactly once, from the declared members', async () => {
    server.use(antiforgery());
    server.use(http.post('/api/platform/mfa/enroll', () => HttpResponse.json({
      sharedKey: 'JBSWY3DPEHPK3PXP',
      provisioningUri: 'otpauth://totp/Platform:owner@example.test?secret=JBSWY3DPEHPK3PXP',
      recoveryCodes: ['aaaaa-bbbbb-ccccc-ddddd'],
    })));

    const enrollment = await clientWith().beginMfaEnrollment('token-1');

    expect(enrollment.sharedKey).toBe('JBSWY3DPEHPK3PXP');
    expect(enrollment.recoveryCodes).toHaveLength(1);
  });

  it('refuses an enrollment response that is missing a declared member', async () => {
    server.use(antiforgery());
    server.use(http.post('/api/platform/mfa/enroll', () => HttpResponse.json({ sharedKey: 'JBSWY3DPEHPK3PXP' })));

    await expect(clientWith().beginMfaEnrollment('token-1')).rejects.toThrow(/missing/i);
  });

  it('carries only the membership identifier into a revocation', async () => {
    server.use(antiforgery());
    const seen = captured('post', '/api/platform/admins/:membershipId/revoke', () => new HttpResponse(null, { status: 204 }));

    await clientWith().revokeAdministrator('membership-1');

    expect(seen.url.pathname).toBe('/api/platform/admins/membership-1/revoke');
    expect(seen.body).toEqual({});
  });

  it('sends the suspension reason and never a free-text note', async () => {
    server.use(antiforgery());
    const seen = captured('post', '/api/platform/organizations/:tenantId/suspend', () => new HttpResponse(null, { status: 204 }));

    await clientWith().suspendOrganization('tenant-1', 'PolicyViolation');

    expect(seen.body).toEqual({ reason: 'PolicyViolation' });
    expect(Object.keys(seen.body)).toEqual(['reason']);
  });

  it('does not carry an antiforgery token on a directory read', async () => {
    const seen = captured('get', '/api/platform/organizations', () => HttpResponse.json({ items: [], nextCursor: null }));

    await clientWith().listOrganizations();

    expect(seen.headers.get('X-CSRF-TOKEN')).toBeNull();
  });

  it('refuses a Platform response shaped like an internal Result', async () => {
    server.use(http.get('/api/platform/organizations', () =>
      HttpResponse.json({ succeeded: true, value: { items: [], nextCursor: null } })));

    await expect(clientWith().listOrganizations()).rejects.toThrow(/succeeded|value/i);
  });

  it('cannot be built without the identity transport', () => {
    expect(() => createPlatformClient(undefined)).toThrow(/transport/i);
  });
});
