import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { i18n } from '../../../i18n';
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

  it('shows the shared network message and re-enables password recovery after a network failure', async () => {
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/credentials/password/recovery', () => HttpResponse.error()));

    renderPage(<ForgotPasswordPage />);
    await userEvent.type(screen.getByLabelText('Email'), 'nobody@example.test');
    await userEvent.click(screen.getByRole('button', { name: 'Send the link' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'We could not reach the service. Check your connection. If you were saving something, refresh to see whether it was saved before trying again.',
    );
    expect(screen.getByRole('button', { name: 'Send the link' })).toBeEnabled();
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

  it.each([
    ['en', 'New password', 'Set my password', 'Must be at most 12 characters.'],
    ['es', 'Nueva contraseña', 'Establecer mi contraseña', 'Debe tener como máximo 12 caracteres.'],
  ])('associates, focuses and localizes a reset password detail in %s without duplicating it', async (language, label, submit, helper) => {
    await i18n.changeLanguage(language);
    window.history.replaceState({}, '', '/credentials/reset#token=reset-token-validation');
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/credentials/password/reset', () => problem(400, 'validation_failed', {
      errors: { newPassword: [{ code: 'too_long', params: { max: 12 } }] },
    })));

    renderPage(<ResetPasswordPage />);
    const password = await screen.findByLabelText(label);
    await userEvent.type(password, 'candidate');
    await userEvent.click(screen.getByRole('button', { name: submit }));

    await waitFor(() => expect(password).toHaveAttribute('aria-invalid', 'true'));
    expect(password).toHaveAccessibleDescription(helper);
    expect(password).toHaveFocus();
    expect(screen.getByRole('alert')).not.toHaveTextContent(helper);
  });

  it('binds a reset password policy refusal to the password field without duplicating it in the summary', async () => {
    const resets = [];
    window.history.replaceState({}, '', '/credentials/reset#token=reset-token-policy');
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/credentials/password/reset', async ({ request }) => {
      resets.push(await request.json());
      return problem(400, 'validation_failed', {
        status: 400,
        errors: {
          newPassword: [
            { code: 'too_long', params: { max: 12 } },
            { code: 'password_policy', params: {} },
          ],
          request: [{ code: 'invalid', params: {} }],
        },
      });
    }));

    renderPage(<ResetPasswordPage />);
    const password = screen.getByLabelText('New password');
    await userEvent.type(password, 'weak');
    await userEvent.click(screen.getByRole('button', { name: 'Set my password' }));

    await waitFor(() => expect(password).toHaveAttribute('aria-invalid', 'true'));
    expect(resets).toEqual([{ token: 'reset-token-policy', newPassword: 'weak' }]);
    expect(password).toHaveAccessibleDescription(
      'Must be at most 12 characters. This password does not meet the requirements.',
    );
    expect(password).toHaveFocus();
    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Some of what you sent was not accepted. Check the details and try again.');
    expect(alert).toHaveTextContent('This value is not valid.');
    expect(alert).not.toHaveTextContent('Request:');
    expect(alert).not.toHaveTextContent('newPassword');
    expect(alert).not.toHaveTextContent('Must be at most 12 characters.');

    await userEvent.type(password, '!');
    expect(password).toHaveAttribute('aria-invalid', 'false');
    expect(password).not.toHaveAccessibleDescription(/Must be|does not meet/);
    expect(alert).toHaveTextContent('This value is not valid.');
    expect(alert).not.toHaveTextContent('Request:');
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

  it('associates a new-password detail with the new field and focuses that field', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => new HttpResponse(null, { status: 204 })));
    server.use(http.put('/api/identity/credentials/password', () => problem(400, 'validation_failed', {
      errors: {
        newPassword: [
          { code: 'required', params: {} },
          { code: 'password_policy', params: {} },
        ],
      },
    })));

    renderPage(<ChangePasswordPage />);
    const current = await screen.findByLabelText('Current password');
    const next = screen.getByLabelText('New password');
    await userEvent.type(current, 'Testing1234!');
    await userEvent.type(next, 'candidate');
    await userEvent.click(screen.getByRole('button', { name: 'Change it' }));

    await waitFor(() => expect(next).toHaveAttribute('aria-invalid', 'true'));
    expect(next).toHaveAccessibleDescription('A new password is required. This password does not meet the requirements.');
    expect(next).toHaveFocus();
    expect(current).not.toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('alert')).not.toHaveTextContent(/A new password is required|does not meet the requirements/i);
  });

  it('matches password-policy keys case-insensitively and focuses the changed-password field', async () => {
    const calls = [];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/credentials/reauthenticate', async ({ request }) => {
      calls.push(['prove', await request.json()]);
      return new HttpResponse(null, { status: 204 });
    }));
    server.use(http.put('/api/identity/credentials/password', async ({ request }) => {
      calls.push(['change', await request.json()]);
      return problem(400, 'validation_failed', {
        status: 400,
        errors: { NewPassword: [{ code: 'too_long', params: { max: 12 } }] },
      });
    }));

    renderPage(<ChangePasswordPage />);
    await userEvent.type(screen.getByLabelText('Current password'), 'Testing1234!');
    const nextPassword = screen.getByLabelText('New password');
    await userEvent.type(nextPassword, 'weak');
    await userEvent.click(screen.getByRole('button', { name: 'Change it' }));

    await waitFor(() => expect(nextPassword).toHaveAttribute('aria-invalid', 'true'));
    expect(calls).toEqual([
      ['prove', { action: 'credentials.password.change', password: 'Testing1234!' }],
      ['change', { newPassword: 'weak' }],
    ]);
    expect(nextPassword).toHaveAccessibleDescription('Must be at most 12 characters.');
    expect(nextPassword).toHaveFocus();
    expect(screen.getByRole('alert')).not.toHaveTextContent('Must be at most 12 characters.');

    await userEvent.type(nextPassword, '!');
    expect(nextPassword).toHaveAttribute('aria-invalid', 'false');
    expect(screen.getByLabelText('Current password')).toHaveValue('Testing1234!');
  });
});
