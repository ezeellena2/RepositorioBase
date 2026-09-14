import { describe, expect, it, vi } from 'vitest';
import { ClientFailure } from '../../../api/apiTransport';
import { paginationMembers } from '../../../api/pagination';
import { createIdentityClient } from './identityClient';

const SERVED_PAGE_SIZE = 25;

const offsetPage = (items, pageNumber, { totalCount, hasNextPage }) => ({
  items,
  pageNumber,
  pageSize: SERVED_PAGE_SIZE,
  totalCount,
  totalPages: Math.ceil(totalCount / SERVED_PAGE_SIZE),
  hasPreviousPage: pageNumber > 1,
  hasNextPage,
});

const emptyPage = offsetPage([], 1, { totalCount: 0, hasNextPage: false });

describe('identity client read cancellation', () => {
  it('forwards a caller signal through every effect-owned P2-6 read', async () => {
    const signal = new AbortController().signal;
    const send = vi.fn().mockResolvedValue(emptyPage);
    const client = createIdentityClient({ send });

    await client.listSessions({ signal });
    await client.getOwnCredentials({ signal });
    await client.listExternalLinks({ signal });
    await client.listPermissionCatalog('tenant-1', { signal });
    await client.listRoles('tenant-1', null, { signal });
    await client.listMembers('tenant-1', null, { signal });
    await client.listTenantInvitations('tenant-1', null, { signal });

    expect(send).toHaveBeenCalledTimes(7);
    expect(send.mock.calls.every(([, options]) => options.signal === signal)).toBe(true);
  });
});

const tenantLists = [
  ['listRoles', 'roles'],
  ['listMembers', 'members'],
  ['listTenantInvitations', 'invitations'],
];

/**
 * The three tenant lists read one offset page each (IA-REQ-045). The page a caller asks for is bounded before it
 * is sent, a missing page is the default first page (D10), and a page whose metadata the contract cannot produce is
 * drift, reported as an unreadable response rather than read as data or as a generic failure (E7).
 */
describe('identity client offset lists', () => {
  it.each(tenantLists)('%s asks for the requested page of the tenant %s, expecting the offset members', async (method, collection) => {
    const signal = new AbortController().signal;
    const send = vi.fn().mockResolvedValue(emptyPage);

    await createIdentityClient({ send })[method]('tenant-1', { pageNumber: 2, pageSize: 50 }, { signal });

    expect(send).toHaveBeenCalledOnce();
    expect(send).toHaveBeenCalledWith(`/api/tenants/tenant-1/${collection}?pageNumber=2&pageSize=50`, {
      expect: paginationMembers,
      signal,
    });
  });

  it.each(tenantLists)('%s reads a null page as the default first page of the tenant %s', async (method, collection) => {
    const send = vi.fn().mockResolvedValue(emptyPage);

    await createIdentityClient({ send })[method]('tenant-1', null);

    expect(send.mock.calls[0][0]).toBe(`/api/tenants/tenant-1/${collection}?pageNumber=1&pageSize=25`);
  });

  it.each(tenantLists)('%s returns a well-formed page exactly as it was answered', async (method) => {
    const answered = offsetPage([{ id: 'row-26' }], 2, { totalCount: 26, hasNextPage: false });
    const send = vi.fn().mockResolvedValue(answered);

    await expect(createIdentityClient({ send })[method]('tenant-1', { pageNumber: 2, pageSize: 25 })).resolves.toBe(answered);
  });

  it.each(tenantLists)('%s reports a page number of 0 as an unreadable response, not as data or a generic failure', async (method) => {
    const send = vi.fn().mockResolvedValue({ ...emptyPage, pageNumber: 0 });

    const failure = await createIdentityClient({ send })[method]('tenant-1', { pageNumber: 1, pageSize: 25 })
      .catch((candidate) => candidate);

    expect(failure).toBeInstanceOf(ClientFailure);
    expect(failure.problem).toEqual({ code: 'unreadable_response', status: 0 });
  });
});

const role = (index) => ({ roleId: `role-${String(index).padStart(2, '0')}`, name: `Role ${index}`, isRetired: false });
const roleIds = (roles) => roles.map((item) => item.roleId);
const requestedPage = (url) => Number(new URL(url, 'http://localhost').searchParams.get('pageNumber'));

/**
 * A role picker offers every role, so the catalogue walks `pageNumber` until an answer has no next page (PD-2,
 * AD13). The server may serve fewer rows than asked for; the walk follows `hasNextPage`, not the page size.
 */
describe('identity client role catalogue', () => {
  it('walks every roles page until the answer has no next page, and offers each role once', async () => {
    const signal = new AbortController().signal;
    const thirty = Array.from({ length: 30 }, (_, index) => role(index + 1));
    const pages = {
      1: offsetPage(thirty.slice(0, 25), 1, { totalCount: 30, hasNextPage: true }),
      2: offsetPage([role(25), ...thirty.slice(25)], 2, { totalCount: 30, hasNextPage: false }),
    };
    const send = vi.fn(async (url) => pages[requestedPage(url)]);

    const catalogue = await createIdentityClient({ send }).listRoleCatalogue('tenant-1', { signal });

    expect(send.mock.calls.map(([url]) => url)).toEqual([
      '/api/tenants/tenant-1/roles?pageNumber=1&pageSize=100',
      '/api/tenants/tenant-1/roles?pageNumber=2&pageSize=100',
    ]);
    expect(send.mock.calls.every(([, options]) => options.signal === signal && options.expect === paginationMembers)).toBe(true);
    expect(roleIds(catalogue)).toEqual(roleIds(thirty));
  });

  it('needs one request for a catalogue that fits on one page', async () => {
    const send = vi.fn().mockResolvedValue(offsetPage([role(1), role(2), role(3)], 1, { totalCount: 3, hasNextPage: false }));

    const catalogue = await createIdentityClient({ send }).listRoleCatalogue('tenant-1');

    expect(send).toHaveBeenCalledOnce();
    expect(roleIds(catalogue)).toEqual(['role-01', 'role-02', 'role-03']);
  });
});
