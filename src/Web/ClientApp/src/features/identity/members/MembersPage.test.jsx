import { render, screen, waitFor, within } from '@testing-library/react';
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

/** One offset page, exactly as the API answers it. */
const pageOf = (items, { pageNumber = 1, pageSize = 25, totalCount = items.length } = {}) => {
  const totalPages = totalCount === 0 ? 0 : Math.ceil(totalCount / pageSize);
  return {
    items,
    pageNumber,
    pageSize,
    totalCount,
    totalPages,
    hasPreviousPage: pageNumber > 1 && totalPages > 0,
    hasNextPage: pageNumber < totalPages,
  };
};

const membersAre = (items) =>
  http.get(`/api/tenants/${TENANT}/members`, () => HttpResponse.json(pageOf(items)));

const rolesAre = (items) =>
  http.get(`/api/tenants/${TENANT}/roles`, () => HttpResponse.json(pageOf(items)));

/** The API's side of the roster: every member served a page at a time, with each query string it was asked with. */
const membersServed = (all) => {
  const searches = [];
  server.use(http.get(`/api/tenants/${TENANT}/members`, ({ request }) => {
    const { search, searchParams } = new URL(request.url);
    searches.push(search);
    const pageNumber = Number(searchParams.get('pageNumber'));
    const pageSize = Number(searchParams.get('pageSize'));
    return HttpResponse.json(pageOf(
      all.slice((pageNumber - 1) * pageSize, pageNumber * pageSize),
      { pageNumber, pageSize, totalCount: all.length },
    ));
  }));
  return searches;
};

/**
 * Roles served at most 25 to a page, whatever page size was asked for, as a server with a smaller cap would. Each
 * page number asked for is recorded, so a picker that stops after one page is caught.
 */
const rolesServedTwentyFiveAtATime = (all) => {
  const pageNumbers = [];
  server.use(http.get(`/api/tenants/${TENANT}/roles`, ({ request }) => {
    const pageNumber = Number(new URL(request.url).searchParams.get('pageNumber'));
    pageNumbers.push(pageNumber);
    return HttpResponse.json(pageOf(
      all.slice((pageNumber - 1) * 25, pageNumber * 25),
      { pageNumber, pageSize: 25, totalCount: all.length },
    ));
  }));
  return pageNumbers;
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

const numberedRoles = (count) => Array.from({ length: count }, (_, index) => role({
  roleId: `role-${index + 1}`,
  name: `Role ${index + 1}`,
}));

const numberedMembers = (count) => Array.from({ length: count }, (_, index) => member({
  membershipId: `membership-${index + 1}`,
  identityId: `identity-${index + 1}`,
  displayName: `Member ${index + 1}`,
  normalizedEmail: `member${index + 1}@example.test`,
}));

const proofAccepted = (spent) => http.post('/api/identity/credentials/reauthenticate', async ({ request }) => {
  spent.push(await request.json());
  return new HttpResponse(null, { status: 204 });
});

const deferred = () => {
  let resolve;
  const promise = new Promise((settle) => { resolve = settle; });
  return { promise, resolve };
};

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

    const memberRow = screen.getByRole('button', { name: 'Remove Ana' }).closest('li');
    const alert = await within(memberRow).findByRole('alert');
    expect(alert).toHaveTextContent(/no administrator/i);
    expect(alert).toHaveFocus();
    expect(screen.getAllByRole('alert')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Remove Ana' })).toBeInTheDocument();
  });

  it('does not flash role identifiers and places a catalog refusal inside the member editor', async () => {
    server.use(
      membersAre([member({ roleIds: ['private-role-id'] })]),
      http.get(`/api/tenants/${TENANT}/roles`, () => problem(403, 'permission_denied')),
    );
    renderPage();

    const edit = await screen.findByRole('button', { name: 'Edit roles of Ana' });
    expect(screen.queryByText('private-role-id')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();

    await userEvent.click(edit);
    const editor = screen.getByRole('group', { name: 'Roles for Ana' }).closest('form');
    expect(await within(editor).findByText(/cannot see this organization’s roles/i)).toBeInTheDocument();
    expect(within(editor).queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('replaces an initial roster wait with a retryable error and loads members after Try again', async () => {
    let attempts = 0;
    server.use(
      http.get(`/api/tenants/${TENANT}/members`, () => {
        attempts += 1;
        return attempts === 1
          ? problem(500, 'internal_server_error', { traceId: 'trace-members' })
          : HttpResponse.json(pageOf([member()]));
      }),
      rolesAre([role()]),
    );
    renderPage();

    expect(await screen.findByText('Reference: trace-members')).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('button', { name: 'Suspend Ana' })).toBeInTheDocument();
    expect(attempts).toBe(2);
  });

  it('ends the page skeleton when the roster fails even while the catalog is still loading', async () => {
    const catalog = deferred();
    server.use(
      http.get(`/api/tenants/${TENANT}/members`, () => (
        problem(500, 'internal_server_error', { traceId: 'trace-roster-terminal' })
      )),
      http.get(`/api/tenants/${TENANT}/roles`, async () => {
        await catalog.promise;
        return HttpResponse.json(pageOf([role()]));
      }),
    );
    renderPage();

    expect(await screen.findByText('Reference: trace-roster-terminal')).toBeInTheDocument();
    const statusWhileCatalogLoads = screen.queryByRole('status');
    catalog.resolve();
    await waitFor(() => expect(screen.queryByRole('status')).not.toBeInTheDocument());

    expect(statusWhileCatalogLoads).toBeNull();
  });

  it('keeps the loaded roster visible while a failed catalog retries inside the editor', async () => {
    let catalogAttempts = 0;
    const retry = deferred();
    server.use(
      membersAre([member()]),
      http.get(`/api/tenants/${TENANT}/roles`, async () => {
        catalogAttempts += 1;
        if (catalogAttempts === 1) {
          return problem(500, 'internal_server_error', { traceId: 'trace-member-catalog' });
        }
        await retry.promise;
        return HttpResponse.json(pageOf([role()]));
      }),
    );
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Edit roles of Ana' }));
    const editor = screen.getByRole('group', { name: 'Roles for Ana' }).closest('form');
    await userEvent.click(await within(editor).findByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(catalogAttempts).toBe(2));

    const rosterStayedVisible = screen.queryByRole('button', { name: 'Suspend Ana' });
    const pageStatusDuringRetry = screen.queryByRole('status');
    retry.resolve();
    expect(await screen.findByLabelText('Bookkeeper')).toBeInTheDocument();

    expect(rosterStayedVisible).toBeInTheDocument();
    expect(pageStatusDuringRetry).toBeNull();
  });

  it('reaches the 26th member by page number', async () => {
    server.use(rolesAre([role()]));
    const searches = membersServed(numberedMembers(26));
    renderPage();

    expect(await screen.findByText('1–25 of 26')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Suspend Member 25' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Go to next page' }));

    expect(await screen.findByText('26–26 of 26')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Suspend Member 26' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Suspend Member 1' })).toBeNull();
    expect(searches).toEqual(['?pageNumber=1&pageSize=25', '?pageNumber=2&pageSize=25']);
  });

  /**
   * The editor hands somebody roles by name, so it has to know every role the organization has, not the first
   * page of them. Pages are read until the server says there is no next one, and not a request more.
   */
  it('offers every role in the editor when the organization has more roles than one page carries', async () => {
    server.use(membersAre([member()]));
    const rolePages = rolesServedTwentyFiveAtATime(numberedRoles(30));
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Edit roles of Ana' }));
    const editor = screen.getByRole('group', { name: 'Roles for Ana' });

    expect(await within(editor).findByLabelText('Role 30')).toBeInTheDocument();
    expect(within(editor).getAllByRole('checkbox')).toHaveLength(30);
    expect(rolePages).toEqual([1, 2]);
  });

  it('reads the roles with one request when they fit on one page', async () => {
    server.use(membersAre([member()]));
    const rolePages = rolesServedTwentyFiveAtATime(numberedRoles(3));
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Edit roles of Ana' }));
    const editor = screen.getByRole('group', { name: 'Roles for Ana' });

    expect(await within(editor).findByLabelText('Role 3')).toBeInTheDocument();
    expect(within(editor).getAllByRole('checkbox')).toHaveLength(3);
    expect(rolePages).toEqual([1]);
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
