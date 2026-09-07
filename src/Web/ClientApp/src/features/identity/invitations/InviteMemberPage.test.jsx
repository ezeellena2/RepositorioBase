import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { IdentityProvider } from '../context/IdentityProvider';
import { InviteMemberPage } from './InviteMemberPage';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const TENANT = 'tenant-1';

const renderPage = (context = signedInContext()) => {
  server.use(antiforgery(), contextIs(context));
  return render(<MemoryRouter><IdentityProvider><InviteMemberPage /></IdentityProvider></MemoryRouter>);
};

const role = (overrides = {}) => ({
  roleId: 'role-1',
  name: 'Bookkeeper',
  isSystem: false,
  isRetired: false,
  permissions: ['members.read'],
  version: '1',
  ...overrides,
});

const invitation = (overrides = {}) => ({
  invitationId: 'invitation-1',
  normalizedEmail: 'nuevo@example.test',
  status: 'Pending',
  createdAt: '2026-09-01T00:00:00Z',
  expiresAt: '2026-09-08T00:00:00Z',
  roleIds: ['role-1'],
  ...overrides,
});

const rolesAre = (items) =>
  http.get(`/api/tenants/${TENANT}/roles`, () => HttpResponse.json({ items, nextCursor: null }));

const invitationsAre = (items) =>
  http.get(`/api/tenants/${TENANT}/invitations`, () => HttpResponse.json({ items, nextCursor: null }));

beforeEach(() => {
  vi.spyOn(window, 'confirm').mockReturnValue(true);
});

/**
 * Making an offer and living with it afterwards. No token appears anywhere on this screen by contract: issuing
 * answers with an identifier and an expiry, reissuing answers with nothing at all, and the only usable
 * credential reaches the recipient by email (IA-REQ-015/018).
 */
describe('invite member page', () => {
  it('offers the organization roles by name instead of asking for identifiers', async () => {
    renderPage();
    server.use(rolesAre([role(), role({ roleId: 'role-2', name: 'Auditor', isRetired: true })]), invitationsAre([]));

    expect(await screen.findByLabelText('Bookkeeper')).toBeInTheDocument();
    expect(screen.queryByLabelText('Auditor')).toBeNull('a retired role cannot be offered');
    expect(screen.queryByLabelText(/identifier/i)).toBeNull();
  });

  it('sends the invitation with the roles that were ticked, and says when it expires', async () => {
    const issued = [];
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([]));
    server.use(http.post(`/api/tenants/${TENANT}/invitations`, async ({ request }) => {
      issued.push(await request.json());
      return HttpResponse.json({ invitationId: 'invitation-9', expiresAt: '2026-09-08T00:00:00Z' }, { status: 201 });
    }));

    await userEvent.type(await screen.findByLabelText('Email'), 'nuevo@example.test');
    await userEvent.click(screen.getByLabelText('Bookkeeper'));
    await userEvent.click(screen.getByRole('button', { name: 'Send invitation' }));

    await waitFor(() => expect(issued).toEqual([{ email: 'nuevo@example.test', roleIds: ['role-1'] }]));
    expect(await screen.findByRole('status')).toHaveTextContent(/expires on/i);
    expect(screen.getByLabelText('Email')).toHaveValue('');
  });

  it('lists standing offers with their state, and shows the role by name', async () => {
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([invitation()]));

    expect(await screen.findByRole('button', { name: /Resend to nuevo@example.test/ })).toBeInTheDocument();
    expect(screen.getAllByText(/Bookkeeper/).length).toBeGreaterThan(0);
    expect(screen.getByText(/Pending/)).toBeInTheDocument();
  });

  it('reissues a standing offer and asks the server for the list again', async () => {
    let listed = 0;
    const resent = [];
    renderPage();
    server.use(rolesAre([role()]));
    server.use(http.get(`/api/tenants/${TENANT}/invitations`, () => {
      listed += 1;
      return HttpResponse.json({ items: [invitation()], nextCursor: null });
    }));
    server.use(http.post(`/api/tenants/${TENANT}/invitations/invitation-1/resend`, () => {
      resent.push('invitation-1');
      return new HttpResponse(null, { status: 204 });
    }));

    await userEvent.click(await screen.findByRole('button', { name: /Resend to nuevo@example.test/ }));

    await waitFor(() => expect(resent).toEqual(['invitation-1']));
    await waitFor(() => expect(listed).toBeGreaterThan(1));
  });

  /**
   * Withdrawing ends somebody's way in, so it is asked for deliberately. A declined confirmation must reach no
   * endpoint at all — not a refused one.
   */
  it('confirms before withdrawing an offer', async () => {
    const cancelled = [];
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([invitation()]));
    server.use(http.post(`/api/tenants/${TENANT}/invitations/invitation-1/cancel`, () => {
      cancelled.push('invitation-1');
      return new HttpResponse(null, { status: 204 });
    }));

    await userEvent.click(await screen.findByRole('button', { name: /Withdraw invitation to nuevo@example.test/ }));

    await waitFor(() => expect(cancelled).toEqual(['invitation-1']));
    expect(window.confirm).toHaveBeenCalled();
  });

  it('withdraws nothing when the confirmation is declined', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([invitation()]));
    server.use(http.post(`/api/tenants/${TENANT}/invitations/invitation-1/cancel`, () => {
      throw new Error('an offer must not be withdrawn without a deliberate yes');
    }));

    await userEvent.click(await screen.findByRole('button', { name: /Withdraw invitation to nuevo@example.test/ }));

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('offers nothing to do to an offer already taken or already withdrawn', async () => {
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([
      invitation({ status: 'Accepted' }),
      invitation({ invitationId: 'invitation-2', normalizedEmail: 'otro@example.test', status: 'Cancelled' }),
    ]));

    await screen.findByText(/Accepted/);
    expect(screen.queryByRole('button', { name: /Resend/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Withdraw/ })).not.toBeInTheDocument();
  });

  it('shows the refusal the server answered with', async () => {
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([invitation()]));
    server.use(http.post(`/api/tenants/${TENANT}/invitations/invitation-1/resend`, () => problem(409, 'invitation_conflict')));

    await userEvent.click(await screen.findByRole('button', { name: /Resend to nuevo@example.test/ }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/current state/i);
  });

  /**
   * Inviting and reading the roster are different permissions, so an inviter who holds only the first still gets
   * a usable form. What they cannot see is said plainly rather than left as an empty list.
   */
  it('still invites when it may not read the roles or the standing offers', async () => {
    renderPage();
    server.use(http.get(`/api/tenants/${TENANT}/roles`, () => problem(403, 'permission_denied')));
    server.use(http.get(`/api/tenants/${TENANT}/invitations`, () => problem(403, 'permission_denied')));

    expect(await screen.findByText(/cannot see this organization’s invitations/i)).toBeInTheDocument();
    expect(screen.getByText(/cannot see this organization’s roles/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send invitation' })).toBeEnabled();
  });

  it('says so rather than failing when the session is in no organization', async () => {
    renderPage(signedInContext({ activeTenant: null }));

    expect(await screen.findByText(/choose an organization first/i)).toBeInTheDocument();
    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
  });
});
