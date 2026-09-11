import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import App from '../../../App';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const open = (path) => render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>);
const credentials = () => server.use(
  http.get('/api/identity/credentials', () => HttpResponse.json({ hasPassword: true, passwordUpdatedAt: null })),
  http.get('/api/identity/external', () => HttpResponse.json({ items: [], available: [] })),
);

describe('self-account lifecycle', () => {
  it.each([
    ['en', 'English'],
    [null, 'Not set'],
  ])('shows the account preference %s without adding another editor', async (preferredLanguage, expected) => {
    server.use(antiforgery(), contextIs(signedInContext({ preferredLanguage })));
    credentials();
    open('/identity/account');

    expect(await screen.findByText('Preferred language')).toBeInTheDocument();
    expect(screen.getByText(expected, { selector: 'dd' })).toBeInTheDocument();
    expect(screen.getAllByRole('combobox', { name: 'Language' })).toHaveLength(1);
  });

  it('reaches deactivation from navigation, proves before the empty command, and refreshes antiforgery for the public request', async () => {
    const calls = [];
    let bootstraps = 0;
    server.use(contextIs(signedInContext()), http.get('/api/identity/antiforgery', () => {
      bootstraps += 1;
      return HttpResponse.json({ requestToken: `pair-${bootstraps}` });
    }));
    credentials();
    server.use(http.post('/api/identity/credentials/reauthenticate', async ({ request }) => {
      calls.push(['proof', await request.json()]);
      return new HttpResponse(null, { status: 204 });
    }), http.post('/api/identity/account/deactivate', async ({ request }) => {
      calls.push(['deactivate', await request.text()]);
      return new HttpResponse(null, { status: 204 });
    }), http.post('/api/identity/account/reactivation-requests', async ({ request }) => {
      calls.push(['request', await request.json(), request.headers.get('X-CSRF-TOKEN')]);
      return new HttpResponse(null, { status: 202 });
    }));
    open('/identity');
    await userEvent.click(await screen.findByRole('link', { name: 'Your account' }));
    const deactivate = await screen.findByRole('button', { name: 'Deactivate my account' });
    expect(deactivate).toBeDisabled();
    await userEvent.type(screen.getByLabelText('Current password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('checkbox'));
    await userEvent.click(deactivate);
    await screen.findByText(/your account is deactivated/i);
    expect(screen.queryByRole('link', { name: 'Your profile' })).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Email'), 'ana@example.test');
    await userEvent.click(screen.getByRole('button', { name: 'Send reactivation link' }));
    await screen.findByText(/if that account can be reactivated/i);
    expect(calls).toEqual([
      ['proof', { action: 'identity.account.deactivate', password: 'Testing1234!' }],
      ['deactivate', ''],
      ['request', { email: 'ana@example.test' }, 'pair-2'],
    ]);
  });

  it('keeps the account signed in when its proof is refused', async () => {
    let deactivations = 0;
    server.use(antiforgery(), contextIs(signedInContext()));
    credentials();
    server.use(http.post('/api/identity/credentials/reauthenticate', () => problem(400, 'invalid_credential_proof')),
      http.post('/api/identity/account/deactivate', () => { deactivations += 1; return new HttpResponse(null, { status: 204 }); }));
    open('/identity/account');
    await userEvent.type(await screen.findByLabelText('Current password'), 'wrong');
    await userEvent.click(screen.getByRole('checkbox'));
    await userEvent.click(screen.getByRole('button', { name: 'Deactivate my account' }));
    await screen.findByRole('alert');
    expect(deactivations).toBe(0);
    expect(screen.getByRole('link', { name: 'Your profile' })).toBeInTheDocument();
  });

  it('offers a neutral public request from login', async () => {
    server.use(antiforgery(), contextIs(null), http.post('/api/identity/account/reactivation-requests', () => new HttpResponse(null, { status: 202 })));
    open('/login');
    await userEvent.click(await screen.findByRole('link', { name: 'Reactivate your account' }));
    await userEvent.type(screen.getByLabelText('Email'), 'unknown@example.test');
    await userEvent.click(screen.getByRole('button', { name: 'Send reactivation link' }));
    await screen.findByText(/if that account can be reactivated/i);
  });

  it('spends the fragment with a password once, erases it, and requires a separate sign-in', async () => {
    const returns = [];
    window.history.replaceState({}, '', '/account/reactivate#token=mailed-ticket');
    server.use(antiforgery(), contextIs(null), http.post('/api/identity/account/reactivate', async ({ request }) => {
      returns.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));
    open('/account/reactivate');
    await userEvent.type(await screen.findByLabelText('Current password'), 'Testing1234!');
    expect(window.location.hash).toBe('');
    await userEvent.click(screen.getByRole('button', { name: 'Reactivate my account' }));
    await screen.findByText(/your account is active again/i);
    await waitFor(() => expect(returns).toEqual([{ reactivationToken: 'mailed-ticket', password: 'Testing1234!' }]));
    expect(screen.queryByRole('link', { name: 'Your profile' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reactivate my account' })).not.toBeInTheDocument();
    expect(JSON.stringify(window.localStorage) + JSON.stringify(window.sessionStorage)).not.toMatch(/mailed-ticket|Testing1234/);
  });
});
