import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const app = resolve(import.meta.dirname, '..');
const read = (relative) => readFileSync(resolve(app, relative), 'utf8');
const has = (relative) => existsSync(resolve(app, relative));

/**
 * A filesystem contract rather than an import graph. Importing a module that does not exist yet is a build
 * failure, and a build failure is not a RED anyone can read: it names the first missing file and stops. This
 * names every one of them at once, and keeps naming them until each exists.
 */
describe('identity feature files', () => {
  it.each([
    'features/identity/api/identityClient.js',
    'features/identity/api/problemDetails.js',
    'features/identity/context/IdentityProvider.jsx',
    'features/identity/login/LoginPage.jsx',
    'features/identity/register/RegisterOrganizationPage.jsx',
    'features/identity/register/ConfirmEmailPage.jsx',
    'features/identity/tenants/TenantSelector.jsx',
    'features/identity/invitations/InvitationPages.jsx',
    'features/identity/invitations/InviteMemberPage.jsx',
  ])('exposes %s', (relative) => {
    expect(has(relative), `${relative} is missing`).toBe(true);
  });

  /**
   * The legacy provider and its pages are replaced, not merely bypassed. Leaving them importable leaves a second
   * way to sign in that no test covers and no contract governs.
   */
  it.each([
    'components/api-authorization/AuthContext.jsx',
    'components/api-authorization/LoginPage.jsx',
    'components/api-authorization/RegisterPage.jsx',
  ])('no longer ships %s', (relative) => {
    expect(has(relative), `${relative} should have been removed`).toBe(false);
  });

  it('routes every identity journey from the root', () => {
    const routes = read('AppRoutes.jsx');
    for (const path of [
      '/login',
      '/organizations/register',
      '/invitations/register',
      '/invitations/accept',
      '/confirm-email',
      '/identity',
      '/organizations/select',
      '/members/invite',
    ]) {
      expect(routes, `${path} is not routed`).toContain(path);
    }
  });

  /**
   * One boundary for the network. A component that calls fetch directly bypasses the antiforgery lifecycle and
   * the Problem Details parser, which is exactly how a client starts disagreeing with its own API contract.
   */
  it('keeps fetch inside the identity client', () => {
    const offenders = [
      'App.jsx',
      'features/identity/context/IdentityProvider.jsx',
      'features/identity/login/LoginPage.jsx',
      'features/identity/register/RegisterOrganizationPage.jsx',
      'features/identity/register/ConfirmEmailPage.jsx',
      'features/identity/tenants/TenantSelector.jsx',
      'features/identity/invitations/InvitationPages.jsx',
      'features/identity/invitations/InviteMemberPage.jsx',
    ].filter((relative) => has(relative) && /\bfetch\s*\(/.test(read(relative)));

    expect(offenders).toEqual([]);
  });

  it('never targets an absolute API origin', () => {
    for (const relative of ['features/identity/api/identityClient.js']) {
      if (!has(relative)) continue;
      expect(read(relative)).not.toMatch(/https?:\/\/[^\s'"`]*\/api/);
    }
  });

  /**
   * The API is reached same-origin through Vite's proxy. An absolute origin, or a permissive CORS fallback in
   * its place, would move the session cookie and the antiforgery pair onto a cross-origin request — which is the
   * arrangement SPEC section 8 rules out for this SPA.
   */
  it('keeps the API same-origin through the dev proxy', () => {
    const config = readFileSync(resolve(app, '..', 'vite.config.ts'), 'utf8');

    expect(config).toContain("'/api'");
    expect(config).not.toMatch(/cors\s*:/);
    expect(config).not.toMatch(/Access-Control-Allow-Origin/);
    expect(config).not.toMatch(/https?:\/\/[^\s'"`]*\/api/);
  });
});
