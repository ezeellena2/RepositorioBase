import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { IdentityProvider } from '../../identity/context/IdentityProvider';
import { MfaRecoveryPage } from './MfaRecoveryPage';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const renderPage = () => render(<MemoryRouter><IdentityProvider><MfaRecoveryPage /></IdentityProvider></MemoryRouter>);

const replacement = {
  sharedKey: 'JBSWY3DPEHPK3PXP',
  provisioningUri: 'otpauth://totp/Platform:owner@example.test?secret=JBSWY3DPEHPK3PXP',
  recoveryCodes: ['aaaa-1111', 'bbbb-2222'],
};

/**
 * Replacing a second factor whose authenticator is gone (IA-REQ-041, C6).
 *
 * The two things that matter on this screen: it takes a password and a code and keeps neither, and what it is
 * handed back is shown once. There is no route that reads a shared key or a recovery code again, so a screen that
 * cached either would be the only place they still existed.
 */
describe('platform mfa recovery', () => {
  it('proves the password for this action alone, then spends the code, and shows the replacement once', async () => {
    const proofs = [];
    const recoveries = [];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/credentials/reauthenticate', async ({ request }) => {
      proofs.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));
    server.use(http.post('/api/platform/mfa/recover', async ({ request }) => {
      recoveries.push(await request.json());
      return HttpResponse.json(replacement);
    }));

    renderPage();
    await userEvent.type(await screen.findByLabelText(/your password/i), 'Testing1234!');
    await userEvent.type(screen.getByLabelText(/a recovery code/i), 'aaaa-0000');
    await userEvent.click(screen.getByRole('button', { name: /replace my second factor/i }));

    await waitFor(() => expect(recoveries).toHaveLength(1));

    // The proof is bought for this action and no other: a proof for a password change would not pay for this.
    expect(proofs).toEqual([{ action: 'platform.mfa.recover', password: 'Testing1234!' }]);
    expect(recoveries).toEqual([{ recoveryCode: 'aaaa-0000' }]);

    expect(await screen.findByTestId('recovered-shared-key')).toHaveTextContent('JBSWY3DPEHPK3PXP');
    expect(screen.getByText('aaaa-1111')).toBeInTheDocument();
    expect(screen.getByText('bbbb-2222')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(/shown once/i);

    // Nobody has proved the replacement yet, and the screen says so rather than letting somebody assume it works.
    expect(screen.getByText(/prove the new factor/i)).toBeInTheDocument();
  });

  it('keeps neither the password nor the codes anywhere a browser would remember them', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => new HttpResponse(null, { status: 204 })));
    server.use(http.post('/api/platform/mfa/recover', () => HttpResponse.json(replacement)));

    renderPage();
    await userEvent.type(await screen.findByLabelText(/your password/i), 'Testing1234!');
    await userEvent.type(screen.getByLabelText(/a recovery code/i), 'aaaa-0000');
    await userEvent.click(screen.getByRole('button', { name: /replace my second factor/i }));

    await screen.findByTestId('recovered-shared-key');

    const stored = JSON.stringify(window.localStorage) + JSON.stringify(window.sessionStorage);
    expect(stored).not.toContain('Testing1234!');
    expect(stored).not.toContain('aaaa-0000');
    expect(stored).not.toContain('JBSWY3DPEHPK3PXP');
    expect(stored).not.toContain('aaaa-1111');
  });

  it('binds a refused recovery code to its field and keeps the form to try another', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => new HttpResponse(null, { status: 204 })));
    server.use(http.post('/api/platform/mfa/recover', () => problem(400, 'invalid_recovery_code')));

    renderPage();
    await userEvent.type(await screen.findByLabelText(/your password/i), 'Testing1234!');
    const recoveryCode = screen.getByLabelText(/a recovery code/i);
    await userEvent.type(recoveryCode, 'wrong');
    await userEvent.click(screen.getByRole('button', { name: /replace my second factor/i }));

    const refusal = 'That recovery code was not accepted. Check it or try another unused code.';
    expect(await screen.findAllByText(refusal)).toHaveLength(1);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(recoveryCode).toHaveAttribute('aria-invalid', 'true');
    expect(recoveryCode).toHaveAccessibleDescription(refusal);
    expect(recoveryCode).toHaveFocus();
    expect(screen.getByRole('button', { name: /replace my second factor/i })).toBeInTheDocument();
    expect(screen.queryByTestId('recovered-shared-key')).not.toBeInTheDocument();

    await userEvent.type(recoveryCode, 'next-code');

    expect(recoveryCode).not.toHaveAttribute('aria-invalid', 'true');
    expect(recoveryCode).not.toHaveAccessibleDescription(refusal);
    expect(screen.queryByText(refusal)).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('associates and focuses a recovery-code validation detail without marking the proof password', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/credentials/reauthenticate', () => new HttpResponse(null, { status: 204 })));
    server.use(http.post('/api/platform/mfa/recover', () => problem(400, 'validation_failed', {
      errors: { recoveryCode: [{ code: 'too_long', params: { max: 64 } }] },
    })));

    renderPage();
    const password = await screen.findByLabelText(/your password/i);
    const recoveryCode = screen.getByLabelText(/a recovery code/i);
    await userEvent.type(password, 'Testing1234!');
    await userEvent.type(recoveryCode, 'candidate');
    await userEvent.click(screen.getByRole('button', { name: /replace my second factor/i }));

    await waitFor(() => expect(recoveryCode).toHaveAttribute('aria-invalid', 'true'));
    expect(recoveryCode).toHaveAccessibleDescription('Must be at most 64 characters.');
    expect(recoveryCode).toHaveFocus();
    expect(password).not.toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('alert')).not.toHaveTextContent('Must be at most 64 characters.');
  });

  it('says what to do first when nobody is signed in, rather than offering a form that cannot work', () => {
    server.use(antiforgery(), contextIs(null));

    renderPage();

    expect(screen.getByText(/sign in first/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /replace my second factor/i })).not.toBeInTheDocument();
  });
});
