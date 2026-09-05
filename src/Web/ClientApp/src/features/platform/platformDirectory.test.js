import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { createApiTransport } from '../identity/api/apiTransport';
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

const page = (items, nextCursor = null) => HttpResponse.json({ items, nextCursor });

/**
 * The directory half of IA-REQ-045: four distinct typed resources, each bounded, each followed only through the
 * opaque cursor the server issued. The client never invents a filter, an offset or a page size outside the bounds
 * the API declares — sending one would only be clamped, and a client that relies on being clamped is one that has
 * stopped agreeing with the contract.
 */
describe('platform directories', () => {
  it('asks for a bounded page and no filter', async () => {
    const seen = directory('/api/platform/organizations', () => page([]));

    await client().listOrganizations();

    expect(seen.urls[0].searchParams.get('limit')).toBe('25');
    expect([...seen.urls[0].searchParams.keys()]).toEqual(['limit']);
  });

  it('never asks for more than the maximum or less than the minimum', async () => {
    const seen = directory('/api/platform/identities', () => page([]));

    await client().listIdentities({ limit: 10_000 });
    await client().listIdentities({ limit: 0 });

    expect(seen.urls.map((url) => url.searchParams.get('limit'))).toEqual(['100', '1']);
  });

  it('follows only the cursor the server issued', async () => {
    const seen = directory('/api/platform/admins', (call) =>
      (call === 1 ? page([{ membershipId: 'm-1' }], 'opaque-cursor') : page([{ membershipId: 'm-2' }])));

    const first = await client().listAdministrators({ limit: 1 });
    const second = await client().listAdministrators({ limit: 1, cursor: first.nextCursor });

    expect(first.nextCursor).toBe('opaque-cursor');
    expect(seen.urls[1].searchParams.get('cursor')).toBe('opaque-cursor');
    expect(second.nextCursor).toBeNull();
  });

  it('omits the cursor entirely on the first page rather than sending an empty one', async () => {
    const seen = directory('/api/platform/audit', () => page([]));

    await client().listAudit({ cursor: undefined });

    expect(seen.urls[0].searchParams.has('cursor')).toBe(false);
  });

  it('reads each directory as its own typed items and nextCursor', async () => {
    server.use(http.get('/api/platform/organizations', () => page([{ tenantId: 't-1', slug: 'acme', status: 'Active' }])));
    server.use(http.get('/api/platform/identities', () => page([{ identityId: 'i-1', normalizedEmail: 'ana@example.test' }])));
    server.use(http.get('/api/platform/admins', () => page([{ membershipId: 'm-1', isOwner: true }])));
    server.use(http.get('/api/platform/audit', () => page([{ eventId: 'e-1', eventType: 'platform.bootstrap.completed' }])));

    const platform = client();

    expect((await platform.listOrganizations()).items[0].slug).toBe('acme');
    expect((await platform.listIdentities()).items[0].normalizedEmail).toBe('ana@example.test');
    expect((await platform.listAdministrators()).items[0].isOwner).toBe(true);
    expect((await platform.listAudit()).items[0].eventType).toBe('platform.bootstrap.completed');
  });

  /**
   * A directory that stopped declaring one of its two members is drift, and drift is reported rather than
   * absorbed — a client that quietly read a half-shaped page would keep working while the contract moved.
   */
  it('refuses a directory page that is missing its cursor member', async () => {
    server.use(http.get('/api/platform/organizations', () => HttpResponse.json({ items: [] })));

    await expect(client().listOrganizations()).rejects.toThrow(/nextCursor/);
  });

  it('refuses a directory page that answers with a bare array', async () => {
    server.use(http.get('/api/platform/organizations', () => HttpResponse.json([{ tenantId: 't-1' }])));

    await expect(client().listOrganizations()).rejects.toThrow();
  });
});
