import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { IdentityProvider } from '../context/IdentityProvider';
import { ExternalAccountsPage, ExternalReturnPage } from './ExternalAccountsPage';
import { externalNavigation } from '../externalNavigation';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const renderAt = (page, path = '/identity/external') => render(
  <MemoryRouter initialEntries={[path]}><IdentityProvider>{page}</IdentityProvider></MemoryRouter>
);

const linksAre = (rows, available = ['Google']) =>
  http.get('/api/identity/external', () => HttpResponse.json({ items: rows, available }));

const credentialsAre = (hasPassword) =>
  http.get('/api/identity/credentials', () => HttpResponse.json({ hasPassword, passwordUpdatedAt: null }));

/** The browser leaving for the provider is the one thing jsdom cannot really do, so it is watched instead. */
let left;

beforeEach(() => {
  left = [];
  vi.spyOn(externalNavigation, 'leaveFor').mockImplementation((uri) => left.push(uri));
});

/**
 * Provider accounts from the browser (IA-REQ-052, BR-ID-005/006). What matters here is that linking is a
 * deliberate act with a proof behind it, that the page never holds anything the provider issued, and that a
 * refusal to remove the last way in is shown rather than worked around.
 */
describe('external accounts page', () => {
  it('shows a linked provider by the address it asserted, and never by its identifier', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(credentialsAre(true), linksAre([{ provider: 'Google', providerEmail: 'ana@provider.test' }]));

    renderAt(<ExternalAccountsPage />);

    expect(await screen.findByRole('button', { name: 'Unlink Google' })).toBeInTheDocument();
    expect(screen.getByText(/ana@provider.test/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Link Google' })).not.toBeInTheDocument();
  });

  it('buys a proof before it sends anyone to the provider, and sends the consent with the start', async () => {
    const proofs = [];
    const starts = [];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(credentialsAre(true), linksAre([]));
    server.use(http.post('/api/identity/credentials/reauthenticate', async ({ request }) => {
      proofs.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));
    server.use(http.post('/api/identity/external/Google/link/start', async ({ request }) => {
      starts.push(await request.json());
      return HttpResponse.json({ authorizationRequestUri: '/api/identity/external/Google/link/challenge' });
    }));

    renderAt(<ExternalAccountsPage />);
    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Link Google' }));

    await waitFor(() => expect(left).toEqual(['/api/identity/external/Google/link/challenge']));
    expect(proofs).toEqual([{ action: 'external.link', password: 'Testing1234!' }]);
    // Consent is stated in the request, not assumed from the click.
    expect(starts).toEqual([{ consent: true }]);
    expect(screen.getByLabelText('Password')).toHaveValue('');
  });

  it('does not leave for the provider when the proof is refused', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(credentialsAre(true), linksAre([]));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => problem(400, 'invalid_credential_proof')));
    server.use(http.post('/api/identity/external/Google/link/start', () => {
      throw new Error('the link must never be started without a proof');
    }));

    renderAt(<ExternalAccountsPage />);
    await userEvent.type(await screen.findByLabelText('Password'), 'Wrong1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Link Google' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/that password was not accepted/i);
    expect(left).toEqual([]);
  });

  it('shows the refusal when unlinking would leave no way in, and keeps the link on screen', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(credentialsAre(true), linksAre([{ provider: 'Google', providerEmail: 'ana@provider.test' }]));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => new HttpResponse(null, { status: 204 })));
    server.use(http.delete('/api/identity/external/Google', () => problem(409, 'last_authenticator_required')));

    renderAt(<ExternalAccountsPage />);
    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Unlink Google' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/only way to sign in/i);
    expect(screen.getByRole('button', { name: 'Unlink Google' })).toBeInTheDocument();
  });

  /**
   * The refusal the server would give is knowable before the click, so the page says it instead of offering a
   * button whose only possible answer is `last_authenticator_required`.
   */
  it('explains rather than offers when the provider is the only way in', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(credentialsAre(false), linksAre([{ provider: 'Google', providerEmail: 'ana@provider.test' }]));

    renderAt(<ExternalAccountsPage />);

    expect(await screen.findByText(/only way to sign in/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Unlink Google' })).not.toBeInTheDocument();
  });

  it('asks for no password from an account that has none', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(credentialsAre(false), linksAre([{ provider: 'Google', providerEmail: 'ana@provider.test' }]));

    renderAt(<ExternalAccountsPage />);

    await screen.findByText(/only way to sign in/i);
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
  });

  /**
   * Whether a provider exists at all is a server fact: with no client configured its middleware is never
   * registered and every route refuses. Offering it anyway means a person types a password, buys a real proof,
   * and only then learns it was impossible.
   */
  it('offers nothing, and asks for nothing, when the deployment configured no provider', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(credentialsAre(true), linksAre([], []));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => {
      throw new Error('no password may be spent for a provider the deployment does not have');
    }));

    renderAt(<ExternalAccountsPage />);

    expect(await screen.findByText(/no sign-in provider/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Link Google' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
  });

  it('unlinks with a proof and stops showing the provider', async () => {
    let remaining = [{ provider: 'Google', providerEmail: 'ana@provider.test' }];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(credentialsAre(true), http.get('/api/identity/external', () => HttpResponse.json({ items: remaining, available: ['Google'] })));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => new HttpResponse(null, { status: 204 })));
    server.use(http.delete('/api/identity/external/Google', () => {
      remaining = [];
      return new HttpResponse(null, { status: 204 });
    }));

    renderAt(<ExternalAccountsPage />);
    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Unlink Google' }));

    expect(await screen.findByRole('button', { name: 'Link Google' })).toBeInTheDocument();
  });
});

/**
 * The return leg. It carries which round trip finished and nothing else — no code, no token, no address — and
 * every completion is a first-party request this page makes with its own antiforgery pair.
 */
describe('external return page', () => {
  it('completes a sign-in and asks the server for a fresh antiforgery pair', async () => {
    const completions = [];
    let tokens = 0;
    server.use(http.get('/api/identity/antiforgery', () => {
      tokens += 1;
      return HttpResponse.json({ requestToken: `request-token-${tokens}` });
    }));
    server.use(contextIs(signedInContext()));
    server.use(http.post('/api/identity/external/complete', async ({ request }) => {
      completions.push(request.headers.get('X-CSRF-TOKEN'));
      return new HttpResponse(null, { status: 204 });
    }));

    renderAt(<ExternalReturnPage />, '/external/return?outcome=signed_in');

    await waitFor(() => expect(completions).toHaveLength(1));
    // A new session means a new pair, so the page asks for one after completing.
    await waitFor(() => expect(tokens).toBeGreaterThan(1));
  });

  // The completion spends the handoff, so a second call would answer `invalid_external_login` against a round
  // trip that already succeeded — and show a failure that is not one.
  it('completes a link once, however many times the effect is invoked', async () => {
    const called = [];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/external/complete', () => {
      called.push('complete');
      return new HttpResponse(null, { status: 204 });
    }));

    renderAt(<ExternalReturnPage />, '/external/return?outcome=linked');

    await waitFor(() => expect(called).toEqual(['complete']));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows the refusal rather than retrying when the round trip was not accepted', async () => {
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/external/complete', () => problem(400, 'invalid_external_login')));

    renderAt(<ExternalReturnPage />, '/external/return?outcome=signed_in');

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be completed/i);
  });

  /**
   * The slug is written by the server's own redirect, but it arrives in a URL anybody can craft, while the
   * server decides what the completion really did from a cookie it sealed. So the page must not report a
   * security event it did not observe, and must not skip the antiforgery rotation on the strength of a word in
   * the address bar -- a stale pair refuses the very next mutation.
   */
  it('rotates the antiforgery pair after any completion, whatever the address bar says', async () => {
    const order = [];
    server.use(http.get('/api/identity/antiforgery', () => {
      order.push('antiforgery');
      return HttpResponse.json({ requestToken: `request-token-${order.length}` });
    }));
    server.use(contextIs(signedInContext()));
    server.use(http.post('/api/identity/external/complete', () => {
      order.push('complete');
      return new HttpResponse(null, { status: 204 });
    }));

    renderAt(<ExternalReturnPage />, '/external/return?outcome=linked');

    await waitFor(() => expect(order).toContain('complete'));
    await waitFor(() => expect(order.slice(order.indexOf('complete'))).toContain('antiforgery'));
  });

  it('claims nothing about linking that it did not see the server do', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(credentialsAre(true), linksAre([]));

    renderAt(<ExternalAccountsPage />, '/identity/external?outcome=linked');

    await screen.findByRole('button', { name: 'Link Google' });
    expect(screen.queryByText(/other devices have been signed out/i)).not.toBeInTheDocument();
  });

  it('completes nothing at all when the provider leg was refused', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/external/complete', () => {
      throw new Error('a refused round trip must not be completed');
    }));

    renderAt(<ExternalReturnPage />, '/external/return?outcome=refused');

    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
  });
});
