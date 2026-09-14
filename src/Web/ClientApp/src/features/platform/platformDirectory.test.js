import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { createApiTransport } from '../../api/apiTransport';
import { createPlatformClient } from './api/platformClient';
import { server } from '../../test/server';

const client = () => createPlatformClient(createApiTransport());

const directory = (path, respond) => {
  const seen = { urls: [] };
  server.use(http.get(path, ({ request }) => {
    seen.urls.push(new URL(request.url));
    return respond(seen.urls.length);
  }));
  return seen;
};

const DEFAULT_PAGE_SIZE = 25;

/** A full offset page, with the metadata the server derives from the requested page and the total. */
const page = (items, { pageNumber = 1, pageSize = DEFAULT_PAGE_SIZE, totalCount = items.length } = {}) => {
  const totalPages = Math.ceil(totalCount / pageSize);
  return HttpResponse.json({
    items,
    pageNumber,
    pageSize,
    totalCount,
    totalPages,
    hasPreviousPage: pageNumber > 1,
    hasNextPage: pageNumber < totalPages,
  });
};

/**
 * The directory half of IA-REQ-045: four distinct typed resources, each read one bounded offset page at a time.
 * The client never invents a filter, and never asks for a page size outside the bounds the API declares — sending
 * one would only be clamped, and a client that relies on being clamped is one that has stopped agreeing with the
 * contract.
 */
describe('platform directories', () => {
  it('asks for the default first page and no filter', async () => {
    const seen = directory('/api/platform/organizations', () => page([]));

    await client().listOrganizations();

    expect(seen.urls[0].searchParams.get('pageNumber')).toBe('1');
    expect(seen.urls[0].searchParams.get('pageSize')).toBe('25');
    expect([...seen.urls[0].searchParams.keys()]).toEqual(['pageNumber', 'pageSize']);
  });

  it('never asks for more than the maximum or less than the minimum', async () => {
    const seen = directory('/api/platform/identities', () => page([]));

    await client().listIdentities({ pageNumber: 1, pageSize: 10_000 });
    await client().listIdentities({ pageNumber: -3, pageSize: 0 });

    expect(seen.urls.map((url) => url.searchParams.get('pageSize'))).toEqual(['100', '1']);
    expect(seen.urls.map((url) => url.searchParams.get('pageNumber'))).toEqual(['1', '1']);
  });

  it('asks for the page it was given and reads where that page sits', async () => {
    const seen = directory('/api/platform/admins', (call) =>
      (call === 1
        ? page([{ membershipId: 'm-1' }], { pageNumber: 1, pageSize: 1, totalCount: 2 })
        : page([{ membershipId: 'm-2' }], { pageNumber: 2, pageSize: 1, totalCount: 2 })));

    const first = await client().listAdministrators({ pageNumber: 1, pageSize: 1 });
    const second = await client().listAdministrators({ pageNumber: first.pageNumber + 1, pageSize: first.pageSize });

    expect(seen.urls.map((url) => url.search)).toEqual(['?pageNumber=1&pageSize=1', '?pageNumber=2&pageSize=1']);
    expect(first).toMatchObject({ pageNumber: 1, totalPages: 2, hasPreviousPage: false, hasNextPage: true });
    expect(second).toMatchObject({ pageNumber: 2, totalPages: 2, hasPreviousPage: true, hasNextPage: false });
    expect(second.items).toEqual([{ membershipId: 'm-2' }]);
  });

  it('reads a null page as the default first page', async () => {
    const seen = directory('/api/platform/audit', () => page([]));

    await client().listAudit(null);

    expect(seen.urls[0].search).toBe('?pageNumber=1&pageSize=25');
  });

  it('reads each directory as its own typed items and offset page metadata', async () => {
    server.use(http.get('/api/platform/organizations', () => page([{ tenantId: 't-1', slug: 'acme', status: 'Active' }])));
    server.use(http.get('/api/platform/identities', () => page([{ identityId: 'i-1', normalizedEmail: 'ana@example.test' }])));
    server.use(http.get('/api/platform/admins', () => page([{ membershipId: 'm-1', isOwner: true }])));
    server.use(http.get('/api/platform/audit', () => page([{ eventId: 'e-1', eventType: 'platform.bootstrap.completed' }])));

    const platform = client();
    const organizations = await platform.listOrganizations();

    expect(organizations.items[0].slug).toBe('acme');
    expect(organizations).toMatchObject({ pageNumber: 1, pageSize: 25, totalCount: 1, totalPages: 1, hasNextPage: false });
    expect((await platform.listIdentities()).items[0].normalizedEmail).toBe('ana@example.test');
    expect((await platform.listAdministrators()).items[0].isOwner).toBe(true);
    expect((await platform.listAudit()).items[0].eventType).toBe('platform.bootstrap.completed');
  });

  /**
   * A directory that stopped declaring one of its page members is drift, and drift is reported rather than
   * absorbed — a client that quietly read a half-shaped page would keep working while the contract moved.
   */
  it('refuses a directory page that is missing a declared page member', async () => {
    server.use(http.get('/api/platform/organizations', () => HttpResponse.json({
      items: [],
      pageNumber: 1,
      pageSize: 25,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    })));

    const failure = await client().listOrganizations().catch((candidate) => candidate);

    expect(failure.problem).toEqual({ code: 'unreadable_response', status: 0 });
    expect(failure.message).not.toContain('totalCount');
    expect(JSON.stringify(failure.problem)).not.toContain('totalCount');
  });

  it('refuses a directory page that answers with a bare array', async () => {
    server.use(http.get('/api/platform/organizations', () => HttpResponse.json([{ tenantId: 't-1' }])));

    await expect(client().listOrganizations()).rejects.toThrow();
  });
});
