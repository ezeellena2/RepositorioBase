import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { IdentityProvider } from '../identity/context/IdentityProvider';
import { PlatformPanel } from './PlatformPanel';
import { server } from '../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../test/identityServer';

const PLATFORM_PERMISSIONS = [
  'platform.organizations.read',
  'platform.admins.read',
  'platform.audit.read',
  'platform.admins.manage',
  'platform.tenants.manage',
];

const platformContext = (permissions = PLATFORM_PERMISSIONS) => signedInContext({
  activeTenant: { id: 'platform-1', type: 'Platform', name: 'platform' },
  availableTenants: [{ id: 'platform-1', type: 'Platform', name: 'platform' }],
  permissions,
});

const ORGANIZATION = {
  tenantId: 'tenant-1',
  slug: 'acme-1',
  type: 'Organization',
  status: 'Active',
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
  suspensionReason: null,
  suspendedAt: null,
  authorizationVersion: 1,
};

const ADMINISTRATOR = {
  membershipId: 'membership-1',
  identityId: 'identity-1',
  normalizedEmail: 'owner@example.test',
  emailConfirmed: true,
  membershipStatus: 'Active',
  mfaStatus: 'Active',
  isOwner: true,
  since: '2026-09-01T00:00:00Z',
};

const directories = ({ organizations = [ORGANIZATION], administrators = [ADMINISTRATOR], audit = [] } = {}) => [
  http.get('/api/platform/organizations', () => HttpResponse.json({ items: organizations, nextCursor: null })),
  http.get('/api/platform/admins', () => HttpResponse.json({ items: administrators, nextCursor: null })),
  http.get('/api/platform/audit', () => HttpResponse.json({ items: audit, nextCursor: null })),
];

const renderPanel = () => render(<IdentityProvider><PlatformPanel /></IdentityProvider>);

/**
 * The panel (IA-REQ-045/046).
 *
 * Half of what matters is what it will not do: render for a session that is not operating as Platform, offer an
 * impersonation or delete control, or carry out a suspension or a revocation on one click. The other half is that
 * it renders exactly the allowlisted projection and reads the server's typed refusals rather than inventing its
 * own.
 */
describe('platform panel', () => {
  it('does not render for a session that is not operating as Platform', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    renderPanel();

    expect(await screen.findByText(/MFA-authenticated Platform administrator/i)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('does not render for a Platform session that lacks the directory permission', async () => {
    server.use(antiforgery(), contextIs(platformContext(['members.view'])));
    renderPanel();

    expect(await screen.findByText(/MFA-authenticated Platform administrator/i)).toBeInTheDocument();
  });

  it('renders the allowlisted organization, administrator and audit projections', async () => {
    server.use(antiforgery(), contextIs(platformContext()), ...directories({
      audit: [{ eventId: 'event-1', eventType: 'platform.bootstrap.completed', outcome: 'owner_invited' }],
    }));
    renderPanel();

    expect(await screen.findByText('acme-1')).toBeInTheDocument();
    expect(screen.getByText('owner@example.test')).toBeInTheDocument();
    expect(screen.getByText('Active (owner)')).toBeInTheDocument();
    expect(screen.getByText(/platform.bootstrap.completed/)).toBeInTheDocument();
  });

  /** A suspension is visible to everyone inside the affected tenant and is not undone by clicking again. */
  it('requires a confirmation and a reason before suspending an organization', async () => {
    const suspensions = [];
    server.use(antiforgery(), contextIs(platformContext()), ...directories());
    server.use(http.post('/api/platform/organizations/:tenantId/suspend', async ({ request, params }) => {
      suspensions.push({ tenantId: params.tenantId, body: await request.json() });
      return new HttpResponse(null, { status: 204 });
    }));
    renderPanel();

    await userEvent.click(await screen.findByRole('button', { name: 'Suspend acme-1' }));
    expect(suspensions).toHaveLength(0);

    await userEvent.selectOptions(screen.getByLabelText('Reason'), 'SecurityIncident');
    await userEvent.click(screen.getByRole('button', { name: 'Confirm suspension' }));

    await waitFor(() => expect(suspensions).toHaveLength(1));
    expect(suspensions[0]).toEqual({ tenantId: 'tenant-1', body: { reason: 'SecurityIncident' } });
  });

  it('requires a confirmation before revoking an administrator and sends only the membership id', async () => {
    const revocations = [];
    server.use(antiforgery(), contextIs(platformContext()), ...directories());
    server.use(http.post('/api/platform/admins/:membershipId/revoke', ({ params }) => {
      revocations.push(params.membershipId);
      return new HttpResponse(null, { status: 204 });
    }));
    renderPanel();

    await userEvent.click(await screen.findByRole('button', { name: 'Revoke owner@example.test' }));
    expect(revocations).toHaveLength(0);

    await userEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));

    await waitFor(() => expect(revocations).toEqual(['membership-1']));
  });

  it('shows the server refusal when the last owner cannot be revoked', async () => {
    server.use(antiforgery(), contextIs(platformContext()), ...directories());
    server.use(http.post('/api/platform/admins/:membershipId/revoke', () =>
      problem(400, 'invalid_platform_operation')));
    renderPanel();

    await userEvent.click(await screen.findByRole('button', { name: 'Revoke owner@example.test' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/not valid in its current state/i);
  });

  it('shows the server refusal when the second factor is not recent enough', async () => {
    server.use(antiforgery(), contextIs(platformContext()), ...directories());
    server.use(http.post('/api/platform/organizations/:tenantId/suspend', () => problem(401, 'recent_mfa_required')));
    renderPanel();

    await userEvent.click(await screen.findByRole('button', { name: 'Suspend acme-1' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm suspension' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/second factor again/i);
  });

  it('shows a stale lifecycle change as the typed conflict the server sent', async () => {
    server.use(antiforgery(), contextIs(platformContext()), ...directories());
    server.use(http.post('/api/platform/organizations/:tenantId/suspend', () =>
      problem(409, 'platform_tenant_concurrency_conflict')));
    renderPanel();

    await userEvent.click(await screen.findByRole('button', { name: 'Suspend acme-1' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm suspension' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/changed while you were working/i);
  });

  it('says why a directory could not be read instead of showing an empty one', async () => {
    server.use(antiforgery(), contextIs(platformContext()));
    server.use(http.get('/api/platform/organizations', () => problem(403, 'permission_denied')));
    server.use(...directories().slice(1));
    renderPanel();

    expect(await screen.findByText(/do not have permission/i)).toBeInTheDocument();
  });

  /** The capabilities Platform must never grow, checked at the surface a person actually touches. */
  it('offers no impersonation, no deletion and no way to choose a tenant to act as', async () => {
    server.use(antiforgery(), contextIs(platformContext()), ...directories());
    renderPanel();

    await screen.findByText('acme-1');
    const labels = screen.getAllByRole('button').map((button) => button.textContent.toLowerCase());
    expect(labels.some((label) => label.includes('impersonat'))).toBe(false);
    expect(labels.some((label) => label.includes('delete'))).toBe(false);
    expect(screen.queryByLabelText(/act as/i)).not.toBeInTheDocument();
  });

  it('does not offer inviting an administrator without the permission to do it', async () => {
    server.use(antiforgery(), contextIs(platformContext(['platform.organizations.read', 'platform.admins.read', 'platform.audit.read'])), ...directories());
    renderPanel();

    await screen.findByText('acme-1');
    expect(screen.queryByLabelText('Invite an administrator')).not.toBeInTheDocument();
  });
});
