import { describe, expect, it, vi } from 'vitest';
import { ApiProblem, ClientFailure } from './apiTransport';
import { boundedPage, paginationMembers, paginationSearch, readEveryPage, readPage, sendPage } from './pagination';

/**
 * Paging arguments mirror PaginationQuery on the server: a missing or non-integer member takes the default page,
 * and an integer is clamped rather than refused (PD-1, AD12). The server clamps again, so these bounds only keep
 * the client from asking for something the contract would have to correct.
 */
describe('offset paging arguments', () => {
  it('clamps an out-of-range page number and page size into the contract', () => {
    expect(paginationSearch({ pageNumber: 0, pageSize: 500 }).toString()).toBe('pageNumber=1&pageSize=100');
  });

  it('keeps an in-range page exactly as asked', () => {
    expect(paginationSearch({ pageNumber: 3, pageSize: 50 }).toString()).toBe('pageNumber=3&pageSize=50');
  });

  it.each([
    ['a zero page size', { pageNumber: 1, pageSize: 0 }],
    ['a negative page size', { pageSize: -5 }],
  ])('reads %s as one row per page, never as the default', (_label, page) => {
    expect(paginationSearch(page).toString()).toBe('pageNumber=1&pageSize=1');
  });

  it.each([
    ['a non-numeric page number', { pageNumber: 'abc' }],
    ['a null page', null],
    ['an undefined page', undefined],
  ])('reads %s as the default first page', (_label, page) => {
    expect(paginationSearch(page).toString()).toBe('pageNumber=1&pageSize=25');
  });

  it('bounds a null page without failing, as the role pickers pass one', () => {
    expect(boundedPage(null)).toEqual({ pageNumber: 1, pageSize: 25 });
  });

  it('names the seven members every offset page declares', () => {
    expect(paginationMembers).toEqual([
      'items',
      'pageNumber',
      'pageSize',
      'totalCount',
      'totalPages',
      'hasPreviousPage',
      'hasNextPage',
    ]);
  });
});

const emptyCollection = {
  items: [],
  pageNumber: 1,
  pageSize: 25,
  totalCount: 0,
  totalPages: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

const lastOfTwoPages = {
  items: [{ roleId: 'role-26', name: 'Auditor' }],
  pageNumber: 2,
  pageSize: 25,
  totalCount: 26,
  totalPages: 2,
  hasPreviousPage: true,
  hasNextPage: false,
};

/**
 * A page is read against its declared metadata, not merely its member names (E7). Metadata the contract cannot
 * produce is drift: the reader refuses it with an English developer message, and the caller turns that into
 * `unreadable_response`, whose words come from the catalogue.
 */
describe('strict offset page reader', () => {
  it.each([
    ['a missing total', { items: [], pageNumber: 1, pageSize: 25, totalPages: 0, hasPreviousPage: false, hasNextPage: false }],
    ['a fractional page', { ...emptyCollection, pageNumber: 1.5 }],
    ['a page size of 500', { ...emptyCollection, pageSize: 500 }],
    ['string flags', { ...emptyCollection, hasPreviousPage: 'false' }],
    ['a page number of 0', { ...emptyCollection, pageNumber: 0 }],
    ['a negative total of pages', { ...emptyCollection, totalPages: -1 }],
  ])('refuses %s', (_label, body) => {
    expect(() => readPage(body)).toThrow(/offset pagination contract/);
  });

  it.each([
    ['a page with rows', lastOfTwoPages],
    ['an empty collection', emptyCollection],
  ])('returns %s unchanged', (_label, body) => {
    expect(readPage(body)).toBe(body);
  });
});

/**
 * One classification for every paged read (AD11, error rule 16): drift in the page becomes `unreadable_response`,
 * and whatever the transport already classified reaches the caller untouched.
 */
describe('paged read', () => {
  it('asks the transport for the bounded page and expects exactly the offset members', async () => {
    const signal = new AbortController().signal;
    const send = vi.fn().mockResolvedValue(emptyCollection);

    await expect(sendPage(send, '/api/tenants/tenant-1/roles', null, { signal })).resolves.toBe(emptyCollection);

    expect(send).toHaveBeenCalledOnce();
    expect(send).toHaveBeenCalledWith('/api/tenants/tenant-1/roles?pageNumber=1&pageSize=25', {
      expect: paginationMembers,
      signal,
    });
  });

  it('sends the page it was asked for', async () => {
    const send = vi.fn().mockResolvedValue(lastOfTwoPages);

    await expect(sendPage(send, '/api/platform/audit', { pageNumber: 2, pageSize: 25 })).resolves.toBe(lastOfTwoPages);

    expect(send).toHaveBeenCalledWith('/api/platform/audit?pageNumber=2&pageSize=25', {
      expect: paginationMembers,
      signal: undefined,
    });
  });

  it('rejects malformed page metadata as an unreadable response', async () => {
    const send = vi.fn().mockResolvedValue({ ...emptyCollection, pageNumber: 0 });

    const failure = await sendPage(send, '/api/tenants/tenant-1/roles', null).catch((candidate) => candidate);

    expect(failure).toBeInstanceOf(ClientFailure);
    expect(failure.problem).toEqual({ code: 'unreadable_response', status: 0 });
  });

  it.each([
    ['a problem document', new ApiProblem({ status: 403, code: 'permission_denied', traceId: 'trace-403' })],
    ['a client failure', new ClientFailure('network_unavailable')],
  ])('passes %s from the transport through unchanged', async (_label, failure) => {
    const send = vi.fn().mockRejectedValue(failure);

    await expect(sendPage(send, '/api/platform/organizations', null)).rejects.toBe(failure);
  });
});

const SERVED_PAGE_SIZE = 25;
const role = (index) => ({ roleId: `role-${String(index).padStart(2, '0')}`, name: `Role ${index}` });
const roleIds = (roles) => roles.map((item) => item.roleId);
const servedPage = (items, pageNumber, { totalCount, hasNextPage }) => ({
  items,
  pageNumber,
  pageSize: SERVED_PAGE_SIZE,
  totalCount,
  totalPages: Math.ceil(totalCount / SERVED_PAGE_SIZE),
  hasPreviousPage: pageNumber > 1,
  hasNextPage,
});

/** Thirty roles over two served pages. A row that shifts between the two reads appears on both, as a fresh object. */
const thirtyRoles = Array.from({ length: 30 }, (_, index) => role(index + 1));
const thirtyRolePages = {
  1: servedPage(thirtyRoles.slice(0, 25), 1, { totalCount: 30, hasNextPage: true }),
  2: servedPage([role(25), ...thirtyRoles.slice(25)], 2, { totalCount: 30, hasNextPage: false }),
};

/**
 * A whole collection, such as the role catalogue a picker offers, is read by walking `pageNumber` until an answer
 * says there is no next page (PD-2, AD13). The walk is bounded: a collection that never ends is drift, never an
 * endless loop.
 */
describe('whole-collection walk', () => {
  it('reads every page at the largest page size, keeps one row per key and stops at the last page', async () => {
    const readOne = vi.fn(async (page) => thirtyRolePages[page.pageNumber]);

    const catalogue = await readEveryPage(readOne, (item) => item.roleId);

    expect(readOne.mock.calls.map(([page]) => page)).toEqual([
      { pageNumber: 1, pageSize: 100 },
      { pageNumber: 2, pageSize: 100 },
    ]);
    expect(roleIds(catalogue)).toEqual(roleIds(thirtyRoles));
  });

  it('needs one read for a collection that fits on one page', async () => {
    const readOne = vi.fn().mockResolvedValue(servedPage([role(1), role(2), role(3)], 1, { totalCount: 3, hasNextPage: false }));

    const catalogue = await readEveryPage(readOne, (item) => item.roleId);

    expect(readOne).toHaveBeenCalledOnce();
    expect(roleIds(catalogue)).toEqual(['role-01', 'role-02', 'role-03']);
  });

  it('gives up after 100 pages that all claim a next page, as an unreadable response', async () => {
    const readOne = vi.fn(async (page) =>
      servedPage([role(page.pageNumber)], page.pageNumber, { totalCount: 10_000, hasNextPage: true }));

    const failure = await readEveryPage(readOne, (item) => item.roleId).catch((candidate) => candidate);

    expect(readOne).toHaveBeenCalledTimes(100);
    expect(readOne.mock.calls.at(-1)[0]).toEqual({ pageNumber: 100, pageSize: 100 });
    expect(failure).toBeInstanceOf(ClientFailure);
    expect(failure.problem).toEqual({ code: 'unreadable_response', status: 0 });
  });

  it('forwards the caller signal to every read', async () => {
    const signal = new AbortController().signal;
    const readOne = vi.fn(async (page) => thirtyRolePages[page.pageNumber]);

    await readEveryPage(readOne, (item) => item.roleId, { signal });

    expect(readOne).toHaveBeenCalledTimes(2);
    readOne.mock.calls.forEach(([, options]) => expect(options.signal).toBe(signal));
  });
});
