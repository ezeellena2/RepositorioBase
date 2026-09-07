import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { IdentityProvider } from '../context/IdentityProvider';
import { MembersPage } from './MembersPage';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const TENANT = 'tenant-1';

const renderPage = (context = signedInContext()) => {
  server.use(antiforgery(), contextIs(context));
  return render(<MemoryRouter><IdentityProvider><MembersPage /></IdentityProvider></MemoryRouter>);
};

const member = (overrides = {}) => ({
  membershipId: 'membership-1',
  identityId: 'identity-1',
  displayName: 'Ana',
  normalizedEmail: 'ana@example.test',
  status: 'Active',
  roleIds: ['role-1'],
  isOwner: false,
  version: '781',
  ...overrides,
});

const membersAre = (items) =>
  http.get(`/api/tenants/${TENANT}/members`, () => HttpResponse.json({ items, nextCursor: null }));

const rolesAre = (items) =>
  http.get(`/api/tenants/${TENANT}/roles`, () => HttpResponse.json({ items, nextCursor: null }));

const role = (overrides = {}) => ({
  roleId: 'role-1',
  name: 'Bookkeeper',
  isSystem: false,
  isRetired: false,
  permissions: ['members.read'],
  version: '1',
  ...overrides,
});

const proofAccepted = (spent) => http.post('/api/identity/credentials/reauthenticate', async ({ request }) => {
  spent.push(await request.json());
  return new HttpResponse(null, { status: 204 });
});

beforeEach(() => {
  vi.spyOn(window, 'confirm').mockReturnValue(true);
});

/**
 * Member administration from the browser. What this screen owes the person is legibility, not enforcement: the
 * ceiling, the floor and the owner rule are all decided by the server, and the screen's job is to offer what can
 * work and to show plainly what was refused.
 */
describe('members page', () => {
  it('lists who is in the organization, their state, and which one owns it', async () => {
    renderPage();
    server.use(membersAre([member(), member({ membershipId: 'membership-2', displayName: 'Bruno', isOwner: true })]));
    server.use(rolesAre([role()]));

    expect(await screen.findByRole('button', { name: 'Suspend Ana' })).toBeInTheDocument();
    expect(screen.getAllByText(/ana@example.test/).length).toBeGreaterThan(0);
    expect(screen.getByText((_, node) => node?.textContent?.trim() === '— owner')).toBeInTheDocument();
    expect(screen.getAllByText(/Bookkeeper/).length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Suspend Bruno' }))
      .toBeNull("the owner is listed but is not offered an ending");
  });

  /**
   * The owner rule made visible. An organization whose owner is not a member of it has nobody who can give it
   * away, so the screen does not offer the button at all rather than letting the server refuse it.
   */
  it('offers no way to suspend or remove the owner', async () => {
    renderPage();
    server.use(membersAre([member({ displayName: 'Bruno', isOwner: true })]));
    server.use(rolesAre([role()]));

    await screen.findByRole('button', { name: 'Edit roles of Bruno' });
    expect(screen.queryByRole('button', { name: 'Suspend Bruno' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove Bruno' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Transfer ownership/ })).not.toBeInTheDocument();
  });

  it('buys a proof before it changes which roles a member holds, and echoes the version it read', async () => {
    const spent = [];
    const changes = [];
    renderPage();
    server.use(membersAre([member()]), rolesAre([role(), role({ roleId: 'role-2', name: 'Auditor' })]), proofAccepted(spent));
    server.use(http.put(`/api/tenants/${TENANT}/members/membership-1/roles`, async ({ request }) => {
      changes.push(await request.json());
      return HttpResponse.json(member({ roleIds: ['role-1', 'role-2'], version: '782' }));
    }));

    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Edit roles of Ana' }));
    await userEvent.click(screen.getByLabelText('Auditor'));
    await userEvent.click(screen.getByRole('button', { name: 'Save roles' }));

    await waitFor(() => expect(changes).toHaveLength(1));
    expect(spent).toEqual([{ action: 'members.roles.change', password: 'Testing1234!' }]);
    expect(changes[0]).toEqual({ roleIds: ['role-1', 'role-2'], version: '781' });
  });

  it('suspends without asking for a password, because that change needs no proof', async () => {
    const changes = [];
    renderPage();
    server.use(membersAre([member()]), rolesAre([role()]));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => {
      throw new Error('a status change buys no proof');
    }));
    server.use(http.post(`/api/tenants/${TENANT}/members/membership-1/suspend`, async ({ request }) => {
      changes.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));

    await userEvent.click(await screen.findByRole('button', { name: 'Suspend Ana' }));

    await waitFor(() => expect(changes).toEqual([{ version: '781' }]));
  });

  it('offers reactivation only to somebody who is suspended', async () => {
    renderPage();
    server.use(membersAre([member({ status: 'Suspended' })]), rolesAre([role()]));

    expect(await screen.findByRole('button', { name: 'Reactivate Ana' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Suspend Ana' })).not.toBeInTheDocument();
  });

  it('offers nothing to somebody already removed, because there is no way back from here', async () => {
    renderPage();
    server.use(membersAre([member({ status: 'Revoked', roleIds: [] })]), rolesAre([role()]));

    await screen.findByText(/Revoked/);
    expect(screen.queryByRole('button', { name: 'Reactivate Ana' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove Ana' })).not.toBeInTheDocument();
  });

  /**
   * Only the server can count administrators, and it counts after the change it then rejects. The screen shows
   * that answer rather than trying to predict it.
   */
  it('shows the refusal when a change would leave nobody able to administer', async () => {
    renderPage();
    server.use(membersAre([member()]), rolesAre([role()]));
    server.use(http.post(`/api/tenants/${TENANT}/members/membership-1/revoke`, () => problem(409, 'last_administrator_required')));

    await userEvent.click(await screen.findByRole('button', { name: 'Remove Ana' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/no administrator/i);
    expect(screen.getByRole('button', { name: 'Remove Ana' })).toBeInTheDocument();
  });

  it('confirms before giving the organization away, and spends its own proof', async () => {
    const spent = [];
    const transfers = [];
    renderPage();
    server.use(membersAre([member()]), rolesAre([role()]), proofAccepted(spent));
    server.use(http.post(`/api/tenants/${TENANT}/ownership/transfer`, async ({ request }) => {
      transfers.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));

    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Transfer ownership to Ana' }));

    await waitFor(() => expect(transfers).toEqual([{ toMembershipId: 'membership-1', version: '781' }]));
    expect(window.confirm).toHaveBeenCalled();
    expect(spent).toEqual([{ action: 'tenant.ownership.transfer', password: 'Testing1234!' }]);
  });

  it('transfers nothing when the confirmation is declined', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    renderPage();
    server.use(membersAre([member()]), rolesAre([role()]));
    server.use(http.post(`/api/tenants/${TENANT}/ownership/transfer`, () => {
      throw new Error('ownership must not move without a deliberate yes');
    }));

    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Transfer ownership to Ana' }));

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('says so rather than failing when the session is in no organization', async () => {
    renderPage(signedInContext({ activeTenant: null }));

    expect(await screen.findByText(/choose an organization first/i)).toBeInTheDocument();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
  });
});
