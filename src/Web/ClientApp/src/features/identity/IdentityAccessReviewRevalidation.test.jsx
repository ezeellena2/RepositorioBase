import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { NavMenu } from '../../components/NavMenu';
import { server } from '../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../test/identityServer';
import { IdentityProvider } from './context/IdentityProvider';
import { externalNavigation } from './externalNavigation';
import { SessionsPage } from './sessions/SessionsPage';
import { RolesPage } from './roles/RolesPage';
import { MembersPage } from './members/MembersPage';
import { InviteMemberPage } from './invitations/InviteMemberPage';

const TENANT = 'tenant-1';
const PAGE_SIZE = 100;
const CURSOR = 'AAAAAAAAAAAAAAAAAAAAZA';
const ADMIN_PERMISSIONS = [
  'members.read', 'members.manage', 'members.invite', 'roles.read', 'roles.manage',
  'tenant.ownership.transfer', 'identity.sessions.manage', 'identity.credentials.manage', 'identity.external.manage',
];
const role = (overrides = {}) => ({
  roleId: 'role-1', name: 'Bookkeeper', isSystem: false, isRetired: false,
  permissions: ['members.read'], version: '781', ...overrides,
});
const member = (overrides = {}) => ({
  membershipId: 'member-1', identityId: 'recipient-1', displayName: 'Bruno',
  normalizedEmail: 'bruno@example.test', status: 'Active', roleIds: ['role-1'],
  isOwner: false, version: '782', ...overrides,
});
const invitation = (overrides = {}) => ({
  invitationId: 'invitation-1', normalizedEmail: 'invitee@example.test', status: 'Pending',
  createdAt: '2026-09-01T00:00:00Z', expiresAt: '2026-09-08T00:00:00Z', roleIds: [], ...overrides,
});
const pageResponse = (items, nextCursor = null) => HttpResponse.json({ items, nextCursor });
const rolesAre = (items) => http.get(`/api/tenants/${TENANT}/roles`, () => pageResponse(items));
const membersAre = (items) => http.get(`/api/tenants/${TENANT}/members`, () => pageResponse(items));
const invitationsAre = (items) => http.get(`/api/tenants/${TENANT}/invitations`, () => pageResponse(items));
const renderPage = (page, permissions = ADMIN_PERMISSIONS) => {
  server.use(antiforgery(), contextIs(signedInContext({ permissions })));
  return render(<MemoryRouter><IdentityProvider>{page}</IdentityProvider></MemoryRouter>);
};

beforeEach(() => {
  vi.spyOn(window, 'confirm').mockReturnValue(true);
  vi.spyOn(externalNavigation, 'leaveFor').mockImplementation(() => {});
});
afterEach(() => vi.restoreAllMocks());

// These are rendered-component reproductions with an API fixture, not a Google authentication or browser
// integration claim. The fixture describes an already confirmed, Google-only Organization owner. A successful
// proof start must be observable before any sensitive write; no mock silently grants a password to that owner.
const googleOnlyOwner = () => {
  const starts = [];
  const passwordAttempts = [];
  const writes = [];
  server.use(
    http.get('/api/identity/credentials', () => HttpResponse.json({ hasPassword: false, passwordUpdatedAt: null })),
    http.get('/api/identity/external', () => HttpResponse.json({
      available: ['Google'],
      items: [{ handle: 'linked-google', provider: 'Google', providerEmail: 'owner@provider.test', linkedAt: '2026-09-01T00:00:00Z' }],
    })),
    http.get('/api/identity/sessions', () => HttpResponse.json([
      { sessionRef: 'AAAAAAAAAAAAAAAAAAAAAA', isCurrent: true, deviceLabel: 'Windows', createdAt: '2026-09-07T10:00:00Z', lastSeenAt: '2026-09-07T10:00:00Z', expiresAt: '2026-09-07T22:00:00Z' },
      { sessionRef: 'BBBBBBBBBBBBBBBBBBBBBB', isCurrent: false, deviceLabel: 'Android', createdAt: '2026-09-07T09:00:00Z', lastSeenAt: '2026-09-07T10:00:00Z', expiresAt: '2026-09-07T21:00:00Z' },
    ])),
    rolesAre([role(), role({ roleId: 'role-2', name: 'Auditor' })]),
    membersAre([member(), member({ membershipId: 'owner-membership', identityId: 'user-1', displayName: 'Ana', isOwner: true })]),
    http.get(`/api/tenants/${TENANT}/permission-catalog`, () => HttpResponse.json([{ code: 'members.read', grantable: true }])),
    http.post('/api/identity/credentials/reauthenticate', async ({ request }) => {
      passwordAttempts.push(await request.json());
      return problem(400, 'invalid_credential_proof');
    }),
    http.post('/api/identity/external/:provider/proof/start', async ({ request, params }) => {
      starts.push({ provider: params.provider, ...await request.json() });
      return HttpResponse.json({ authorizationRequestUri: '/api/identity/external/Google/challenge' });
    }),
    ...[
      [http.delete, '/api/identity/sessions/:sessionRef'],
      [http.post, `/api/tenants/${TENANT}/roles`],
      [http.put, `/api/tenants/${TENANT}/roles/:roleId`],
      [http.post, `/api/tenants/${TENANT}/roles/:roleId/retire`],
      [http.put, `/api/tenants/${TENANT}/members/:membershipId/roles`],
      [http.post, `/api/tenants/${TENANT}/ownership/transfer`],
    ].map(([method, path]) => method(path, ({ request }) => {
      writes.push(request.url);
      return problem(401, 'recent_proof_required');
    })),
  );
  return { starts, passwordAttempts, writes };
};

const providerEntryName = /google|verify.*identity|prove.*identity|reauthenticat/i;
const executable = (element) => !element.disabled && element.getAttribute('aria-disabled') !== 'true';
const providerEntries = () => [
  ...screen.queryAllByRole('button', { name: providerEntryName }),
  ...screen.queryAllByRole('link', { name: providerEntryName }),
].filter(executable);

// Accept either the existing operation control or an accessible provider-proof entry, without requiring a
// new button label or prescribing a modal/page design. The first blocker is reported before asserting a round
// trip that the current UI cannot even begin. No local password is entered as a workaround.
async function beginWithGoogle(operationName, action, recorded) {
  const operation = screen.queryByRole('button', { name: operationName });
  const entry = operation && executable(operation) ? operation : providerEntries()[0];
  expect(entry, `R6A: ${operationName} has no executable operation or provider-proof entry without a local password`).toBeDefined();
  await userEvent.click(entry);
  if (recorded.starts.length === 0) {
    const provider = providerEntries().find((element) => element !== entry);
    if (provider) await userEvent.click(provider);
  }
  await waitFor(() => expect(recorded.starts).toEqual([{ provider: 'Google', action }]));
  expect(recorded.passwordAttempts).toEqual([]);
  expect(recorded.writes, 'the operation must wait for completion of its action-bound proof').toEqual([]);
}

describe('R6A: Google-only users can begin sensitive operations using their linked authenticator', () => {
  it.each([
    ['one other session', 'End this device', 'sessions.revoke-one'],
    ['every other session', 'End every other device', 'sessions.revoke-others'],
  ])('can revoke %s without creating a password', async (_, operationName, action) => {
    const recorded = googleOnlyOwner();
    renderPage(<SessionsPage />);
    await screen.findByText('Android');
    await beginWithGoogle(operationName, action, recorded);
  });

  it.each(['create', 'edit', 'retire'])('can %s a role with Google proof', async (operation) => {
    const recorded = googleOnlyOwner();
    renderPage(<RolesPage />);
    await screen.findByText('Bookkeeper');
    if (operation === 'edit') await userEvent.click(screen.getByRole('button', { name: 'Edit Bookkeeper' }));
    if (operation !== 'retire') {
      await userEvent.clear(screen.getByLabelText('Name'));
      await userEvent.type(screen.getByLabelText('Name'), 'Updated bookkeeper');
      if (operation === 'create') await userEvent.click(screen.getByLabelText('members.read'));
    }
    const operationName = { create: 'Create role', edit: 'Save role', retire: 'Retire Bookkeeper' }[operation];
    await beginWithGoogle(operationName, 'roles.change', recorded);
  });

  it('can change a membership assignment with Google proof', async () => {
    const recorded = googleOnlyOwner();
    renderPage(<MembersPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Edit roles of Bruno' }));
    await userEvent.click(screen.getByLabelText('Auditor'));
    await beginWithGoogle('Save roles', 'members.roles.change', recorded);
  });

  it('can transfer ownership to an active member with Google proof', async () => {
    const recorded = googleOnlyOwner();
    renderPage(<MembersPage />);
    await screen.findByText('Bruno');
    await beginWithGoogle('Transfer ownership to Bruno', 'tenant.ownership.transfer', recorded);
  });
});

describe('R6B: member-directory permission is sufficient for the member-directory UI', () => {
  it('shows the navigable roster even when reading roles is forbidden', async () => {
    const responses = [];
    server.use(
      http.get(`/api/tenants/${TENANT}/members`, () => {
        responses.push(['members', 200]);
        return pageResponse([member()]);
      }),
      http.get(`/api/tenants/${TENANT}/roles`, () => {
        responses.push(['roles', 403]);
        return problem(403, 'permission_denied');
      }),
    );
    renderPage(<><NavMenu /><MembersPage /></>, ['members.read']);
    expect(await screen.findByRole('link', { name: 'Members' })).toHaveAttribute('href', '/members');
    expect(screen.queryByRole('link', { name: 'Roles' })).not.toBeInTheDocument();
    await waitFor(() => expect(responses).toContainEqual(['members', 200]));
    expect(await screen.findByText(/bruno@example\.test/), 'an optional roles read must not hide the successful member response').toBeInTheDocument();
  });
});

const directories = [
  { name: 'roles', path: 'roles', Page: RolesPage, make: (i) => role({ roleId: `role-${i}`, name: `Directory role ${i}` }), label: (row) => row.name },
  { name: 'members', path: 'members', Page: MembersPage, make: (i) => member({ membershipId: `member-${i}`, identityId: `identity-${i}`, displayName: `Directory member ${i}`, normalizedEmail: `member${i}@example.test` }), label: (row) => row.displayName },
  { name: 'invitations', path: 'invitations', Page: InviteMemberPage, make: (i) => invitation({ invitationId: `invitation-${i}`, normalizedEmail: `invitee${i}@example.test` }), label: (row) => row.normalizedEmail },
];

describe('R6C: directories can reach records after the default 100-item page', () => {
  it.each(directories)('continues the $name directory with 101 distinct records', async ({ name, path, Page, make, label }) => {
    const rows = Array.from({ length: PAGE_SIZE + 1 }, (_, index) => make(index + 1));
    const requested = [];
    const delivered = new Map();
    server.use(
      rolesAre([role()]), membersAre([member()]), invitationsAre([]),
      http.get(`/api/tenants/${TENANT}/permission-catalog`, () => HttpResponse.json([])),
    );
    server.use(http.get(`/api/tenants/${TENANT}/${path}`, ({ request }) => {
      const url = new URL(request.url);
      const cursor = url.searchParams.get('cursor');
      const limit = Number(url.searchParams.get('limit') ?? PAGE_SIZE);
      requested.push({ cursor, limit });
      if (cursor !== null && cursor !== CURSOR) return problem(400, 'invalid_cursor');
      const start = cursor === CURSOR ? PAGE_SIZE : 0;
      const page = rows.slice(start, start + Math.min(limit, PAGE_SIZE));
      delivered.set(cursor, page.map(label));
      return pageResponse(page, start + page.length < rows.length ? CURSOR : null);
    }));

    renderPage(<Page />);
    await screen.findByText(label(rows[0]));
    expect(screen.getByText(label(rows[PAGE_SIZE - 1]))).toBeInTheDocument();
    expect(requested[0].limit).toBe(PAGE_SIZE);
    const visible = new Set(rows.filter((row) => screen.queryByText(label(row))).map(label));

    // Allow either automatic loading or an accessible next/more/continuation control. There is no assertion
    // about one particular label or about appending versus replacing the currently visible page.
    if (!requested.some(({ cursor }) => cursor === CURSOR)) {
      const continuationName = /next|more|continue|page\s*2|2\s*page|›|»/i;
      const continuation = [
        ...screen.queryAllByRole('button', { name: continuationName }),
        ...screen.queryAllByRole('link', { name: continuationName }),
      ].find(executable);
      expect(continuation, `R6C: ${name} displays 100 of 101 records but offers no continuation for nextCursor`).toBeDefined();
      await userEvent.click(continuation);
    }

    await waitFor(() => expect(requested).toContainEqual({ cursor: CURSOR, limit: PAGE_SIZE }));
    expect(await screen.findByText(label(rows[PAGE_SIZE]))).toBeInTheDocument();
    for (const row of rows.filter((candidate) => screen.queryByText(label(candidate)))) visible.add(label(row));
    expect([...visible].sort()).toEqual(rows.map(label).sort());
    const traversed = [...delivered.values()].flat();
    expect(new Set(traversed).size).toBe(traversed.length);
  });
});
