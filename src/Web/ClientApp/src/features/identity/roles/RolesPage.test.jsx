import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { IdentityProvider } from '../context/IdentityProvider';
import { RolesPage } from './RolesPage';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const TENANT = 'tenant-1';

const renderPage = (context = signedInContext()) => {
  server.use(antiforgery(), contextIs(context));
  return render(<MemoryRouter><IdentityProvider><RolesPage /></IdentityProvider></MemoryRouter>);
};

const role = (overrides = {}) => ({
  roleId: 'role-1',
  name: 'Bookkeeper',
  isSystem: false,
  isRetired: false,
  permissions: ['members.read'],
  version: '781',
  ...overrides,
});

const rolesAre = (items) => http.get(`/api/tenants/${TENANT}/roles`, () => HttpResponse.json({ items, nextCursor: null }));

const catalogIs = (entries) => http.get(`/api/tenants/${TENANT}/permission-catalog`, () => HttpResponse.json(entries));

const proofAccepted = (spent) => http.post('/api/identity/credentials/reauthenticate', async ({ request }) => {
  spent.push(await request.json());
  return new HttpResponse(null, { status: 204 });
});

/**
 * Roles, from the browser. The two rules that matter are both the server's, and this screen's job is to make them
 * legible rather than to re-implement them: only grantable permissions are offered, and a refusal is shown.
 */
describe('roles page', () => {
  it('lists the roles with what each one confers, and marks the built-in one', async () => {
    server.use(rolesAre([role(), role({ roleId: 'role-2', name: 'Owner', isSystem: true, permissions: ['roles.manage'] })]));
    server.use(catalogIs([{ code: 'members.read', grantable: true }]));
    renderPage();

    expect(await screen.findByRole('button', { name: 'Retire Bookkeeper' })).toBeInTheDocument();
    expect(screen.getByText(/built in/)).toBeInTheDocument();
    expect(screen.getAllByText(/members\.read/).length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Retire Owner' }))
      .toBeNull("a built-in role is the tenant's own scaffolding and is not offered for retirement");
    expect(screen.queryByRole('button', { name: 'Edit Owner' })).toBeNull();
  });

  /**
   * The ceiling made visible. A code this administrator cannot grant is not offered at all, so nobody composes a
   * role the server will refuse without being able to say which code was the problem.
   */
  it('offers only the permissions this administrator could actually grant', async () => {
    renderPage();
    server.use(rolesAre([]));
    server.use(catalogIs([
      { code: 'members.read', grantable: true },
      { code: 'members.manage', grantable: false },
    ]));

    expect(await screen.findByLabelText('members.read')).toBeInTheDocument();
    expect(screen.queryByLabelText('members.manage')).not.toBeInTheDocument();
  });

  it('buys a proof before it creates a role, and sends exactly what was ticked', async () => {
    const spent = [];
    const created = [];
    renderPage();
    server.use(rolesAre([]), catalogIs([{ code: 'members.read', grantable: true }]), proofAccepted(spent));
    server.use(http.post(`/api/tenants/${TENANT}/roles`, async ({ request }) => {
      created.push(await request.json());
      return HttpResponse.json(role(), { status: 201 });
    }));

    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    await userEvent.type(screen.getByLabelText('Name'), 'Bookkeeper');
    await userEvent.click(screen.getByLabelText('members.read'));
    await userEvent.click(screen.getByRole('button', { name: 'Create role' }));

    await waitFor(() => expect(created).toHaveLength(1));
    expect(spent).toEqual([{ action: 'roles.change', password: 'Testing1234!' }]);
    expect(created[0]).toEqual({ name: 'Bookkeeper', permissions: ['members.read'] });
    expect(screen.getByLabelText('Password')).toHaveValue('');
  });

  it('sends no role change when the proof is refused', async () => {
    renderPage();
    server.use(rolesAre([]), catalogIs([{ code: 'members.read', grantable: true }]));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => problem(400, 'invalid_credential_proof')));
    server.use(http.post(`/api/tenants/${TENANT}/roles`, () => {
      throw new Error('a role must never be written without a proof');
    }));

    await userEvent.type(await screen.findByLabelText('Password'), 'Wrong1234!');
    await userEvent.type(screen.getByLabelText('Name'), 'Nope');
    await userEvent.click(screen.getByRole('button', { name: 'Create role' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/that password was not accepted/i);
  });

  it('binds a role name error, focuses it, and leaves unclaimed errors in the summary', async () => {
    renderPage();
    server.use(rolesAre([]), catalogIs([{ code: 'members.read', grantable: true }]), proofAccepted([]));
    server.use(http.post(`/api/tenants/${TENANT}/roles`, () => problem(400, 'validation_failed', {
      status: 400,
      errors: {
        name: [{ code: 'required', params: {} }],
        request: [{ code: 'invalid', params: {} }],
      },
    })));

    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    await userEvent.type(screen.getByLabelText('Name'), 'x');
    await userEvent.click(screen.getByRole('button', { name: 'Create role' }));

    const name = screen.getByLabelText('Name');
    await waitFor(() => expect(name).toHaveFocus());
    expect(name).toHaveAttribute('id', 'role-name');
    expect(name).toHaveAttribute('aria-invalid', 'true');
    expect(name).toHaveAccessibleDescription('This value is required.');
    expect(screen.getByRole('alert')).toHaveTextContent('Request: This value is not valid.');
    expect(screen.getByRole('alert')).not.toHaveTextContent('Name: This value is required.');

    await userEvent.type(name, 'y');
    expect(name).not.toHaveAttribute('aria-invalid', 'true');
  });

  it('echoes the version it read when it saves an edit', async () => {
    const edits = [];
    renderPage();
    server.use(rolesAre([role()]), catalogIs([{ code: 'members.read', grantable: true }]), proofAccepted([]));
    server.use(http.put(`/api/tenants/${TENANT}/roles/role-1`, async ({ request }) => {
      edits.push(await request.json());
      return HttpResponse.json(role({ version: '782' }));
    }));

    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Edit Bookkeeper' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save role' }));

    await waitFor(() => expect(edits).toHaveLength(1));
    expect(edits[0].version).toBe('781');
    expect(edits[0].permissions).toEqual(['members.read']);
  });

  /**
   * Only the server can count administrators, and it counts after the change it then rejects. The screen's job
   * is to show that answer rather than to guess at it beforehand.
   */
  it('shows the refusal when a change would leave nobody able to administer', async () => {
    renderPage();
    server.use(rolesAre([role()]), catalogIs([{ code: 'members.read', grantable: true }]), proofAccepted([]));
    server.use(http.post(`/api/tenants/${TENANT}/roles/role-1/retire`, () => problem(409, 'last_administrator_required')));

    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Retire Bookkeeper' }));

    const row = screen.getByRole('button', { name: 'Retire Bookkeeper' }).closest('tr');
    const alert = await within(row).findByRole('alert');
    expect(alert).toHaveTextContent(/no administrator/i);
    expect(alert).toHaveFocus();
    expect(screen.getAllByRole('alert')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Retire Bookkeeper' }))
      .toBeInTheDocument("a refused retirement leaves the role exactly where it was");
  });

  it('replaces the initial wait with a retryable read error and loads roles after Try again', async () => {
    let attempts = 0;
    server.use(
      http.get(`/api/tenants/${TENANT}/roles`, () => {
        attempts += 1;
        return attempts === 1
          ? problem(500, 'internal_server_error', { traceId: 'trace-roles' })
          : HttpResponse.json({ items: [role()], nextCursor: null });
      }),
      catalogIs([{ code: 'members.read', grantable: true }]),
    );
    renderPage();

    expect(await screen.findByText('Reference: trace-roles')).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('button', { name: 'Retire Bookkeeper' })).toBeInTheDocument();
    expect(attempts).toBe(2);
  });

  it('keeps listed roles and places a failed continuation beside Show more', async () => {
    server.use(
      http.get(`/api/tenants/${TENANT}/roles`, ({ request }) =>
        new URL(request.url).searchParams.has('cursor')
          ? problem(500, 'internal_server_error', { traceId: 'trace-role-page' })
          : HttpResponse.json({ items: [role()], nextCursor: 'next-role' })),
      catalogIs([{ code: 'members.read', grantable: true }]),
    );
    renderPage();

    const more = await screen.findByRole('button', { name: 'Show more roles' });
    await userEvent.click(more);

    const pagination = more.parentElement;
    expect(await within(pagination).findByText('Reference: trace-role-page')).toBeInTheDocument();
    expect(screen.getByText('Bookkeeper')).toBeInTheDocument();
    expect(screen.getAllByRole('alert')).toHaveLength(1);
  });

  it('says so rather than failing when the session is in no organization', async () => {
    renderPage(signedInContext({ activeTenant: null }));

    expect(await screen.findByText(/choose an organization first/i)).toBeInTheDocument();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
  });
});
