import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { IdentityProvider } from '../context/IdentityProvider';
import { ChangePasswordPage, ForgotPasswordPage, ResetPasswordPage } from './PasswordPages';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const renderPage = (page) => render(
  <MemoryRouter><IdentityProvider>{page}</IdentityProvider></MemoryRouter>
);

/**
 * Forgetting a password and changing one, from the browser. The two things that matter are that the reset token
 * never survives in the address bar, and that a change buys its proof before it sends anything.
 */
describe('password pages', () => {
  it('says the same thing whatever address was typed', async () => {
    const asked = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/credentials/password/recovery', async ({ request }) => {
      asked.push(await request.json());
      return new HttpResponse(null, { status: 202 });
    }));

    renderPage(<ForgotPasswordPage />);
    await userEvent.type(screen.getByLabelText('Email'), 'nobody@example.test');
    await userEvent.click(screen.getByRole('button', { name: 'Send the link' }));

    await waitFor(() => expect(asked).toHaveLength(1));
    expect(asked[0]).toEqual({ email: 'nobody@example.test' });
    expect(await screen.findByRole('status')).toHaveTextContent(/we have sent it a reset link/i);
  });

  it('spends the token from the fragment, erases it, and issues no session', async () => {
    const resets = [];
    window.history.replaceState({}, '', '/credentials/reset#token=reset-token-1');
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/credentials/password/reset', async ({ request }) => {
      resets.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage(<ResetPasswordPage />);
    expect(window.location.hash).toBe('');

    await userEvent.type(screen.getByLabelText('New password'), 'Replaced5678!');
    await userEvent.click(screen.getByRole('button', { name: 'Set my password' }));

    await waitFor(() => expect(resets).toHaveLength(1));
    expect(resets[0]).toEqual({ token: 'reset-token-1', newPassword: 'Replaced5678!' });
    expect(await screen.findByRole('status')).toHaveTextContent(/sign in to continue/i);
    expect(JSON.stringify(window.localStorage)).not.toContain('reset-token-1');
  });

  it('refuses to send a reset without the token the link carries', async () => {
    window.history.replaceState({}, '', '/credentials/reset');
    server.use(antiforgery(), contextIs(null));

    renderPage(<ResetPasswordPage />);

    expect(screen.getByRole('button', { name: 'Set my password' })).toBeDisabled();
    expect(screen.getByText(/needs the token it carries/i)).toBeInTheDocument();
  });

  it('buys the proof before it changes anything, and sends no current password to the change', async () => {
    const calls = [];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/credentials/reauthenticate', async ({ request }) => {
      calls.push(['prove', await request.json()]);
      return new HttpResponse(null, { status: 204 });
    }));
    server.use(http.put('/api/identity/credentials/password', async ({ request }) => {
      calls.push(['change', await request.json()]);
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage(<ChangePasswordPage />);
    await userEvent.type(screen.getByLabelText('Current password'), 'Testing1234!');
    await userEvent.type(screen.getByLabelText('New password'), 'Replaced5678!');
    await userEvent.click(screen.getByRole('button', { name: 'Change it' }));

    await waitFor(() => expect(calls).toHaveLength(2));
    expect(calls[0]).toEqual(['prove', { action: 'credentials.password.change', password: 'Testing1234!' }]);
    expect(calls[1]).toEqual(['change', { newPassword: 'Replaced5678!' }]);
    expect(Object.keys(calls[1][1])).not.toContain('currentPassword');
    expect(await screen.findByRole('status')).toHaveTextContent(/other devices have been signed out/i);
  });

  it('changes nothing when the current password is refused', async () => {
    const calls = [];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => problem(400, 'invalid_credential_proof')));
    server.use(http.put('/api/identity/credentials/password', () => {
      calls.push('change');
      return new HttpResponse(null, { status: 204 });
    }));

    renderPage(<ChangePasswordPage />);
    await userEvent.type(screen.getByLabelText('Current password'), 'wrong');
    await userEvent.type(screen.getByLabelText('New password'), 'Replaced5678!');
    await userEvent.click(screen.getByRole('button', { name: 'Change it' }));

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
    expect(calls).toHaveLength(0);
  });
});
