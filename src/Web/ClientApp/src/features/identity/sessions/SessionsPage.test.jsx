import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { IdentityProvider } from '../context/IdentityProvider';
import { SessionsPage } from './SessionsPage';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const renderPage = () => render(
  <MemoryRouter><IdentityProvider><SessionsPage /></IdentityProvider></MemoryRouter>
);

const credentialsAre = (hasPassword) =>
  http.get('/api/identity/credentials', () => HttpResponse.json({ hasPassword, passwordUpdatedAt: null }));

const sessions = () => [
  { sessionRef: 'AAAAAAAAAAAAAAAAAAAAAA', isCurrent: true, deviceLabel: 'Windows', createdAt: '2026-09-06T12:00:00+00:00', lastSeenAt: '2026-09-06T12:30:00+00:00', expiresAt: '2026-09-06T13:00:00+00:00' },
  { sessionRef: 'BBBBBBBBBBBBBBBBBBBBBB', isCurrent: false, deviceLabel: 'Android', createdAt: '2026-09-05T09:00:00+00:00', lastSeenAt: '2026-09-05T09:10:00+00:00', expiresAt: '2026-09-05T10:00:00+00:00' },
];

/**
 * The devices screen. What matters here is the order of two requests: the password buys a server-side proof, and
 * only then is the revocation sent — a page that revoked first would be asking the API to trust a cookie alone.
 */
describe('sessions page', () => {
  it('lists the devices and marks the one being used', async () => {
    server.use(antiforgery(), contextIs(signedInContext()), credentialsAre(true));
    server.use(http.get('/api/identity/sessions', () => HttpResponse.json(sessions())));

    renderPage();

    expect(await screen.findByText('Windows')).toBeInTheDocument();
    expect(screen.getByText('Android')).toBeInTheDocument();
    expect(screen.getByText(/— this device/)).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'End this device' })).toHaveLength(1);
  });

  it('proves the password before it ends another device, and never keeps it', async () => {
    const calls = [];
    server.use(antiforgery(), contextIs(signedInContext()), credentialsAre(true));
    server.use(http.get('/api/identity/sessions', () => HttpResponse.json(sessions())));
    server.use(http.post('/api/identity/credentials/reauthenticate', async ({ request }) => {
      calls.push(['prove', await request.json()]);
      return new HttpResponse(null, { status: 204 });
    }));
    server.use(http.delete('/api/identity/sessions/:sessionRef', ({ params }) => {
      calls.push(['revoke', params.sessionRef]);
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'End this device' }));

    await waitFor(() => expect(calls).toHaveLength(2));
    expect(calls[0]).toEqual(['prove', { action: 'sessions.revoke-one', password: 'Testing1234!' }]);
    expect(calls[1]).toEqual(['revoke', 'BBBBBBBBBBBBBBBBBBBBBB']);
    await waitFor(() => expect(screen.getByLabelText('Password')).toHaveValue(''));
    expect(JSON.stringify(window.localStorage)).not.toContain('Testing1234!');
    expect(JSON.stringify(window.sessionStorage)).not.toContain('Testing1234!');
  });

  it('does not send a revocation when the proof is refused', async () => {
    const calls = [];
    server.use(antiforgery(), contextIs(signedInContext()), credentialsAre(true));
    server.use(http.get('/api/identity/sessions', () => HttpResponse.json(sessions())));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => problem(400, 'invalid_credential_proof')));
    server.use(http.delete('/api/identity/sessions/:sessionRef', () => {
      calls.push('revoke');
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await userEvent.type(await screen.findByLabelText('Password'), 'wrong');
    await userEvent.click(screen.getByRole('button', { name: 'End this device' }));

    const row = screen.getByRole('button', { name: 'End this device' }).closest('li');
    const alert = await within(row).findByRole('alert');
    expect(alert).toHaveFocus();
    expect(screen.getAllByRole('alert')).toHaveLength(1);
    expect(calls).toHaveLength(0);
  });

  it('keeps a revoke-all refusal with the header action that caused it', async () => {
    server.use(antiforgery(), contextIs(signedInContext()), credentialsAre(true));
    server.use(http.get('/api/identity/sessions', () => HttpResponse.json(sessions())));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => new HttpResponse(null, { status: 204 })));
    server.use(http.delete('/api/identity/sessions/others', () => problem(404, 'session_not_found')));

    renderPage();
    await userEvent.type(await screen.findByLabelText('Password'), 'Testing1234!');
    const endAll = screen.getByRole('button', { name: 'End every other device' });
    await userEvent.click(endAll);

    const actionRegion = endAll.parentElement;
    const alert = await within(actionRegion).findByRole('alert');
    expect(alert).toHaveFocus();
    expect(screen.getAllByRole('alert')).toHaveLength(1);
  });

  it('replaces the initial device wait with a retryable error and retries in the list region', async () => {
    let attempts = 0;
    server.use(antiforgery(), contextIs(signedInContext()), credentialsAre(true));
    server.use(http.get('/api/identity/sessions', () => {
      attempts += 1;
      return attempts === 1
        ? problem(500, 'internal_server_error', { traceId: 'trace-sessions' })
        : HttpResponse.json(sessions());
    }));

    renderPage();

    expect(await screen.findByText('Reference: trace-sessions')).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Android')).toBeInTheDocument();
    expect(attempts).toBe(2);
  });

  /**
   * An account created through a provider has no password to prove with, so the screen must not offer a button
   * that can never work. It says what the way out is instead, and that way -- a mailed reset -- needs no proof,
   * which is exactly why it is the one that works here.
   */
  it('tells an account with no password how to get one instead of offering a proof it cannot give', async () => {
    server.use(antiforgery(), contextIs(signedInContext()), credentialsAre(false));
    server.use(http.get('/api/identity/sessions', () => HttpResponse.json(sessions())));

    renderPage();

    expect(await screen.findByText(/Android/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'End this device' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'End every other device' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: /set a password/i }).getAttribute('href')).toBe('/credentials/forgot');
  });

  it('offers no way to end the device being used from this list', async () => {
    server.use(antiforgery(), contextIs(signedInContext()), credentialsAre(true));
    server.use(http.get('/api/identity/sessions', () => HttpResponse.json(sessions())));

    renderPage();

    const buttons = await screen.findAllByRole('button', { name: 'End this device' });
    expect(buttons).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'End every other device' })).toBeDisabled();
  });
});
