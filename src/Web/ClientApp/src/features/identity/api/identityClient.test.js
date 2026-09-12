import { describe, expect, it, vi } from 'vitest';
import { createIdentityClient } from './identityClient';

describe('identity client read cancellation', () => {
  it('forwards a caller signal through every effect-owned P2-6 read', async () => {
    const signal = new AbortController().signal;
    const send = vi.fn().mockResolvedValue({ items: [], nextCursor: null });
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
