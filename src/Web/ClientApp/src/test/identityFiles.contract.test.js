import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const app = resolve(import.meta.dirname, '..');
const read = (relative) => readFileSync(resolve(app, relative), 'utf8');
const has = (relative) => existsSync(resolve(app, relative));
const actionCatches = new Map([
  ['features/identity/sessions/SessionsPage.jsx', 1],
  ['features/identity/roles/RolesPage.jsx', 2],
  ['features/identity/members/MembersPage.jsx', 2],
  ['features/identity/credentials/ExternalAccountsPage.jsx', 2],
  ['features/identity/invitations/InviteMemberPage.jsx', 1],
  ['features/identity/credentials/PasswordPages.jsx', 1],
  ['features/identity/lifecycle/AccountLifecyclePages.jsx', 1],
  ['features/identity/people/PersonalPages.jsx', 1],
  ['features/identity/login/LoginPage.jsx', 1],
]);

/**
 * A filesystem contract rather than an import graph. Importing a module that does not exist yet is a build
 * failure, and a build failure is not a RED anyone can read: it names the first missing file and stops. This
 * names every one of them at once, and keeps naming them until each exists.
 */
describe('identity feature files', () => {
  it.each([
    'features/identity/api/identityClient.js',
    'features/identity/api/problemDetails.js',
    'features/identity/useRead.js',
    'features/identity/context/IdentityProvider.jsx',
    'features/identity/login/LoginPage.jsx',
    'features/identity/register/RegisterOrganizationPage.jsx',
    'features/identity/register/ConfirmEmailPage.jsx',
    'features/identity/tenants/TenantSelector.jsx',
    'features/identity/invitations/InvitationPages.jsx',
    'features/identity/invitations/InviteMemberPage.jsx',
    'features/identity/roles/RolesPage.jsx',
    'features/identity/members/MembersPage.jsx',
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
      '/members',
      '/roles',
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
      'features/identity/roles/RolesPage.jsx',
      'features/identity/members/MembersPage.jsx',
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

describe('identity client failure classification contract', () => {
  it('routes the exact twelve remaining action catches through toProblem with no legacy synthetic code', () => {
    let classified = 0;
    for (const [path, expected] of actionCatches) {
      const source = read(path);
      const calls = source.match(/toProblem\(error\)/g) ?? [];
      expect(calls, path).toHaveLength(expected);
      expect(source, path).not.toMatch(/error\.problem\s*\?\?/);
      expect(source, path).not.toContain("code: 'unexpected'");
      classified += calls.length;
    }
    expect(classified).toBe(12);
  });

  it('classifies reads in the canonical hook and retires shared synthetic fallbacks', () => {
    const useSubmit = read('features/identity/useSubmit.js');
    expect(useSubmit).toContain('toProblem(failure)');
    expect(useSubmit).not.toContain('instanceof IdentityProblem');
    expect(useSubmit).not.toContain("code: 'internal_server_error', status: 0");

    const useRead = read('features/identity/useRead.js');
    expect(useRead.match(/toProblem\(failure\)/g) ?? []).toHaveLength(1);
    expect(useRead.match(/isRetryable\(problem\)/g) ?? []).toHaveLength(1);
    expect(useRead).not.toMatch(/failure\.problem\s*\?\?/);
    expect(useRead).not.toContain("code: 'unexpected'");
    expect(has('features/platform/shared/usePlatformRead.js')).toBe(false);

    const provider = read('features/identity/context/IdentityProvider.jsx');
    expect(provider).toContain('toProblem(failure)');
    expect(provider).not.toContain('context_unreadable');
  });
});
