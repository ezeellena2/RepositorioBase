import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { IdentityProvider } from '../context/IdentityProvider';
import { ConfirmEmailPage } from './ConfirmEmailPage';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem } from '../../../test/identityServer';

const withToken = (token) =>
  window.history.replaceState({}, '', `/confirm-email#token=${encodeURIComponent(token)}`);

const renderPage = () => render(
  <MemoryRouter><IdentityProvider><ConfirmEmailPage /></IdentityProvider></MemoryRouter>
);

/**
 * The screen the confirmation mail opens (IA-REQ-005).
 *
 * Before it existed, the delivered link `/confirm-email#token=…` reached the application shell and nothing
 * consumed the token, so an address could only be confirmed by writing to the database. These tests are about the
 * seam that was missing: the token arrives in the fragment, is spent against the real endpoint, and never
 * survives in the address bar.
 */
describe('confirm email page', () => {
  afterEach(() => vi.restoreAllMocks());

  it('spends the token from the fragment and erases it', async () => {
    const submissions = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/confirm-email', async ({ request }) => {
      submissions.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));
    withToken('confirmation-token-1');

    renderPage();
    expect(window.location.hash).toBe('');

    await userEvent.click(await screen.findByRole('button', { name: 'Confirm my address' }));

    await waitFor(() => expect(submissions).toHaveLength(1));
    expect(submissions[0]).toEqual({ token: 'confirmation-token-1' });
    expect(await screen.findByRole('status')).toHaveTextContent(/your address is confirmed/i);
  });

  /** The token is a one-time secret, so it must not be left anywhere a later page or a bookmark could read it. */
  it('leaves the token in no URL and writes it to no browser storage', async () => {
    const stored = [];
    const record = { setItem: (key, value) => stored.push([key, value]) };
    vi.spyOn(window.localStorage, 'setItem').mockImplementation(record.setItem);
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/confirm-email', () => new HttpResponse(null, { status: 204 })));
    withToken('confirmation-token-2');

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Confirm my address' }));

    await screen.findByRole('status');
    expect(window.location.hash).toBe('');
    expect(window.location.search).toBe('');
    expect(stored.flat().join(' ')).not.toContain('confirmation-token-2');
  });

  /** Each confirmation purpose reads out the server's collapsed refusal rather than inventing a success. */
  it.each([
    [400, 'invalid_confirmation', 'That confirmation link is not usable.'],
    [409, 'registration_conflict', 'That organization registration cannot be completed. If you already have an account at this address, sign in; otherwise start registration again.'],
    [409, 'personal_registration_conflict', 'Your personal account could not be created. If this address is confirmed, sign in and try again.'],
  ])('shows the exact %s %s refusal without a success state', async (status, code, message) => {
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/confirm-email', () => problem(status, code)));
    withToken('stale-token');

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Confirm my address' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  /** Arriving without one is a broken link, not a confirmation attempt: nothing is sent. */
  it('sends nothing when the link carried no token', async () => {
    const submissions = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/confirm-email', async ({ request }) => {
      submissions.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));
    window.history.replaceState({}, '', '/confirm-email');

    renderPage();

    expect(await screen.findByRole('button', { name: 'Confirm my address' })).toBeDisabled();
    expect(submissions).toHaveLength(0);
  });
});
