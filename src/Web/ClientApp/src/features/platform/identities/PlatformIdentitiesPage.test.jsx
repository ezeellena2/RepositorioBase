import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { IdentityProvider } from '../../identity/context/IdentityProvider';
import { PlatformIdentitiesPage } from './PlatformIdentitiesPage';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const IDENTITY_PERMISSIONS = ['platform.identities.read', 'platform.identities.manage'];

const platformContext = (permissions = IDENTITY_PERMISSIONS, session = {}) => signedInContext({
  activeTenant: { id: 'platform-1', type: 'Platform', name: 'platform' },
  availableTenants: [{ id: 'platform-1', type: 'Platform', name: 'platform' }],
  permissions,
  session: { expiresAt: '2026-12-31T00:00:00Z', requiresTwoFactor: false, ...session },
});

const ACTIVE_ID = '11111111-1111-1111-1111-111111111111';
const STOPPED_ID = '22222222-2222-2222-2222-222222222222';
const CLOSED_ID = '33333333-3333-3333-3333-333333333333';

const identityRow = (overrides = {}) => ({
  identityId: ACTIVE_ID,
  normalizedEmail: 'person@example.test',
  accountStatus: 'Active',
  emailConfirmed: true,
  isLockedOut: false,
  membershipCount: 1,
  mfaStatus: 'None',
  lastSeen: '2026-09-01T00:00:00Z',
  ...overrides,
});

const renderPage = () => render(
  <MemoryRouter><IdentityProvider><PlatformIdentitiesPage /></IdentityProvider></MemoryRouter>,
);

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

/** A directory handler that counts what it served, so "asked for nothing" is an assertion and not a hope. */
const countedDirectory = (items = [identityRow()]) => {
  const reads = { count: 0 };
  const handler = http.get('/api/platform/identities', () => {
    reads.count += 1;
    return HttpResponse.json(pageOf(items));
  });
  return [reads, handler];
};

/** The API's side of the directory: every account served a page at a time, with each query string it was asked with. */
const identitiesServed = (all) => {
  const searches = [];
  server.use(antiforgery(), contextIs(platformContext()), http.get('/api/platform/identities', ({ request }) => {
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

const numberedIdentities = (count) => Array.from({ length: count }, (_, index) => identityRow({
  identityId: `identity-${index + 1}`,
  normalizedEmail: `account-${index + 1}@example.test`,
}));

/** A directory whose answer is chosen page by page, with each page number it was asked for. */
const pagesServed = (answer) => {
  const requested = [];
  server.use(antiforgery(), contextIs(platformContext()), http.get('/api/platform/identities', ({ request }) => {
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

/**
 * The operator directory of accounts (IA-REQ-054).
 *
 * Two gates answer with the same code and the same document — `401 recent_mfa_required` — and mean different
 * things. Reading the directory needs a session that proved the second factor at all; changing one account needs a
 * recent proof. Nothing on the wire separates them, so most of what follows is about the screen telling them apart
 * from `requiresTwoFactor` and never replaying the change it was refused.
 */
describe('platform identities page', () => {
  it('asks for the second factor instead of reading the directory', async () => {
    const [reads, directory] = countedDirectory();
    server.use(antiforgery(), contextIs(platformContext(IDENTITY_PERMISSIONS, { requiresTwoFactor: true })), directory);

    renderPage();

    expect(await screen.findByRole('form', { name: 'Step up' })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(reads.count).toBe(0);
  });

  /**
   * The one branch that makes the reload worth doing. A step-up the server accepted is not the same statement as
   * a session that has settled its factor, and a screen that reads success off the request it just sent would put
   * the directory in front of somebody the API is still going to refuse.
   */
  it('keeps the gate up when the reloaded context still owes the factor', async () => {
    const [reads, directory] = countedDirectory();
    let stepUps = 0;
    server.use(antiforgery(), directory);
    server.use(http.get('/api/identity/context', () =>
      HttpResponse.json(platformContext(IDENTITY_PERMISSIONS, { requiresTwoFactor: true }))));
    server.use(http.post('/api/platform/mfa/step-up', () => {
      stepUps += 1;
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await userEvent.type(await screen.findByLabelText('Authenticator code'), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Step up' }));

    await waitFor(() => expect(stepUps).toBe(1));
    expect(screen.getByRole('form', { name: 'Step up' })).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(reads.count).toBe(0);
  });

  it('keeps an invalid authenticator code on its field without losing the authenticated screen', async () => {
    const [reads, directory] = countedDirectory();
    server.use(antiforgery(), contextIs(platformContext(IDENTITY_PERMISSIONS, { requiresTwoFactor: true })), directory);
    server.use(http.post('/api/platform/mfa/step-up', () => problem(400, 'invalid_mfa_code')));

    renderPage();
    const code = await screen.findByLabelText('Authenticator code');
    await userEvent.type(code, '000000');
    await userEvent.click(screen.getByRole('button', { name: 'Step up' }));

    const refusal = 'That authenticator code was not accepted. Check the code and try again.';
    expect(await screen.findAllByText(refusal)).toHaveLength(1);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(code).toHaveAttribute('aria-invalid', 'true');
    expect(code).toHaveAccessibleDescription(refusal);
    expect(code).toHaveFocus();
    expect(screen.getByRole('heading', { name: 'Identities' })).toBeInTheDocument();
    expect(screen.getByRole('form', { name: 'Step up' })).toBeInTheDocument();
    expect(reads.count).toBe(0);

    await userEvent.type(code, '1');

    expect(code).not.toHaveAttribute('aria-invalid', 'true');
    expect(code).not.toHaveAccessibleDescription(refusal);
    expect(screen.queryByText(refusal)).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('associates and focuses a structured step-up code detail without repeating it', async () => {
    const [reads, directory] = countedDirectory();
    server.use(antiforgery(), contextIs(platformContext(IDENTITY_PERMISSIONS, { requiresTwoFactor: true })), directory);
    server.use(http.post('/api/platform/mfa/step-up', () => problem(400, 'validation_failed', {
      errors: { code: [{ code: 'too_long', params: { max: 16 } }] },
    })));

    renderPage();
    const code = await screen.findByLabelText('Authenticator code');
    await userEvent.type(code, '000000');
    await userEvent.click(screen.getByRole('button', { name: 'Step up' }));

    await waitFor(() => expect(code).toHaveAttribute('aria-invalid', 'true'));
    expect(code).toHaveAccessibleDescription('Must be at most 16 characters.');
    expect(code).toHaveFocus();
    expect(screen.getByRole('alert')).not.toHaveTextContent('Must be at most 16 characters.');
    expect(reads.count).toBe(0);
  });

  it('opens the directory once the reloaded context says the factor is settled', async () => {
    let requiresTwoFactor = true;
    server.use(antiforgery());
    server.use(http.get('/api/identity/context', () =>
      HttpResponse.json(platformContext(IDENTITY_PERMISSIONS, { requiresTwoFactor }))));
    server.use(http.get('/api/platform/identities', () => HttpResponse.json(pageOf([identityRow()]))));
    server.use(http.post('/api/platform/mfa/step-up', () => {
      requiresTwoFactor = false;
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await userEvent.type(await screen.findByLabelText('Authenticator code'), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Step up' }));

    expect(await screen.findByRole('table')).toBeInTheDocument();
    expect(screen.getByText('person@example.test')).toBeInTheDocument();
  });

  /**
   * The other gate. The session proved its factor, so the rows were readable and are on screen; what was refused
   * is the change. Clearing the table would throw away an answer the server never withdrew.
   */
  it('keeps the rows and takes the confirmation down when a change needs a recent proof', async () => {
    const [, directory] = countedDirectory();
    const suspensions = [];
    server.use(antiforgery(), contextIs(platformContext()), directory);
    server.use(http.post('/api/platform/identities/:identityId/suspend', async ({ request }) => {
      suspensions.push(await request.json());
      return problem(401, 'recent_mfa_required');
    }));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Suspend person@example.test' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm suspension' }));

    expect(await screen.findByRole('form', { name: 'Step up' })).toBeInTheDocument();
    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(screen.getByText('person@example.test')).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Confirm suspension' })).not.toBeInTheDocument();
    expect(await screen.findByRole('alert')).toHaveTextContent(/second factor again/i);
    expect(screen.queryByText(/recent_mfa_required/)).not.toBeInTheDocument();
    expect(suspensions).toHaveLength(1);
  });

  /**
   * The refused change is never replayed by the gate that unblocked it. Proving a factor is not the same act as
   * asking to stop somebody's account, and the second one is the operator's to repeat deliberately.
   */
  it('sends no second suspension when the step-up that follows the refusal succeeds', async () => {
    const suspensions = [];
    // The row moves while the operator is stepping up, so a re-read is visible as a different affordance rather
    // than as a call count — which would only be counting how often the shared client happens to be rebuilt.
    let stoppedElsewhere = false;
    server.use(antiforgery(), contextIs(platformContext()));
    server.use(http.get('/api/platform/identities', () => HttpResponse.json(pageOf(
      [identityRow({ accountStatus: stoppedElsewhere ? 'AdministrativelySuspended' : 'Active' })],
    ))));
    server.use(http.post('/api/platform/identities/:identityId/suspend', async ({ request }) => {
      suspensions.push(await request.json());
      return problem(401, 'recent_mfa_required');
    }));
    server.use(http.post('/api/platform/mfa/step-up', () => {
      stoppedElsewhere = true;
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Suspend person@example.test' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm suspension' }));

    await userEvent.type(await screen.findByLabelText('Authenticator code'), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Step up' }));

    expect(await screen.findByRole('button', { name: 'Reactivate person@example.test' })).toBeInTheDocument();
    expect(suspensions).toHaveLength(1);
    expect(screen.queryByRole('form', { name: 'Step up' })).not.toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Confirm suspension' })).not.toBeInTheDocument();
  });

  /** Every refusal a step-up could not clear. Offering one would be offering a ceremony that changes nothing. */
  it.each([
    [403, 'permission_denied', /do not have permission/i],
    [409, 'identity_concurrency_conflict', /changed while you were working/i],
    [409, 'last_administrator_required', /no administrator/i],
    [429, 'rate_limit_exceeded', /too many attempts/i],
    [404, 'not_found', /not available/i],
    [409, 'platform_last_owner', /no owner/i],
    [400, 'invalid_platform_operation', /not valid in its current state/i],
  ])('offers no step-up when a suspension is refused with %i %s', async (status, code, message) => {
    const [, directory] = countedDirectory();
    server.use(antiforgery(), contextIs(platformContext()), directory);
    server.use(http.post('/api/platform/identities/:identityId/suspend', () => problem(status, code)));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Suspend person@example.test' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm suspension' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(screen.queryByRole('form', { name: 'Step up' })).not.toBeInTheDocument();
    expect(screen.queryByText(code)).not.toBeInTheDocument();
  });

  it('offers no step-up when a reactivation is refused as unavailable', async () => {
    const [, directory] = countedDirectory([identityRow({
      identityId: STOPPED_ID, normalizedEmail: 'stopped@example.test', accountStatus: 'AdministrativelySuspended',
    })]);
    server.use(antiforgery(), contextIs(platformContext()), directory);
    server.use(http.post('/api/platform/identities/:identityId/reactivate', () =>
      problem(403, 'identity_reactivation_unavailable')));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Reactivate stopped@example.test' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm reactivation' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/cannot be reactivated/i);
    expect(screen.queryByRole('form', { name: 'Step up' })).not.toBeInTheDocument();
  });

  /** The account moved under the operator. Re-sending the same precondition would only be refused again. */
  it('re-reads the directory and sends nothing more when the account has moved', async () => {
    const [reads, directory] = countedDirectory();
    const suspensions = [];
    server.use(antiforgery(), contextIs(platformContext()), directory);
    server.use(http.post('/api/platform/identities/:identityId/suspend', async ({ request }) => {
      suspensions.push(await request.json());
      return problem(409, 'identity_concurrency_conflict');
    }));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Suspend person@example.test' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm suspension' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/account changed while you were working/i);
    await waitFor(() => expect(reads.count).toBe(2));
    expect(suspensions).toHaveLength(1);
    expect(screen.queryByRole('form', { name: 'Confirm suspension' })).not.toBeInTheDocument();
  });

  /**
   * `expectedStatus` is the state the operator was looking at when they armed the confirmation, sent back
   * verbatim. Defaulting it to `Active`, or re-reading it at submit time, would make the precondition agree with
   * whatever arrived in the meantime — which is the disagreement it exists to catch.
   */
  it('sends the account status the row showed when the suspension was armed', async () => {
    const [, directory] = countedDirectory([identityRow({
      normalizedEmail: 'parked@example.test', accountStatus: 'SelfDeactivated',
    })]);
    const suspensions = [];
    server.use(antiforgery(), contextIs(platformContext()), directory);
    server.use(http.post('/api/platform/identities/:identityId/suspend', async ({ request, params }) => {
      suspensions.push({ identityId: params.identityId, body: await request.json() });
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Suspend parked@example.test' }));
    await userEvent.click(screen.getByRole('combobox', { name: 'Reason' }));
    await userEvent.click(screen.getByRole('option', { name: 'Security incident' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm suspension' }));

    await waitFor(() => expect(suspensions).toHaveLength(1));
    expect(suspensions[0]).toEqual({
      identityId: ACTIVE_ID,
      body: { reason: 'SecurityIncident', expectedStatus: 'SelfDeactivated' },
    });
  });

  it('sends the account status the row showed when the reactivation was armed', async () => {
    const [, directory] = countedDirectory([identityRow({
      identityId: STOPPED_ID, normalizedEmail: 'stopped@example.test', accountStatus: 'AdministrativelySuspended',
    })]);
    const reactivations = [];
    server.use(antiforgery(), contextIs(platformContext()), directory);
    server.use(http.post('/api/platform/identities/:identityId/reactivate', async ({ request, params }) => {
      reactivations.push({ identityId: params.identityId, body: await request.json() });
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Reactivate stopped@example.test' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm reactivation' }));

    await waitFor(() => expect(reactivations).toHaveLength(1));
    expect(reactivations[0]).toEqual({
      identityId: STOPPED_ID,
      body: { expectedStatus: 'AdministrativelySuspended', acknowledgeSelfDeactivation: false },
    });
  });

  /** An action the server can only refuse is a lie told by the screen, so the row offers what the state allows. */
  it('offers only the transition the account status allows, and shows the identifier', async () => {
    const [, directory] = countedDirectory([
      identityRow({ normalizedEmail: 'active@example.test', accountStatus: 'Active' }),
      identityRow({ identityId: STOPPED_ID, normalizedEmail: 'stopped@example.test', accountStatus: 'AdministrativelySuspended' }),
      identityRow({ identityId: CLOSED_ID, normalizedEmail: 'closed@example.test', accountStatus: 'Closed' }),
    ]);
    server.use(antiforgery(), contextIs(platformContext()), directory);

    renderPage();

    expect(await screen.findByRole('button', { name: 'Suspend active@example.test' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reactivate active@example.test' })).not.toBeInTheDocument();

    expect(screen.getByRole('button', { name: 'Reactivate stopped@example.test' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Suspend stopped@example.test' })).not.toBeInTheDocument();

    expect(screen.queryByRole('button', { name: /closed@example\.test/ })).not.toBeInTheDocument();

    expect(screen.getByText(ACTIVE_ID)).toBeInTheDocument();
    expect(screen.getAllByRole('columnheader').every((header) => header.getAttribute('scope') === 'col')).toBe(true);
  });

  /**
   * The same lie told about the caller rather than the row. Reading the directory and changing an account are
   * separately granted, so an operator holding only the first is shown the accounts and offered nothing.
   */
  it('reads the directory without offering a transition it may not perform', async () => {
    const [, directory] = countedDirectory([
      identityRow({ normalizedEmail: 'active@example.test', accountStatus: 'Active' }),
      identityRow({ identityId: STOPPED_ID, normalizedEmail: 'stopped@example.test', accountStatus: 'AdministrativelySuspended' }),
    ]);
    server.use(antiforgery(), contextIs(platformContext(['platform.identities.read'])), directory);

    renderPage();

    expect(await screen.findByText('active@example.test')).toBeInTheDocument();
    expect(screen.getByText('stopped@example.test')).toBeInTheDocument();

    expect(screen.queryByRole('button', { name: 'Suspend active@example.test' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reactivate stopped@example.test' })).not.toBeInTheDocument();
  });

  /**
   * The server answers one bounded page and says how many accounts there are in all. The screen offers no search,
   * so an account past the first page is reachable only through the page control — and only if the page asked for
   * is the page the server is sent.
   */
  it('reaches the 26th account with the next page and returns with the previous one', async () => {
    const searches = identitiesServed(numberedIdentities(26));
    renderPage();

    expect(await screen.findByText('1–25 of 26')).toBeInTheDocument();
    expect(screen.getByText('account-1@example.test')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Go to next page' }));

    expect(await screen.findByText('26–26 of 26')).toBeInTheDocument();
    expect(screen.getByText('account-26@example.test')).toBeInTheDocument();
    expect(screen.queryByText('account-1@example.test')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Go to previous page' }));

    expect(await screen.findByText('1–25 of 26')).toBeInTheDocument();
    expect(screen.getByText('account-1@example.test')).toBeInTheDocument();
    expect(searches).toEqual(['?pageNumber=1&pageSize=25', '?pageNumber=2&pageSize=25', '?pageNumber=1&pageSize=25']);
  });

  it('restarts at the first page when the rows per page change', async () => {
    const searches = identitiesServed(numberedIdentities(60));
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));
    expect(await screen.findByText('26–50 of 60')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('combobox', { name: /rows per page/i }));
    await userEvent.click(screen.getByRole('option', { name: '50' }));

    expect(await screen.findByText('1–50 of 60')).toBeInTheDocument();
    expect(screen.getByText('account-50@example.test')).toBeInTheDocument();
    expect(searches).toEqual(['?pageNumber=1&pageSize=25', '?pageNumber=2&pageSize=25', '?pageNumber=1&pageSize=50']);
  });

  it('disables both page buttons when every account fits on one page', async () => {
    const searches = identitiesServed(numberedIdentities(3));
    renderPage();

    expect(await screen.findByText('1–3 of 3')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go to next page' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Go to previous page' })).toBeDisabled();
    expect(searches).toEqual(['?pageNumber=1&pageSize=25']);
  });

  it('keeps the empty state and draws no page control when no account is listed', async () => {
    identitiesServed([]);
    renderPage();

    expect(await screen.findByText(/no accounts are listed/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Go to next page' })).not.toBeInTheDocument();
    expect(screen.queryByText(/rows per page/i)).not.toBeInTheDocument();
  });

  /** A page change holds what is on screen: the loaded rows and their range stay until the page asked for arrives. */
  it('keeps the loaded page and its range on screen while the next page is on its way', async () => {
    let releaseNextPage;
    const nextPage = new Promise((resolve) => { releaseNextPage = resolve; });
    const all = numberedIdentities(26);
    const asked = [];
    server.use(antiforgery(), contextIs(platformContext()), http.get('/api/platform/identities', ({ request }) => {
      const pageNumber = Number(new URL(request.url).searchParams.get('pageNumber'));
      asked.push(pageNumber);
      return pageNumber === 2 ? nextPage : HttpResponse.json(pageOf(all.slice(0, 25), { totalCount: 26 }));
    }));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));
    await waitFor(() => expect(asked).toEqual([1, 2]));

    expect(screen.getByRole('progressbar', { name: 'Loading the directory…' })).toBeInTheDocument();
    expect(screen.getByText('account-1@example.test')).toBeInTheDocument();
    expect(screen.getByText('1–25 of 26')).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();

    releaseNextPage(HttpResponse.json(pageOf(all.slice(25), { pageNumber: 2, totalCount: 26 })));

    expect(await screen.findByText('account-26@example.test')).toBeInTheDocument();
    expect(screen.getByText('26–26 of 26')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
  });

  it('keeps the rows and the control on the loaded page when the next page cannot be reached', async () => {
    const thirty = numberedIdentities(30);
    const requested = pagesServed((pageNumber) => (pageNumber === 1
      ? HttpResponse.json(pageOf(thirty.slice(0, 25), { totalCount: 30 }))
      : HttpResponse.error()));
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('We could not reach the service.');
    expect(screen.getByText('account-1@example.test')).toBeInTheDocument();
    expect(screen.getByText('1–25 of 30')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(requested).toEqual([1, 2, 2]));
  });

  it('clears the rows and explains a refused page in place', async () => {
    const thirty = numberedIdentities(30);
    const requested = pagesServed((pageNumber) => (pageNumber === 1
      ? HttpResponse.json(pageOf(thirty.slice(0, 25), { totalCount: 30 }))
      : problem(403, 'permission_denied')));
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Go to next page' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('You do not have permission to do that here.');
    expect(screen.queryByText('account-1@example.test')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Go to next page' })).not.toBeInTheDocument();
    expect(requested).toEqual([1, 2]);
  });

  it('moves to the real last page when the answered page is past the end, and never calls the directory empty', async () => {
    const thirty = numberedIdentities(30);
    // The first answer is the stale page an operator lands on after the last accounts on it went elsewhere.
    const requested = pagesServed((pageNumber) => HttpResponse.json(pageNumber === 2
      ? pageOf(thirty.slice(25), { pageNumber: 2, totalCount: 30 })
      : pageOf([], { pageNumber: 3, totalCount: 30 })));
    const text = watchShownText();
    renderPage();

    expect(await screen.findByText('26–30 of 30')).toBeInTheDocument();
    text.stop();
    expect(screen.getByText('account-30@example.test')).toBeInTheDocument();
    expect(requested).toEqual([1, 2]);
    expect(text.saw('No accounts are listed')).toBe(false);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('asks once and keeps the empty state when there is no page to correct', async () => {
    const requested = pagesServed(() => HttpResponse.json(pageOf([])));
    renderPage();

    expect(await screen.findByText(/no accounts are listed/i)).toBeInTheDocument();
    // Long enough for a correction to have been sent: the past-the-end test above sees its second request sooner.
    await new Promise((resolve) => { setTimeout(resolve, 50); });
    expect(requested).toEqual([1]);
    expect(screen.getByText(/no accounts are listed/i)).toBeInTheDocument();
  });

  it('renders only the last page asked for when clicks outrun the network', async () => {
    const hundred = numberedIdentities(100);
    let releaseSecondPage;
    const secondPageHeld = new Promise((resolve) => { releaseSecondPage = resolve; });
    const answered = [];
    server.use(antiforgery(), contextIs(platformContext()), http.get('/api/platform/identities', async ({ request }) => {
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
    expect(screen.getByText('account-50@example.test')).toBeInTheDocument();
    expect(screen.queryByText('26–50 of 100')).not.toBeInTheDocument();
  });

  /**
   * The acknowledgement is the operator saying they know the account will land back where its owner put it. It
   * starts unchecked, stays unchecked across a retry, and nothing but the checkbox can set it.
   */
  it('sends the acknowledgement only when it was ticked, and explains the refusal that asks for it', async () => {
    const [, directory] = countedDirectory([identityRow({
      identityId: STOPPED_ID, normalizedEmail: 'stopped@example.test', accountStatus: 'AdministrativelySuspended',
    })]);
    const reactivations = [];
    server.use(antiforgery(), contextIs(platformContext()), directory);
    server.use(http.post('/api/platform/identities/:identityId/reactivate', async ({ request }) => {
      reactivations.push(await request.json());
      return reactivations.length < 3
        ? problem(400, 'invalid_platform_operation')
        : new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Reactivate stopped@example.test' }));

    const acknowledgement = screen.getByLabelText(/deactivated rather than active/i);
    expect(acknowledgement).not.toBeChecked();

    await userEvent.click(screen.getByRole('button', { name: 'Confirm reactivation' }));
    expect(await screen.findByText(/parked by the person who owns it/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/deactivated rather than active/i)).not.toBeChecked();

    await userEvent.click(screen.getByRole('button', { name: 'Confirm reactivation' }));
    await waitFor(() => expect(reactivations).toHaveLength(2));

    await userEvent.click(screen.getByLabelText(/deactivated rather than active/i));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm reactivation' }));

    await waitFor(() => expect(reactivations).toHaveLength(3));
    expect(reactivations.map((body) => body.acknowledgeSelfDeactivation)).toEqual([false, false, true]);
    expect(screen.queryByRole('form', { name: 'Confirm reactivation' })).not.toBeInTheDocument();
  });

  it('says it is loading before the answer arrives', async () => {
    let release;
    const held = new Promise((resolve) => { release = resolve; });
    server.use(antiforgery(), contextIs(platformContext()));
    server.use(http.get('/api/platform/identities', async () => {
      await held;
      return HttpResponse.json(pageOf([]));
    }));

    renderPage();

    expect(await screen.findByRole('status')).toHaveTextContent(/loading/i);
    release();
    expect(await screen.findByText(/no accounts are listed/i)).toBeInTheDocument();
  });

  it('says an empty directory is empty rather than showing a refusal', async () => {
    const [, directory] = countedDirectory([]);
    server.use(antiforgery(), contextIs(platformContext()), directory);

    renderPage();

    expect(await screen.findByText(/no accounts are listed/i)).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('says why the directory could not be read rather than showing an empty one', async () => {
    server.use(antiforgery(), contextIs(platformContext()));
    server.use(http.get('/api/platform/identities', () => problem(403, 'permission_denied')));

    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent(/do not have permission/i);
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByText(/no accounts are listed/i)).not.toBeInTheDocument();
  });

  it('offers a retry and one support reference when a valid server 500 refuses the read', async () => {
    let attempts = 0;
    server.use(antiforgery(), contextIs(platformContext()));
    server.use(http.get('/api/platform/identities', () => {
      attempts += 1;
      return attempts === 1
        ? problem(500, 'internal_server_error', { traceId: 'trace-directory' })
        : HttpResponse.json(pageOf([identityRow()]));
    }));

    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent(/something went wrong/i);
    expect(screen.getAllByText('Reference: trace-directory')).toHaveLength(1);
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('person@example.test')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('names the permission it needs and asks for nothing without it', async () => {
    const [reads, directory] = countedDirectory();
    server.use(antiforgery(), contextIs(platformContext(['platform.organizations.read'])), directory);

    renderPage();

    expect(await screen.findByText('This screen is for a Platform administrator holding platform.identities.read.')).toBeInTheDocument();
    expect(screen.getByText('View identities', { exact: true })).toBeVisible();
    expect(screen.getByText('platform.identities.read', { exact: true })).toBeVisible();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Step up' })).not.toBeInTheDocument();
    expect(reads.count).toBe(0);
  });

  it('asks for nothing when the session is not operating as Platform', async () => {
    const [reads, directory] = countedDirectory();
    server.use(antiforgery(), contextIs(signedInContext()), directory);

    renderPage();

    expect(await screen.findByText('This screen is for a Platform administrator holding platform.identities.read.')).toBeInTheDocument();
    expect(screen.getByText('View identities', { exact: true })).toBeVisible();
    expect(screen.getByText('platform.identities.read', { exact: true })).toBeVisible();
    expect(reads.count).toBe(0);
  });

  /** The acting tenant comes from the session. A request that carried one would be asking to act as somebody. */
  it('carries no tenant identifier on anything it sends', async () => {
    const urls = [];
    const bodies = [];
    server.use(antiforgery(), contextIs(platformContext()));
    server.use(http.get('/api/platform/identities', ({ request }) => {
      urls.push(request.url);
      return HttpResponse.json(pageOf([identityRow()]));
    }));
    server.use(http.post('/api/platform/identities/:identityId/suspend', async ({ request }) => {
      urls.push(request.url);
      bodies.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Suspend person@example.test' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirm suspension' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(urls.some((url) => /tenant|platform-1/i.test(url))).toBe(false);
    expect(bodies.some((body) => /tenant/i.test(JSON.stringify(body)))).toBe(false);
  });

  it('offers no impersonation, deletion, purge or way to choose a tenant to act as', async () => {
    const [, directory] = countedDirectory();
    server.use(antiforgery(), contextIs(platformContext()), directory);

    renderPage();
    await screen.findByText('person@example.test');

    const labels = screen.getAllByRole('button').map((button) => button.textContent.toLowerCase());
    expect(labels.some((label) => label.includes('impersonat'))).toBe(false);
    expect(labels.some((label) => label.includes('delete'))).toBe(false);
    expect(labels.some((label) => label.includes('purge'))).toBe(false);
    expect(labels.some((label) => label.includes('act as'))).toBe(false);
    expect(screen.queryByLabelText(/act as|acting tenant/i)).not.toBeInTheDocument();
  });
});
