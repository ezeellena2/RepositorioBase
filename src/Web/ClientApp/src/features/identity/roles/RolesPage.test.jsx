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

const rolesAre = (items, meta) => http.get(`/api/tenants/${TENANT}/roles`, () => HttpResponse.json(pageOf(items, meta)));

/** The API's side of a role list: every role served a page at a time, with each query string it was asked with. */
const rolesServed = (all) => {
  const searches = [];
  server.use(http.get(`/api/tenants/${TENANT}/roles`, ({ request }) => {
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

const numberedRoles = (count) => Array.from({ length: count }, (_, index) => role({
  roleId: `role-${index + 1}`,
  name: `Role ${index + 1}`,
}));

/** A role list whose answer is chosen page by page, with each page number it was asked for. */
const pagesServed = (answer) => {
  const requested = [];
  server.use(http.get(`/api/tenants/${TENANT}/roles`, ({ request }) => {
    const pageNumber = Number(new URL(request.url).searchParams.get('pageNumber'));
    requested.push(pageNumber);
    return answer(pageNumber);
  }));
  return requested;
};

/**
 * Every node the screen puts on the page while it is watched, read back by its text. A sentence drawn for one render
 * and taken down by the next is gone before any assertion runs, so "never said" is checked here instead.
 */
const watchShownText = () => {
  const shown = [];
  const observer = new MutationObserver((records) => {
    records.forEach((record) => record.addedNodes.forEach((node) => shown.push(node.textContent)));
  });
  observer.observe(document.body, { childList: true, subtree: true });
  return { saw: (text) => shown.some((content) => content.includes(text)), stop: () => observer.disconnect() };
};

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
    expect(name).toHaveAccessibleDescription('A role name is required.');
    expect(screen.getByRole('alert')).toHaveTextContent('This value is not valid.');
    expect(screen.getByRole('alert')).not.toHaveTextContent('Request:');
    expect(screen.getByRole('alert')).not.toHaveTextContent('Name: A role name is required.');

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
          : HttpResponse.json(pageOf([role()]));
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

  it('reaches the 26th role by page number, and comes back to the first page', async () => {
    server.use(catalogIs([{ code: 'members.read', grantable: true }]));
    const searches = rolesServed(numberedRoles(26));
    renderPage();

    expect(await screen.findByText('1–25 of 26')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retire Role 25' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Go to next page' }));

    expect(await screen.findByText('26–26 of 26')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retire Role 26' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retire Role 1' })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Go to previous page' }));

    expect(await screen.findByText('1–25 of 26')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retire Role 1' })).toBeInTheDocument();
    expect(searches).toEqual(['?pageNumber=1&pageSize=25', '?pageNumber=2&pageSize=25', '?pageNumber=1&pageSize=25']);
  });

  it('restarts at the first page when the rows per page change', async () => {
    server.use(catalogIs([{ code: 'members.read', grantable: true }]));
    const searches = rolesServed(numberedRoles(60));
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));
    expect(await screen.findByText('26–50 of 60')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('combobox', { name: /rows per page/i }));
    await userEvent.click(screen.getByRole('option', { name: '50' }));

    expect(await screen.findByText('1–50 of 60')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retire Role 50' })).toBeInTheDocument();
    expect(searches).toEqual(['?pageNumber=1&pageSize=25', '?pageNumber=2&pageSize=25', '?pageNumber=1&pageSize=50']);
  });

  it('offers no way to another page when every role fits on one', async () => {
    server.use(catalogIs([{ code: 'members.read', grantable: true }]));
    rolesServed(numberedRoles(3));
    renderPage();

    expect(await screen.findByText('1–3 of 3')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go to next page' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Go to previous page' })).toBeDisabled();
  });

  it('keeps the empty state and draws no page control when the organization has no roles', async () => {
    server.use(rolesAre([]), catalogIs([{ code: 'members.read', grantable: true }]));
    renderPage();

    expect(await screen.findByText(/No roles have been made for this organization yet/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Go to next page' })).toBeNull();
    expect(screen.queryByText(/rows per page/i)).toBeNull();
  });

  it('asks for the permission catalogue once, however many pages are turned', async () => {
    let catalogueReads = 0;
    server.use(http.get(`/api/tenants/${TENANT}/permission-catalog`, () => {
      catalogueReads += 1;
      return HttpResponse.json([{ code: 'members.read', grantable: true }]);
    }));
    rolesServed(numberedRoles(30));
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));
    expect(await screen.findByText('26–30 of 30')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Go to previous page' }));
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument();

    expect(catalogueReads).toBe(1);
    expect(screen.getByLabelText('members.read')).toBeInTheDocument();
  });

  /**
   * A role is drawn with the codes it confers and the codes that may be granted, so a role list without its
   * catalogue would be half a screen. The roles wait, and only the read that failed is asked for again.
   */
  it('holds the roles back when the permission catalogue cannot be reached, and retries only the catalogue', async () => {
    let catalogueReads = 0;
    const searches = rolesServed([role()]);
    server.use(http.get(`/api/tenants/${TENANT}/permission-catalog`, () => {
      catalogueReads += 1;
      return catalogueReads === 1
        ? HttpResponse.error()
        : HttpResponse.json([{ code: 'members.read', grantable: true }]);
    }));
    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('We could not reach the service.');
    expect(screen.queryByRole('button', { name: 'Retire Bookkeeper' })).toBeNull();
    expect(screen.queryByText('Bookkeeper')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('button', { name: 'Retire Bookkeeper' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).toBeNull();
    expect(catalogueReads).toBe(2);
    expect(searches).toEqual(['?pageNumber=1&pageSize=25']);
  });

  it('keeps the rows and the control on the loaded page when the next page cannot be reached', async () => {
    server.use(catalogIs([{ code: 'members.read', grantable: true }]));
    const thirty = numberedRoles(30);
    const requested = pagesServed((pageNumber) => (pageNumber === 1
      ? HttpResponse.json(pageOf(thirty.slice(0, 25), { totalCount: 30 }))
      : HttpResponse.error()));
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('We could not reach the service.');
    expect(screen.getByRole('button', { name: 'Retire Role 1' })).toBeInTheDocument();
    expect(screen.getByText('1–25 of 30')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(requested).toEqual([1, 2, 2]));
  });

  it('clears the rows and explains a refused page in place', async () => {
    server.use(catalogIs([]));
    const thirty = numberedRoles(30);
    const requested = pagesServed((pageNumber) => (pageNumber === 1
      ? HttpResponse.json(pageOf(thirty.slice(0, 25), { totalCount: 30 }))
      : problem(403, 'permission_denied')));
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('You do not have permission to do that here.');
    expect(screen.queryByRole('button', { name: 'Retire Role 1' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Go to next page' })).toBeNull();
    expect(requested).toEqual([1, 2]);
  });

  it('moves to the real last page when the answered page is past the end, and never calls the list empty', async () => {
    server.use(catalogIs([]));
    const thirty = numberedRoles(30);
    // The first answer is the stale page a person lands on after the last roles on it were retired elsewhere.
    const requested = pagesServed((pageNumber) => HttpResponse.json(pageNumber === 2
      ? pageOf(thirty.slice(25), { pageNumber: 2, totalCount: 30 })
      : pageOf([], { pageNumber: 3, totalCount: 30 })));
    const text = watchShownText();
    renderPage();

    expect(await screen.findByText('26–30 of 30')).toBeInTheDocument();
    text.stop();
    expect(screen.getByRole('button', { name: 'Retire Role 30' })).toBeInTheDocument();
    expect(requested).toEqual([1, 2]);
    expect(text.saw('No roles have been made for this organization yet')).toBe(false);
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('asks once and keeps the empty state when there is no page to correct', async () => {
    server.use(catalogIs([{ code: 'members.read', grantable: true }]));
    const requested = pagesServed(() => HttpResponse.json(pageOf([])));
    renderPage();

    expect(await screen.findByText(/No roles have been made for this organization yet/)).toBeInTheDocument();
    // Long enough for a correction to have been sent: the past-the-end test above sees its second request sooner.
    await new Promise((resolve) => { setTimeout(resolve, 50); });
    expect(requested).toEqual([1]);
    expect(screen.getByText(/No roles have been made for this organization yet/)).toBeInTheDocument();
  });

  it('renders only the last page asked for when clicks outrun the network', async () => {
    server.use(catalogIs([]));
    const hundred = numberedRoles(100);
    let releaseSecondPage;
    const secondPageHeld = new Promise((resolve) => { releaseSecondPage = resolve; });
    const answered = [];
    server.use(http.get(`/api/tenants/${TENANT}/roles`, async ({ request }) => {
      const { searchParams } = new URL(request.url);
      const pageNumber = Number(searchParams.get('pageNumber'));
      const pageSize = Number(searchParams.get('pageSize'));
      if (pageNumber === 2) await secondPageHeld;
      answered.push(`${pageNumber}/${pageSize}`);
      return HttpResponse.json(pageOf(
        hundred.slice((pageNumber - 1) * pageSize, pageNumber * pageSize),
        { pageNumber, pageSize, totalCount: 100 },
      ));
    }));
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));
    await userEvent.click(screen.getByRole('combobox', { name: /rows per page/i }));
    await userEvent.click(screen.getByRole('option', { name: '50' }));
    expect(await screen.findByText('1–50 of 100')).toBeInTheDocument();

    releaseSecondPage();
    await waitFor(() => expect(answered).toEqual(['1/25', '1/50', '2/25']));
    await new Promise((resolve) => { setTimeout(resolve, 0); });
    expect(screen.getByText('1–50 of 100')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retire Role 50' })).toBeInTheDocument();
    expect(screen.queryByText('26–50 of 100')).toBeNull();
  });

  /**
   * A retirement the server accepted stays accepted. Reading its page again is housekeeping, so a failed read is said
   * as a read problem and never as the retirement failing — and the page read again is the page it was made from.
   */
  it('reads the page the retirement was made from again, and a failed read never says the retirement failed', async () => {
    const thirty = numberedRoles(30);
    let retirements = 0;
    const requested = pagesServed((pageNumber) => {
      if (pageNumber === 1) return HttpResponse.json(pageOf(thirty.slice(0, 25), { totalCount: 30 }));
      return retirements === 0
        ? HttpResponse.json(pageOf(thirty.slice(25), { pageNumber: 2, totalCount: 30 }))
        : problem(500, 'internal_server_error', { traceId: 'trace-retired' });
    });
    server.use(catalogIs([{ code: 'members.read', grantable: true }]), proofAccepted([]));
    server.use(http.post(`/api/tenants/${TENANT}/roles/role-26/retire`, () => {
      retirements += 1;
      return new HttpResponse(null, { status: 204 });
    }));
    renderPage();

    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Go to next page' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Retire Role 26' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Something went wrong. Try again.');
    expect(screen.getByText('Reference: trace-retired')).toBeInTheDocument();
    expect(retirements).toBe(1);
    expect(requested).toEqual([1, 2, 2]);
    expect(screen.getAllByRole('alert')).toHaveLength(1);
    expect(within(screen.getByRole('button', { name: 'Retire Role 26' }).closest('tr')).queryByRole('alert')).toBeNull();
    expect(screen.getByLabelText('Password')).toHaveValue('');
  });

  it('says so rather than failing when the session is in no organization', async () => {
    renderPage(signedInContext({ activeTenant: null }));

    expect(await screen.findByText(/choose an organization first/i)).toBeInTheDocument();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
  });
});
