import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { IdentityProvider } from '../context/IdentityProvider';
import { InviteMemberPage } from './InviteMemberPage';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const TENANT = 'tenant-1';

const renderPage = (context = signedInContext()) => {
  server.use(antiforgery(), contextIs(context));
  return render(<MemoryRouter><IdentityProvider><InviteMemberPage /></IdentityProvider></MemoryRouter>);
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

const invitation = (overrides = {}) => ({
  invitationId: 'invitation-1',
  normalizedEmail: 'nuevo@example.test',
  status: 'Pending',
  createdAt: '2026-09-01T00:00:00Z',
  expiresAt: '2026-09-08T00:00:00Z',
  roleIds: ['role-1'],
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

const rolesAre = (items) =>
  http.get(`/api/tenants/${TENANT}/roles`, () => HttpResponse.json(pageOf(items)));

const invitationsAre = (items) =>
  http.get(`/api/tenants/${TENANT}/invitations`, () => HttpResponse.json(pageOf(items)));

const pageAskedFor = (request) => {
  const { searchParams } = new URL(request.url);
  return { pageNumber: Number(searchParams.get('pageNumber')), pageSize: Number(searchParams.get('pageSize')) };
};

/** The API's side of the offers: every invitation served a page at a time, with each query string it was asked with. */
const invitationsServed = (all) => {
  const searches = [];
  server.use(http.get(`/api/tenants/${TENANT}/invitations`, ({ request }) => {
    searches.push(new URL(request.url).search);
    const { pageNumber, pageSize } = pageAskedFor(request);
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
    const { pageNumber } = pageAskedFor(request);
    pageNumbers.push(pageNumber);
    return HttpResponse.json(pageOf(
      all.slice((pageNumber - 1) * 25, pageNumber * 25),
      { pageNumber, pageSize: 25, totalCount: all.length },
    ));
  }));
  return pageNumbers;
};

const numberedRoles = (count) => Array.from({ length: count }, (_, index) => role({
  roleId: `role-${index + 1}`,
  name: `Role ${index + 1}`,
}));

const numberedInvitations = (count) => Array.from({ length: count }, (_, index) => invitation({
  invitationId: `invitation-${index + 1}`,
  normalizedEmail: `person${index + 1}@example.test`,
}));

beforeEach(() => {
  vi.spyOn(window, 'confirm').mockReturnValue(true);
});

/**
 * Making an offer and living with it afterwards. No token appears anywhere on this screen by contract: issuing
 * answers with an identifier and an expiry, reissuing answers with nothing at all, and the only usable
 * credential reaches the recipient by email (IA-REQ-015/018).
 */
describe('invite member page', () => {
  it('offers the organization roles by name instead of asking for identifiers', async () => {
    renderPage();
    server.use(rolesAre([role(), role({ roleId: 'role-2', name: 'Auditor', isRetired: true })]), invitationsAre([]));

    expect(await screen.findByLabelText('Bookkeeper')).toBeInTheDocument();
    expect(screen.queryByLabelText('Auditor')).toBeNull('a retired role cannot be offered');
    expect(screen.queryByLabelText(/identifier/i)).toBeNull();
  });

  it('sends the invitation with the roles that were ticked, and says when it expires', async () => {
    const issued = [];
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([]));
    server.use(http.post(`/api/tenants/${TENANT}/invitations`, async ({ request }) => {
      issued.push(await request.json());
      return HttpResponse.json({ invitationId: 'invitation-9', expiresAt: '2026-09-08T00:00:00Z' }, { status: 201 });
    }));

    await userEvent.type(await screen.findByLabelText('Email'), 'nuevo@example.test');
    await userEvent.click(screen.getByLabelText('Bookkeeper'));
    await userEvent.click(screen.getByRole('button', { name: 'Send invitation' }));

    await waitFor(() => expect(issued).toEqual([{ email: 'nuevo@example.test', roleIds: ['role-1'] }]));
    expect(await screen.findByRole('status')).toHaveTextContent(/expires on/i);
    expect(screen.getByLabelText('Email')).toHaveValue('');
  });

  it('binds email and role errors, focuses the first field, and makes the fieldset describable', async () => {
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([]));
    server.use(http.post(`/api/tenants/${TENANT}/invitations`, () => problem(400, 'validation_failed', {
      status: 400,
      errors: {
        email: [{ code: 'required', params: {} }],
        roleIds: [{ code: 'required', params: {} }],
        request: [{ code: 'invalid', params: {} }],
      },
    })));

    await userEvent.type(await screen.findByLabelText('Email'), 'valid@example.test');
    await userEvent.click(screen.getByRole('button', { name: 'Send invitation' }));

    const email = screen.getByLabelText('Email');
    const roles = screen.getByRole('group', { name: 'Roles to offer' });
    await waitFor(() => expect(email).toHaveFocus());
    expect(email).toHaveAttribute('id', 'invite-email');
    expect(email).toHaveAccessibleDescription('Enter an email address.');
    expect(roles).toHaveAttribute('id', 'invite-role-ids');
    expect(roles).toHaveAttribute('tabindex', '-1');
    expect(roles).toHaveAttribute('aria-invalid', 'true');
    expect(roles).toHaveAttribute('aria-describedby', 'invite-role-ids-error');
    expect(roles).toHaveAccessibleDescription('Choose at least one role.');
    expect(document.getElementById('invite-role-ids-error')).toHaveTextContent('Choose at least one role.');
    expect(screen.getByRole('alert')).toHaveTextContent('This value is not valid.');
    expect(screen.getByRole('alert')).not.toHaveTextContent('Request:');
    expect(screen.getByRole('alert')).not.toHaveFocus();
    expect(screen.getByRole('alert')).not.toHaveTextContent('Email: Enter an email address.');
    expect(screen.getByRole('alert')).not.toHaveTextContent('Roles: Choose at least one role.');

    await userEvent.type(email, 'x');
    expect(email).not.toHaveAttribute('aria-invalid', 'true');
    await userEvent.click(screen.getByLabelText('Bookkeeper'));
    expect(roles).not.toHaveAttribute('aria-invalid', 'true');
    expect(roles).not.toHaveAttribute('aria-describedby');
    expect(screen.queryByText('Choose at least one role.')).not.toBeInTheDocument();
  });

  it('lists standing offers with their state, and shows the role by name', async () => {
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([invitation()]));

    expect(await screen.findByRole('button', { name: /Resend to nuevo@example.test/ })).toBeInTheDocument();
    expect(screen.getAllByText(/Bookkeeper/).length).toBeGreaterThan(0);
    expect(screen.getByText(/Pending/)).toBeInTheDocument();
  });

  it('reissues a standing offer and asks the server for the list again', async () => {
    let listed = 0;
    const resent = [];
    renderPage();
    server.use(rolesAre([role()]));
    server.use(http.get(`/api/tenants/${TENANT}/invitations`, () => {
      listed += 1;
      return HttpResponse.json(pageOf([invitation()]));
    }));
    server.use(http.post(`/api/tenants/${TENANT}/invitations/invitation-1/resend`, () => {
      resent.push('invitation-1');
      return new HttpResponse(null, { status: 204 });
    }));

    await userEvent.click(await screen.findByRole('button', { name: /Resend to nuevo@example.test/ }));

    await waitFor(() => expect(resent).toEqual(['invitation-1']));
    await waitFor(() => expect(listed).toBeGreaterThan(1));
  });

  /**
   * Withdrawing ends somebody's way in, so it is asked for deliberately. A declined confirmation must reach no
   * endpoint at all — not a refused one.
   */
  it('confirms before withdrawing an offer', async () => {
    const cancelled = [];
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([invitation()]));
    server.use(http.post(`/api/tenants/${TENANT}/invitations/invitation-1/cancel`, () => {
      cancelled.push('invitation-1');
      return new HttpResponse(null, { status: 204 });
    }));

    await userEvent.click(await screen.findByRole('button', { name: /Withdraw invitation to nuevo@example.test/ }));

    await waitFor(() => expect(cancelled).toEqual(['invitation-1']));
    expect(window.confirm).toHaveBeenCalled();
  });

  it('withdraws nothing when the confirmation is declined', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([invitation()]));
    server.use(http.post(`/api/tenants/${TENANT}/invitations/invitation-1/cancel`, () => {
      throw new Error('an offer must not be withdrawn without a deliberate yes');
    }));

    await userEvent.click(await screen.findByRole('button', { name: /Withdraw invitation to nuevo@example.test/ }));

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('offers nothing to do to an offer already taken or already withdrawn', async () => {
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([
      invitation({ status: 'Accepted' }),
      invitation({ invitationId: 'invitation-2', normalizedEmail: 'otro@example.test', status: 'Cancelled' }),
    ]));

    await screen.findByText(/Accepted/);
    expect(screen.queryByRole('button', { name: /Resend/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Withdraw/ })).not.toBeInTheDocument();
  });

  it('shows the refusal the server answered with', async () => {
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([invitation()]));
    server.use(http.post(`/api/tenants/${TENANT}/invitations/invitation-1/resend`, () => problem(409, 'invitation_conflict')));

    const resend = await screen.findByRole('button', { name: /Resend to nuevo@example.test/ });
    await userEvent.click(resend);

    const row = resend.closest('tr');
    const alert = await within(row).findByRole('alert');
    expect(alert).toHaveTextContent(/current state/i);
    expect(alert).toHaveFocus();
    expect(screen.getAllByRole('alert')).toHaveLength(1);
  });

  it('keeps a send refusal inside the form, preserves the address, and focuses the alert', async () => {
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([]));
    server.use(http.post(`/api/tenants/${TENANT}/invitations`, () => problem(409, 'invitation_conflict')));

    const email = await screen.findByLabelText('Email');
    await userEvent.type(email, 'nuevo@example.test');
    const send = screen.getByRole('button', { name: 'Send invitation' });
    await userEvent.click(send);

    const alert = await within(send.closest('form')).findByRole('alert');
    expect(alert).toHaveTextContent(/current state/i);
    expect(alert).toHaveFocus();
    expect(email).toHaveValue('nuevo@example.test');
    expect(screen.getAllByRole('alert')).toHaveLength(1);
  });

  it('ends the initial invitation skeleton on a retryable failure and retries in the invitation section', async () => {
    let listed = 0;
    const view = renderPage();
    server.use(rolesAre([role()]));
    server.use(http.get(`/api/tenants/${TENANT}/invitations`, () => {
      listed += 1;
      return listed === 1
        ? problem(500, 'internal_server_error', { traceId: 'trace-invitations' })
        : HttpResponse.json(pageOf([invitation()]));
    }));

    const invitationSection = (await screen.findByRole('heading', { name: 'Invitations' })).parentElement;
    const retry = await within(invitationSection).findByRole('button', { name: 'Try again' });
    expect(view.container.querySelector('.MuiSkeleton-root')).toBeNull();

    await userEvent.click(retry);

    expect(await within(invitationSection).findByRole('button', { name: /Resend to nuevo@example.test/ })).toBeInTheDocument();
    expect(listed).toBe(2);
  });

  it('reaches the 26th invitation by page number', async () => {
    server.use(rolesAre([role()]));
    const searches = invitationsServed(numberedInvitations(26));
    renderPage();

    const invitationSection = (await screen.findByRole('heading', { name: 'Invitations' })).parentElement;
    expect(await within(invitationSection).findByText('1–25 of 26')).toBeInTheDocument();
    await userEvent.click(within(invitationSection).getByRole('button', { name: 'Go to next page' }));

    expect(await within(invitationSection).findByText('26–26 of 26')).toBeInTheDocument();
    expect(within(invitationSection).getByRole('button', { name: /Resend to person26@example\.test/ })).toBeInTheDocument();
    expect(within(invitationSection).queryByRole('button', { name: /Resend to person1@example\.test/ })).toBeNull();
    expect(searches).toEqual(['?pageNumber=1&pageSize=25', '?pageNumber=2&pageSize=25']);
  });

  /**
   * A failed page change is answered beside the control that asked for it (AD15). The offers already on screen
   * stay, and "Try again" asks for the page that was requested rather than going back to the first one.
   */
  it('keeps the loaded invitations and retries the requested page beside the page control when a page change fails', async () => {
    const invitationPages = [];
    const all = numberedInvitations(26);
    server.use(rolesAre([role()]));
    server.use(http.get(`/api/tenants/${TENANT}/invitations`, ({ request }) => {
      const { pageNumber, pageSize } = pageAskedFor(request);
      invitationPages.push(pageNumber);
      if (invitationPages.length === 2) return problem(500, 'internal_server_error', { traceId: 'trace-invitation-page' });
      return HttpResponse.json(pageOf(
        all.slice((pageNumber - 1) * pageSize, pageNumber * pageSize),
        { pageNumber, pageSize, totalCount: all.length },
      ));
    }));
    renderPage();

    const invitationSection = (await screen.findByRole('heading', { name: 'Invitations' })).parentElement;
    await userEvent.click(await within(invitationSection).findByRole('button', { name: 'Go to next page' }));

    const retry = await within(invitationSection).findByRole('button', { name: 'Try again' });
    const alert = within(invitationSection).getByRole('alert');
    expect(alert).toHaveTextContent('Something went wrong. Try again.');
    expect(alert).toHaveTextContent('Reference: trace-invitation-page');
    expect(within(invitationSection).getByRole('button', { name: /Resend to person1@example\.test/ })).toBeInTheDocument();
    expect(within(invitationSection).getByText('1–25 of 26').compareDocumentPosition(alert))
      .toBe(Node.DOCUMENT_POSITION_FOLLOWING);

    await userEvent.click(retry);

    expect(await within(invitationSection).findByRole('button', { name: /Resend to person26@example\.test/ })).toBeInTheDocument();
    expect(within(invitationSection).queryByRole('alert')).toBeNull();
    expect(invitationPages).toEqual([1, 2, 2]);
  });

  it('uses the shared network message for a manual resend catch and re-enables the action', async () => {
    renderPage();
    server.use(rolesAre([role()]), invitationsAre([invitation()]));
    server.use(http.post(`/api/tenants/${TENANT}/invitations/invitation-1/resend`, () => HttpResponse.error()));

    await userEvent.click(await screen.findByRole('button', { name: /Resend to nuevo@example.test/ }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'We could not reach the service. Check your connection. If you were saving something, refresh to see whether it was saved before trying again.',
    );
    expect(screen.getByRole('button', { name: /Resend to nuevo@example.test/ })).toBeEnabled();
  });

  /**
   * The form offers roles by name, so it has to know every role the organization has, not the first page of them.
   * Pages are read until the server says there is no next one, and not a request more.
   */
  it('offers every role when the organization has more roles than one page carries', async () => {
    server.use(invitationsAre([]));
    const rolePages = rolesServedTwentyFiveAtATime(numberedRoles(30));
    renderPage();

    const roles = await screen.findByRole('group', { name: 'Roles to offer' });
    expect(await within(roles).findByLabelText('Role 30')).toBeInTheDocument();
    expect(within(roles).getAllByRole('checkbox')).toHaveLength(30);
    expect(rolePages).toEqual([1, 2]);
  });

  it('reports a role catalogue that never ends as an unreadable answer, with a retry and no role offered', async () => {
    const rolePages = [];
    server.use(invitationsAre([]));
    server.use(http.get(`/api/tenants/${TENANT}/roles`, ({ request }) => {
      const { pageNumber } = pageAskedFor(request);
      rolePages.push(pageNumber);
      return HttpResponse.json(pageOf(
        [role({ roleId: `role-${pageNumber}`, name: `Role ${pageNumber}` })],
        { pageNumber, pageSize: 25, totalCount: 10000 },
      ));
    }));
    renderPage();

    const roles = await screen.findByRole('group', { name: 'Roles to offer' });
    expect(await within(roles).findByRole('alert')).toHaveTextContent('We could not read the answer.');
    expect(rolePages).toHaveLength(100);
    expect(rolePages.at(-1)).toBe(100);
    expect(within(roles).getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    expect(within(roles).queryAllByRole('checkbox')).toHaveLength(0);
  });

  it('reports a failed page of the role catalogue in the roles fieldset, and starts the walk again from page 1', async () => {
    const rolePages = [];
    const thirty = numberedRoles(30);
    server.use(invitationsAre([]));
    server.use(http.get(`/api/tenants/${TENANT}/roles`, ({ request }) => {
      const { pageNumber } = pageAskedFor(request);
      rolePages.push(pageNumber);
      if (rolePages.length === 2) return HttpResponse.error();
      return HttpResponse.json(pageOf(
        thirty.slice((pageNumber - 1) * 25, pageNumber * 25),
        { pageNumber, pageSize: 25, totalCount: thirty.length },
      ));
    }));
    renderPage();

    const roles = await screen.findByRole('group', { name: 'Roles to offer' });
    expect(await within(roles).findByRole('alert')).toHaveTextContent('We could not reach the service.');
    expect(within(roles).queryAllByRole('checkbox')).toHaveLength(0);
    expect(rolePages).toEqual([1, 2]);

    await userEvent.click(within(roles).getByRole('button', { name: 'Try again' }));

    expect(await within(roles).findByLabelText('Role 30')).toBeInTheDocument();
    expect(rolePages).toEqual([1, 2, 1, 2]);
  });

  /**
   * Inviting and reading the roster are different permissions, so an inviter who holds only the first still gets
   * a usable form. What they cannot see is said plainly rather than left as an empty list.
   */
  it('still invites when it may not read the roles or the standing offers', async () => {
    renderPage();
    server.use(http.get(`/api/tenants/${TENANT}/roles`, () => problem(403, 'permission_denied')));
    server.use(http.get(`/api/tenants/${TENANT}/invitations`, () => problem(403, 'permission_denied')));

    expect(await screen.findByText(/cannot see this organization’s invitations/i)).toBeInTheDocument();
    expect(screen.getByText(/cannot see this organization’s roles/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send invitation' })).toBeEnabled();
  });

  it('says so rather than failing when the session is in no organization', async () => {
    renderPage(signedInContext({ activeTenant: null }));

    expect(await screen.findByText(/choose an organization first/i)).toBeInTheDocument();
    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
  });
});
