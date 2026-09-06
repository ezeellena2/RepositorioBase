import { render, screen, waitFor } from '@testing-library/react';
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
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/sessions', () => HttpResponse.json(sessions())));

    renderPage();

    expect(await screen.findByText('Windows')).toBeInTheDocument();
    expect(screen.getByText('Android')).toBeInTheDocument();
    expect(screen.getByText(/— this device/)).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'End this device' })).toHaveLength(1);
  });

  it('proves the password before it ends another device, and never keeps it', async () => {
    const calls = [];
    server.use(antiforgery(), contextIs(signedInContext()));
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
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/sessions', () => HttpResponse.json(sessions())));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => problem(400, 'invalid_credential_proof')));
    server.use(http.delete('/api/identity/sessions/:sessionRef', () => {
      calls.push('revoke');
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage();
    await userEvent.type(await screen.findByLabelText('Password'), 'wrong');
    await userEvent.click(screen.getByRole('button', { name: 'End this device' }));

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
    expect(calls).toHaveLength(0);
  });

  it('offers no way to end the device being used from this list', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/sessions', () => HttpResponse.json(sessions())));

    renderPage();

    const buttons = await screen.findAllByRole('button', { name: 'End this device' });
    expect(buttons).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'End every other device' })).toBeDisabled();
  });
});
