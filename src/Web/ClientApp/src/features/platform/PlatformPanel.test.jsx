import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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

const platformContext = (permissions = PLATFORM_PERMISSIONS, session = {}) => signedInContext({
  activeTenant: { id: 'platform-1', type: 'Platform', name: 'platform' },
  availableTenants: [{ id: 'platform-1', type: 'Platform', name: 'platform' }],
  permissions,
  session: { expiresAt: '2026-12-31T00:00:00Z', requiresTwoFactor: false, ...session },
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

const deferred = () => {
  let resolve;
  const promise = new Promise((onResolve) => { resolve = onResolve; });
  return { promise, resolve };
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
  /**
   * A session that presented only a password holds the Platform membership and its read permissions, so nothing
   * about the tenant or the permission set distinguishes it. What distinguishes it is that it never proved the
   * second factor, and the panel must ask for that rather than request three directories the server will refuse
   * (IA-REQ-045).
   */
  it('asks for the second factor instead of reading the directories', async () => {
    const reads = [];
    server.use(antiforgery(), contextIs(platformContext(PLATFORM_PERMISSIONS, { requiresTwoFactor: true })));
    server.use(...directories().map((handler) => handler));
    server.events.on('request:start', ({ request }) => {
      if (new URL(request.url).pathname.startsWith('/api/platform/')) reads.push(new URL(request.url).pathname);
    });

    renderPanel();

    expect(await screen.findByRole('form', { name: 'Step up' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Organizations' })).not.toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    await waitFor(() => expect(reads.filter((path) => path !== '/api/platform/mfa/step-up')).toEqual([]));
    server.events.removeAllListeners();
  });

  it('associates and focuses a structured panel step-up detail without repeating it', async () => {
    server.use(antiforgery(), contextIs(platformContext(PLATFORM_PERMISSIONS, { requiresTwoFactor: true })));
    server.use(http.post('/api/platform/mfa/step-up', () => problem(400, 'validation_failed', {
      errors: { code: [{ code: 'too_long', params: { max: 16 } }] },
    })));

    renderPanel();
    const code = await screen.findByLabelText('Authenticator code');
    await userEvent.type(code, '000000');
    await userEvent.click(screen.getByRole('button', { name: 'Step up' }));

    await waitFor(() => expect(code).toHaveAttribute('aria-invalid', 'true'));
    expect(code).toHaveAccessibleDescription('Must be at most 16 characters.');
    expect(code).toHaveFocus();
    expect(screen.getByRole('alert')).not.toHaveTextContent('Must be at most 16 characters.');
  });

  it('binds an invalid authenticator code to its field exactly once and clears it on edit', async () => {
    server.use(antiforgery(), contextIs(platformContext(PLATFORM_PERMISSIONS, { requiresTwoFactor: true })));
    server.use(...directories());
    server.use(http.post('/api/platform/mfa/step-up', () => problem(400, 'invalid_mfa_code')));

    renderPanel();
    const code = await screen.findByLabelText('Authenticator code');
    await userEvent.type(code, '000000');
    await userEvent.click(screen.getByRole('button', { name: 'Step up' }));

    const refusal = 'That authenticator code was not accepted. Check the code and try again.';
    expect(await screen.findAllByText(refusal)).toHaveLength(1);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(code).toHaveAttribute('aria-invalid', 'true');
    expect(code).toHaveAccessibleDescription(refusal);
    expect(code).toHaveFocus();
    expect(screen.getByRole('heading', { name: 'Platform' })).toBeInTheDocument();
    expect(screen.getByRole('form', { name: 'Step up' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Organizations' })).not.toBeInTheDocument();

    await userEvent.type(code, '1');

    expect(code).not.toHaveAttribute('aria-invalid', 'true');
    expect(code).not.toHaveAccessibleDescription(refusal);
    expect(screen.queryByText(refusal)).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  /** Proving it is what opens the directories, and the answer comes from the reloaded context, not from the form. */
  it('opens the directories once the factor is proved', async () => {
    let requiresTwoFactor = true;
    server.use(antiforgery());
    server.use(http.get('/api/identity/context', () =>
      HttpResponse.json(platformContext(PLATFORM_PERMISSIONS, { requiresTwoFactor }))));
    server.use(...directories());
    server.use(http.post('/api/platform/mfa/step-up', () => {
      requiresTwoFactor = false;
      return new HttpResponse(null, { status: 204 });
    }));

    renderPanel();
    await userEvent.type(await screen.findByLabelText('Authenticator code'), '123456');
    await userEvent.click(screen.getByRole('form', { name: 'Step up' }).querySelector('button'));

    expect(await screen.findByRole('heading', { name: 'Organizations' })).toBeInTheDocument();
    expect(await screen.findByText('acme-1')).toBeInTheDocument();
  });

  it('does not render for a session that is not operating as Platform', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    renderPanel();

    expect(await screen.findByText(/MFA-authenticated Platform administrator/i)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('does not render for a Platform session that lacks the directory permission', async () => {
    server.use(antiforgery(), contextIs(platformContext(['members.read'])));
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

  it('keeps row actions disabled until a successful mutation has finished refreshing the directories', async () => {
    const refreshed = deferred();
    const suspended = { ...ORGANIZATION, status: 'Suspended', suspensionReason: 'SecurityIncident' };
    let organizationReads = 0;
    let reactivations = 0;
    server.use(antiforgery(), contextIs(platformContext()));
    server.use(http.get('/api/platform/organizations', () => {
      organizationReads += 1;
      return organizationReads === 1
        ? HttpResponse.json({ items: [suspended], nextCursor: null })
        : refreshed.promise;
    }));
    server.use(...directories().slice(1));
    server.use(http.post('/api/platform/organizations/:tenantId/reactivate', () => {
      reactivations += 1;
      return new HttpResponse(null, { status: 204 });
    }));
    renderPanel();

    const reactivate = await screen.findByRole('button', { name: 'Reactivate acme-1' });
    await userEvent.click(reactivate);
    await waitFor(() => expect(organizationReads).toBe(2));
    expect(reactivations).toBe(1);

    expect(reactivate).toBeDisabled();
    reactivate.click();
    expect(reactivations).toBe(1);

    refreshed.resolve(HttpResponse.json({ items: [suspended], nextCursor: null }));
    await waitFor(() => expect(reactivate).toBeEnabled());
  });

  it('shows the server refusal when the last owner cannot be revoked', async () => {
    server.use(antiforgery(), contextIs(platformContext()), ...directories());
    server.use(http.post('/api/platform/admins/:membershipId/revoke', () =>
      problem(400, 'invalid_platform_operation')));
    renderPanel();

    await userEvent.click(await screen.findByRole('button', { name: 'Revoke owner@example.test' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));

    const confirmation = screen.getByRole('form', { name: 'Confirm revocation' });
    const alert = await within(confirmation).findByRole('alert');
    expect(alert).toHaveTextContent(/not valid in its current state/i);
    expect(alert).toHaveFocus();
    expect(screen.getAllByRole('alert')).toHaveLength(1);
    expect(confirmation).toBeInTheDocument();
  });

  it('keeps a failed reactivate action and its focused refusal inside the originating row', async () => {
    const suspended = { ...ORGANIZATION, status: 'Suspended', suspensionReason: 'SecurityIncident' };
    server.use(antiforgery(), contextIs(platformContext()), ...directories({ organizations: [suspended] }));
    server.use(http.post('/api/platform/organizations/:tenantId/reactivate', () =>
      problem(409, 'platform_tenant_concurrency_conflict')));
    renderPanel();

    const reactivate = await screen.findByRole('button', { name: 'Reactivate acme-1' });
    const row = reactivate.closest('tr');
    await userEvent.click(reactivate);

    const alert = await within(row).findByRole('alert');
    expect(alert).toHaveTextContent(/changed while you were working/i);
    expect(alert).toHaveFocus();
    expect(screen.getAllByRole('alert')).toHaveLength(1);
  });

  it('keeps a failed invitation and its focused refusal inside the form without clearing the address', async () => {
    server.use(antiforgery(), contextIs(platformContext()), ...directories());
    server.use(http.post('/api/platform/admins/invitations', () => problem(409, 'invitation_conflict')));
    renderPanel();

    const form = await screen.findByRole('form', { name: 'Invite an administrator' });
    const email = within(form).getByLabelText('Invite an administrator');
    await userEvent.type(email, 'admin@example.test');
    await userEvent.click(within(form).getByRole('button', { name: 'Invite' }));

    const alert = await within(form).findByRole('alert');
    expect(alert).toHaveTextContent('That invitation cannot be completed in its current state.');
    expect(alert).toHaveFocus();
    expect(email).toHaveValue('admin@example.test');
    expect(within(form).queryByRole('status')).not.toBeInTheDocument();
    expect(screen.getAllByRole('alert')).toHaveLength(1);

    await userEvent.type(email, 'x');
    expect(within(form).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('keeps a newer invite draft, reports neutral success, and does not turn a failed refresh into invite failure', async () => {
    const invitationResponse = deferred();
    const invitations = [];
    let organizationReads = 0;
    server.use(antiforgery(), contextIs(platformContext()));
    server.use(http.get('/api/platform/organizations', () => {
      organizationReads += 1;
      return organizationReads === 1
        ? HttpResponse.json({ items: [ORGANIZATION], nextCursor: null })
        : problem(500, 'internal_server_error', { traceId: 'trace-after-invite' });
    }));
    server.use(...directories().slice(1));
    server.use(http.post('/api/platform/admins/invitations', async ({ request }) => {
      invitations.push(await request.json());
      return invitationResponse.promise;
    }));
    renderPanel();

    const form = await screen.findByRole('form', { name: 'Invite an administrator' });
    const email = within(form).getByLabelText('Invite an administrator');
    const invite = within(form).getByRole('button', { name: 'Invite' });
    await userEvent.type(email, 'first@example.test');
    await userEvent.click(invite);
    await waitFor(() => expect(invitations).toHaveLength(1));
    expect(invite).toBeDisabled();
    invite.click();
    fireEvent.submit(form);

    await userEvent.clear(email);
    await userEvent.type(email, 'next@example.test');
    invitationResponse.resolve(new HttpResponse(null, { status: 202 }));

    expect(await within(form).findByRole('status')).toHaveTextContent(
      'If that address can be invited, an invitation is on its way',
    );
    expect(email).toHaveValue('next@example.test');
    expect(await screen.findByText('Reference: trace-after-invite')).toBeInTheDocument();
    expect(within(form).queryByRole('alert')).not.toBeInTheDocument();
    expect(invitations).toEqual([{ email: 'first@example.test' }]);
  });

  it('treats a failed post-suspension refresh as a read error and closes the successful confirmation', async () => {
    let reads = 0;
    server.use(antiforgery(), contextIs(platformContext()));
    server.use(http.get('/api/platform/organizations', () => {
      reads += 1;
      return reads === 1
        ? HttpResponse.json({ items: [ORGANIZATION], nextCursor: null })
        : problem(500, 'internal_server_error', { traceId: 'trace-after-suspend' });
    }));
    server.use(...directories().slice(1));
    server.use(http.post('/api/platform/organizations/:tenantId/suspend', () => new HttpResponse(null, { status: 204 })));
    renderPanel();

    await userEvent.click(await screen.findByRole('button', { name: 'Suspend acme-1' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm suspension' }));

    expect(await screen.findByText('Reference: trace-after-suspend')).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Confirm suspension' })).not.toBeInTheDocument();
    expect(screen.getByText('acme-1')).toBeInTheDocument();
    expect(screen.getAllByRole('alert')).toHaveLength(1);
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

  it('offers a retry for a directory server failure and loads that section afterwards', async () => {
    let attempts = 0;
    server.use(antiforgery(), contextIs(platformContext()));
    server.use(http.get('/api/platform/organizations', () => {
      attempts += 1;
      return attempts === 1
        ? problem(500, 'internal_server_error', { traceId: 'trace-panel' })
        : HttpResponse.json({ items: [ORGANIZATION], nextCursor: null });
    }));
    server.use(...directories().slice(1));
    renderPanel();

    expect(await screen.findByText('Reference: trace-panel')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('acme-1')).toBeInTheDocument();
    expect(attempts).toBe(2);
  });

  it('keeps a displayed directory through a retryable refresh and its in-place retry', async () => {
    let attempts = 0;
    const retry = deferred();
    const refreshed = { ...ORGANIZATION, tenantId: 'tenant-2', slug: 'acme-2' };
    server.use(antiforgery(), contextIs(platformContext()));
    server.use(http.get('/api/platform/organizations', () => {
      attempts += 1;
      if (attempts === 1) {
        return HttpResponse.json({ items: [ORGANIZATION], nextCursor: 'next-organizations' });
      }
      if (attempts === 2) return problem(500, 'internal_server_error', { traceId: 'trace-refresh' });
      return retry.promise;
    }));
    server.use(...directories().slice(1));
    renderPanel();

    expect(await screen.findByText('acme-1')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'More organizations' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Something went wrong. Try again.');
    expect(alert).toHaveTextContent('Reference: trace-refresh');
    expect(screen.getByText('acme-1')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(attempts).toBe(3));
    const section = screen.getByRole('heading', { name: 'Organizations' }).parentElement;
    expect(section).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByText('acme-1')).toBeInTheDocument();

    retry.resolve(HttpResponse.json({ items: [refreshed], nextCursor: null }));

    expect(await screen.findByText('acme-2')).toBeInTheDocument();
    expect(section).toHaveAttribute('aria-busy', 'false');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
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

  it('associates and focuses an administrator invitation email detail without repeating it in the alert', async () => {
    server.use(antiforgery(), contextIs(platformContext()), ...directories());
    server.use(http.post('/api/platform/admins/invitations', () => problem(400, 'validation_failed', {
      errors: { email: [{ code: 'too_long', params: { max: 256 } }] },
    })));
    renderPanel();

    const email = await screen.findByRole('textbox', { name: 'Invite an administrator' });
    await userEvent.type(email, 'owner@example.test');
    await userEvent.click(screen.getByRole('button', { name: 'Invite' }));

    await waitFor(() => expect(email).toHaveAttribute('aria-invalid', 'true'));
    expect(email).toHaveAccessibleDescription('Must be at most 256 characters.');
    expect(email).toHaveFocus();
    expect(screen.getByRole('alert')).not.toHaveTextContent('Must be at most 256 characters.');
  });
});
