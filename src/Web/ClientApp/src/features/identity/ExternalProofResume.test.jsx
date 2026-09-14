import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../../App';
import { server } from '../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../test/identityServer';
import { externalNavigation } from './externalNavigation';

const TENANT = 'tenant-1';
const TARGET = 'membership-2';
/** The page size a screen asks for when nobody chose one. One row more than it is the smallest list that spans two pages. */
const PAGE_SIZE = 25;
const renderAt = (path) => render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>);

/** One offset page, exactly as the API answers it. */
const pageOf = (items, { pageNumber = 1, pageSize = PAGE_SIZE, totalCount = items.length } = {}) => {
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

/**
 * The same return journey as the device screen, on a different screen and a different operation. The seam that
 * carries an interrupted operation across the provider round trip is shared, so proving it once proves nothing
 * about whether it was wired everywhere it is used — this is the second consumer, chosen because it also carries
 * a target and a tenant, and so exercises the binding the device case does not.
 */
const owner = () => {
  const starts = [];
  const transfers = [];
  let completions = 0;
  server.use(
    antiforgery(),
    contextIs(signedInContext({
      permissions: ['members.read', 'members.manage', 'tenant.ownership.transfer', 'identity.external.manage'],
    })),
    http.get('/api/identity/credentials', () => HttpResponse.json({ hasPassword: false, passwordUpdatedAt: null })),
    http.get('/api/identity/external', () => HttpResponse.json({
      available: ['Google'],
      items: [{ handle: 'linked-google', provider: 'Google', providerEmail: 'owner@provider.test', linkedAt: '2026-09-01T00:00:00Z' }],
    })),
    http.get(`/api/tenants/${TENANT}/roles`, () => HttpResponse.json(pageOf([]))),
    http.get(`/api/tenants/${TENANT}/members`, () => HttpResponse.json(pageOf([
      { membershipId: 'membership-1', identityId: 'user-1', displayName: 'Ana', normalizedEmail: 'ana@example.test', status: 'Active', roleIds: [], isOwner: true, version: '11' },
      { membershipId: TARGET, identityId: 'identity-2', displayName: 'Bruno', normalizedEmail: 'bruno@example.test', status: 'Active', roleIds: [], isOwner: false, version: '22' },
    ]))),
    http.post('/api/identity/external/Google/proof/start', async ({ request }) => {
      starts.push(await request.json());
      return HttpResponse.json({ authorizationRequestUri: '/api/identity/external/Google/challenge' });
    }),
    http.post('/api/identity/external/complete', () => {
      completions += 1;
      return completions === 1 ? new HttpResponse(null, { status: 204 }) : problem(400, 'invalid_external_login');
    }),
    http.post(`/api/tenants/${TENANT}/ownership/transfer`, async ({ request }) => {
      transfers.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return { starts, transfers, completed: () => completions };
};

const beginTransfer = async (recorded) => {
  const page = renderAt('/members');
  await screen.findByText('Bruno');
  await userEvent.click(screen.getByRole('button', { name: 'Transfer ownership to Bruno' }));
  await waitFor(() => expect(recorded.starts).toEqual([{ action: 'tenant.ownership.transfer' }]));
  expect(recorded.transfers, 'nothing may be written before the provider has answered').toEqual([]);
  page.unmount();
};

// The target is the one row past the first page. It is reached through the real page control, then searched for
// again after a fresh return mount, which loads only the first page. `afterReturn` says what that search finds.
const paginatedTarget = (kind, afterReturn = 'eligible', waitForLookup = async () => {}) => {
  const recorded = owner();
  const edits = [];
  const reads = [];
  const parameters = new Set();
  const isMember = kind === 'members';
  const target = isMember
    ? { membershipId: TARGET, identityId: 'identity-2', displayName: 'Bruno', normalizedEmail: 'bruno@example.test', status: 'Active', roleIds: [], isOwner: false, version: '22' }
    : { roleId: 'role-target', name: 'Bookkeeper', permissions: ['members.read'], isSystem: false, isRetired: false, version: '44' };
  const firstPage = Array.from({ length: PAGE_SIZE }, (_, index) => isMember
    ? { ...target, membershipId: `other-member-${index}`, identityId: `other-identity-${index}`, displayName: `Member ${index}`, normalizedEmail: `member${index}@example.test`, status: 'Suspended' }
    : { ...target, roleId: `other-role-${index}`, name: `Role ${index}` });
  server.use(
    contextIs(signedInContext({ permissions: ['members.read', 'members.manage', 'roles.read', 'roles.manage', 'tenant.ownership.transfer', 'identity.external.manage'] })),
    http.get(`/api/tenants/${TENANT}/${kind}`, async ({ request }) => {
      const { searchParams } = new URL(request.url);
      searchParams.forEach((_, name) => parameters.add(name));
      const pageNumber = Number(searchParams.get('pageNumber'));
      const pageSize = Number(searchParams.get('pageSize'));
      const returned = recorded.completed() > 0;
      reads.push({ pageNumber, pageSize, returned });
      if (pageNumber === 1) return HttpResponse.json(pageOf(firstPage, { totalCount: PAGE_SIZE + 1 }));
      expect(pageNumber).toBe(2);
      if (returned) await waitForLookup();
      if (returned && afterReturn === 'unreachable') return HttpResponse.error();
      const current = returned ? { ...target, version: '99' } : target;
      if (returned && afterReturn === 'ineligible') {
        if (isMember) current.status = 'Suspended';
        else current.isRetired = true;
      }
      return HttpResponse.json(returned && afterReturn === 'missing'
        ? pageOf([], { pageNumber: 2, totalCount: PAGE_SIZE })
        : pageOf([current], { pageNumber: 2, totalCount: PAGE_SIZE + 1 }));
    }),
    http.get(`/api/tenants/${TENANT}/permission-catalog`, () => HttpResponse.json([
      { code: 'members.read', grantable: true }, { code: 'members.manage', grantable: true },
    ])),
    http.put(`/api/tenants/${TENANT}/roles/role-target`, async ({ request }) => {
      edits.push(await request.json());
      return HttpResponse.json({ ...target, ...edits.at(-1) });
    }),
  );
  return { ...recorded, edits, reads, parameters, kind, writes: isMember ? recorded.transfers : edits };
};

/** A control on the first row of the first page, which each screen disables while a change is under way. */
const firstRowAction = (kind) => (kind === 'members' ? 'Edit roles of Member 0' : 'Edit Role 0');

const beginPaginatedOperation = async (recorded) => {
  const page = renderAt(`/${recorded.kind}`);
  await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));
  if (recorded.kind === 'members') {
    await userEvent.click(await screen.findByRole('button', { name: 'Transfer ownership to Bruno' }));
  } else {
    await userEvent.click(await screen.findByRole('button', { name: 'Edit Bookkeeper' }));
    await userEvent.clear(screen.getByLabelText('Name'));
    await userEvent.type(screen.getByLabelText('Name'), 'Updated bookkeeper');
    await userEvent.click(screen.getByLabelText('members.manage'));
    await userEvent.click(screen.getByRole('button', { name: 'Save role' }));
  }
  await waitFor(() => expect(externalNavigation.leaveFor).toHaveBeenCalledOnce());
  expect(recorded.starts).toEqual([{ action: recorded.kind === 'members' ? 'tenant.ownership.transfer' : 'roles.change' }]);
  expect(recorded.writes).toEqual([]);
  expect(recorded.reads).toEqual([
    { pageNumber: 1, pageSize: PAGE_SIZE, returned: false },
    { pageNumber: 2, pageSize: PAGE_SIZE, returned: false },
  ]);
  page.unmount();
};

beforeEach(() => {
  vi.spyOn(window, 'confirm').mockReturnValue(true);
  vi.spyOn(externalNavigation, 'leaveFor').mockImplementation(() => {});
  window.sessionStorage.clear();
});
afterEach(() => vi.restoreAllMocks());

describe('resuming a sensitive operation after a provider round trip', () => {
  it('finishes the ownership transfer the person asked for, against the roster as it stands', async () => {
    const recorded = owner();
    await beginTransfer(recorded);

    renderAt('/external/return?outcome=proved');

    await waitFor(() => expect(recorded.transfers).toEqual([{ toMembershipId: TARGET, version: '22' }]));
    expect(await screen.findByRole('heading', { level: 1, name: 'Members' })).toBeInTheDocument();
    expect(recorded.completed()).toBe(1);
  });

  /** A return that says nothing was proved is a return that must leave the organization exactly as it was. */
  it('executes nothing when the provider round trip was refused', async () => {
    const recorded = owner();
    await beginTransfer(recorded);

    renderAt('/external/return?outcome=refused');

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign-in providers' })).toBeInTheDocument();
    expect(recorded.transfers).toEqual([]);
    expect(recorded.completed()).toBe(0);
  });

  /**
   * The record is forgotten before the request goes out, so coming back to the return address again — a refresh,
   * a back button, a replayed link — finds nothing waiting and hands the organization over exactly once.
   */
  it('does not hand the organization over twice when the return is replayed', async () => {
    const recorded = owner();
    await beginTransfer(recorded);

    const first = renderAt('/external/return?outcome=proved');
    await waitFor(() => expect(recorded.transfers).toHaveLength(1));
    first.unmount();

    renderAt('/external/return?outcome=proved');

    await waitFor(() => expect(recorded.completed()).toBe(2));
    expect(recorded.transfers).toEqual([{ toMembershipId: TARGET, version: '22' }]);
  });

  it.each(['members', 'roles'])('resumes a %s target beyond the first page exactly once with its original draft and version', async (kind) => {
    const recorded = paginatedTarget(kind);
    await beginPaginatedOperation(recorded);

    const returned = renderAt('/external/return?outcome=proved');

    const expected = kind === 'members'
      ? { toMembershipId: TARGET, version: '22' }
      : { name: 'Updated bookkeeper', permissions: ['members.read', 'members.manage'], version: '44' };
    await waitFor(() => expect(recorded.writes).toEqual([expected]));
    await waitFor(() => expect(screen.getByRole('button', { name: firstRowAction(kind) })).toBeEnabled());
    expect(screen.getByRole('button', { name: 'Go to next page' })).toBeEnabled();
    expect(recorded.reads).toContainEqual({ pageNumber: 2, pageSize: PAGE_SIZE, returned: true });
    expect([...recorded.parameters].sort(), 'pages are asked for by page number and page size alone').toEqual(['pageNumber', 'pageSize']);
    returned.unmount();

    renderAt('/external/return?outcome=proved');
    await waitFor(() => expect(recorded.completed()).toBe(2));
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(recorded.writes).toEqual([expected]);
  });

  /**
   * A resumed operation comes back to a freshly mounted screen: no editor is open and only page one is loaded,
   * so an alert scoped to the region that started the change has nowhere to be drawn. A refusal that reports
   * nothing is worse than one that reports badly, so the screen says it where the reader is.
   */
  it('reports a refused role change that was resumed after the provider round trip', async () => {
    const recorded = owner();
    server.use(
      http.get(`/api/tenants/${TENANT}/roles`, () => HttpResponse.json(pageOf([
        { roleId: 'role-1', name: 'Administrator', permissions: ['members.manage'], isSystem: false, isRetired: false, version: '33' },
      ]))),
      http.put(`/api/tenants/${TENANT}/members/${TARGET}/roles`, () => problem(409, 'last_administrator_required')),
    );

    const page = renderAt('/members');
    await userEvent.click(await screen.findByRole('button', { name: 'Edit roles of Bruno' }));
    await userEvent.click(screen.getByLabelText('Administrator'));
    await userEvent.click(screen.getByRole('button', { name: 'Save roles' }));
    await waitFor(() => expect(recorded.starts).toEqual([{ action: 'members.roles.change' }]));
    page.unmount();

    renderAt('/external/return?outcome=proved');

    expect(await screen.findByRole('alert'))
      .toHaveTextContent('This would leave the organization with no administrator. Give somebody else those permissions first.');
  });

  it.each([['members', 'missing'], ['roles', 'ineligible']])('does not resume when the %s target is %s after return', async (kind, state) => {
    const recorded = paginatedTarget(kind, state);
    await beginPaginatedOperation(recorded);

    renderAt('/external/return?outcome=proved');

    await waitFor(() => expect(recorded.reads).toContainEqual({ pageNumber: 2, pageSize: PAGE_SIZE, returned: true }));
    await waitFor(() => expect(window.sessionStorage.getItem('identity.pending-proof')).toBeNull());
    expect(recorded.writes).toEqual([]);
    expect(recorded.reads.filter(({ returned }) => returned), 'the loaded page is not read again, and nothing past the last page is asked for')
      .toEqual([{ pageNumber: 1, pageSize: PAGE_SIZE, returned: true }, { pageNumber: 2, pageSize: PAGE_SIZE, returned: true }]);
  });

  it('does not execute a late lookup after leaving the returned screen', async () => {
    let release;
    const lookup = new Promise((resolve) => { release = resolve; });
    const recorded = paginatedTarget('roles', 'eligible', () => lookup);
    await beginPaginatedOperation(recorded);

    const returned = renderAt('/external/return?outcome=proved');
    try {
      await waitFor(() => expect(recorded.reads).toContainEqual({ pageNumber: 2, pageSize: PAGE_SIZE, returned: true }));
      returned.unmount();
    } finally {
      await act(async () => { release(); await lookup; });
    }

    expect(recorded.writes).toEqual([]);
  });

  /**
   * A search that cannot reach the next page has not shown that the target is gone. The resume stops, the reason
   * is said where the reader is, and nothing is changed on a guess.
   */
  it.each(['members', 'roles'])('reports a %s search that cannot reach the next page, and executes nothing', async (kind) => {
    const recorded = paginatedTarget(kind, 'unreachable');
    await beginPaginatedOperation(recorded);

    renderAt('/external/return?outcome=proved');

    expect(await screen.findByRole('heading', { level: 1, name: kind === 'members' ? 'Members' : 'Roles' })).toBeInTheDocument();
    await waitFor(() => expect(recorded.reads).toContainEqual({ pageNumber: 2, pageSize: PAGE_SIZE, returned: true }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/^We could not reach the service\./);
    expect(recorded.writes).toEqual([]);
  });
});
