import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { IdentityProvider } from '../../identity/context/IdentityProvider';
import {
  ConfirmPlatformInviteePage,
  PlatformMfaEnrollmentPage,
  RecoverPlatformBootstrapPage,
  RegisterPlatformInviteePage,
} from './PlatformInvitationPages';
import { server } from '../../../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

const withToken = (token) => window.history.replaceState({}, '', `/platform/invitations/register#token=${encodeURIComponent(token)}`);
const withoutToken = () => window.history.replaceState({}, '', '/platform/invitations/register');

const renderPage = (page) => render(<MemoryRouter><IdentityProvider>{page}</IdentityProvider></MemoryRouter>);

/**
 * The Platform onboarding pages (IA-REQ-041).
 *
 * Two things run through all of them. The token arrives in the URL fragment and must not survive there — browsers
 * never send a fragment to a server, which is the whole reason it is used, and leaving it in the address bar
 * would put it into history and into anything that logs a URL. And every answer is neutral: the page says the
 * same thing whether the token was live, dead, or already belonged to an account.
 */
describe('platform invitation pages', () => {
  it('identifies an incomplete link, disables the action and sends nothing without a fragment token', async () => {
    const submissions = [];
    server.use(
      antiforgery(),
      contextIs(null),
      http.post('/api/platform/invitations/register', async ({ request }) => {
        submissions.push(await request.json());
        return new HttpResponse(null, { status: 202 });
      }),
    );
    withoutToken();

    renderPage(<RegisterPlatformInviteePage />);
    const password = await screen.findByLabelText('Choose a password');
    await userEvent.type(password, 'Testing1234!');

    expect(screen.getByText('This invitation link is incomplete. Open it again from the invitation email.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
    expect(password).toBeRequired();
    expect(submissions).toHaveLength(0);
  });

  it('reads the token out of the fragment and erases it', async () => {
    const submissions = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/platform/invitations/register', async ({ request }) => {
      submissions.push(await request.json());
      return new HttpResponse(null, { status: 202 });
    }));
    withToken('platform-token-1');

    renderPage(<RegisterPlatformInviteePage />);
    expect(window.location.hash).toBe('');

    await userEvent.type(await screen.findByLabelText('Choose a password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));

    await waitFor(() => expect(submissions).toHaveLength(1));
    expect(submissions[0]).toEqual({ token: 'platform-token-1', password: 'Testing1234!' });
  });

  /**
   * The continuation renders on a PUBLIC route, so the only thing standing between an anonymous visitor holding
   * a link and the enrollment gates is this check. The negative on the /platform/mfa page does not cover it:
   * that page is behind ProtectedRoute and this one is not.
   */
  it('offers no second factor to a visitor with no session, and asks for nothing', async () => {
    const enrollments = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/platform/mfa/enroll', async ({ request }) => {
      enrollments.push(await request.json());
      return HttpResponse.json({ sharedKey: 'K', provisioningUri: 'otpauth://x', recoveryCodes: ['c'] });
    }));
    withToken('platform-token-anonymous');

    renderPage(<RegisterPlatformInviteePage />);

    expect(await screen.findByLabelText('Choose a password')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Set up your second factor' })).not.toBeInTheDocument();
    expect(enrollments).toHaveLength(0);
  });

  /**
   * Signed in, the same link is the way back into the ceremony — with the token still only in this component's
   * memory. The password form stays because an invitee whose account already existed answers it from here too.
   */
  it('offers a signed-in invitee the second factor from the invitation link, without writing the token anywhere', async () => {
    const enrollments = [];
    server.use(antiforgery(), contextIs(signedInContext({ activeTenant: null, availableTenants: [], permissions: [] })));
    server.use(http.post('/api/platform/mfa/enroll', async ({ request }) => {
      enrollments.push(await request.json());
      return HttpResponse.json({ sharedKey: 'KEY', provisioningUri: 'otpauth://x', recoveryCodes: ['code-1'] });
    }));
    withToken('platform-token-continued');

    renderPage(<RegisterPlatformInviteePage />);
    expect(await screen.findByLabelText('Choose a password')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Set up your second factor' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Begin enrollment' }));

    await waitFor(() => expect(enrollments).toHaveLength(1));
    expect(enrollments[0]).toEqual({ token: 'platform-token-continued' });
    expect(await screen.findByTestId('platform-shared-key')).toHaveTextContent('KEY');
    expect(window.location.hash).toBe('');
    expect(window.location.search).toBe('');
  });

  it('says the same neutral thing whether the token was live or not', async () => {
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/platform/invitations/register', () => new HttpResponse(null, { status: 202 })));
    withToken('unknown-token');

    renderPage(<RegisterPlatformInviteePage />);
    await userEvent.type(await screen.findByLabelText('Choose a password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByRole('status')).toHaveTextContent(/if that invitation is still open/i);
  });

  /**
   * The credential is submitted; whether it is used is the server's decision, and for an address that already has
   * an account it is ignored. What the page must not do is claim otherwise — so the answer is the same one.
   */
  it('cannot take over an existing account and says nothing about whether it exists', async () => {
    const submissions = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/platform/invitations/register', async ({ request }) => {
      submissions.push(await request.json());
      return new HttpResponse(null, { status: 202 });
    }));
    withToken('token-for-existing-identity');

    renderPage(<RegisterPlatformInviteePage />);
    await userEvent.type(await screen.findByLabelText('Choose a password'), 'AnotherPassword1!');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByRole('status')).toHaveTextContent(/if that invitation is still open/i);
    expect(submissions[0].token).toBe('token-for-existing-identity');
    expect(Object.keys(submissions[0])).toEqual(['token', 'password']);
  });

  it('puts the localized password-policy detail on the password and focuses it', async () => {
    const passwordPolicyDetails = [{ code: 'password_policy', params: {} }];
    const submissions = [];
    server.use(
      antiforgery(),
      contextIs(null),
      http.post('/api/platform/invitations/register', async ({ request }) => {
        submissions.push(await request.json());
        return problem(400, 'validation_failed', {
          status: 400,
          type: 'about:blank',
          title: 'Bad Request',
          errors: { password: passwordPolicyDetails },
        });
      }),
    );
    withToken('token-1');

    renderPage(<RegisterPlatformInviteePage />);
    const password = await screen.findByLabelText('Choose a password');
    await userEvent.type(password, 'short');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));

    await waitFor(() => expect(password).toHaveAttribute('aria-invalid', 'true'));
    expect(password).toHaveAccessibleDescription('This password does not meet the requirements.');
    expect(password).toHaveFocus();
    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Some of what you sent was not accepted. Check the details and try again.');
    expect(alert).not.toHaveTextContent('This password does not meet the requirements.');
    expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled();
    expect(submissions).toEqual([{ token: 'token-1', password: 'short' }]);
  });

  it('associates and focuses an invitation password detail without hiding fragment-token details', async () => {
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/platform/invitations/register', () => problem(400, 'validation_failed', {
      errors: {
        password: [{ code: 'password_policy', params: {} }],
        token: [{ code: 'required', params: {} }],
      },
    })));
    withToken('platform-token-validation');

    renderPage(<RegisterPlatformInviteePage />);
    const password = await screen.findByLabelText('Choose a password');
    await userEvent.type(password, 'candidate');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));

    await waitFor(() => expect(password).toHaveAttribute('aria-invalid', 'true'));
    expect(password).toHaveAccessibleDescription('This password does not meet the requirements.');
    expect(password).toHaveFocus();
    const alert = screen.getByRole('alert');
    expect(alert).not.toHaveTextContent('This password does not meet the requirements.');
    expect(alert).toHaveTextContent('Token: This value is required.');
  });

  /**
   * One click, from a link in the recipient's own mailbox. The token comes out of the fragment and never appears
   * in the address bar, and nothing is transcribed by hand.
   */
  it('confirms with the token from the link and then hands off to a normal sign-in', async () => {
    const confirmations = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/platform/invitations/confirm', async ({ request }) => {
      confirmations.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));
    withToken('confirmation-token-1');

    renderPage(<ConfirmPlatformInviteePage />);
    expect(window.location.hash).toBe('');
    await userEvent.click(await screen.findByRole('button', { name: 'Confirm my address' }));

    await waitFor(() => expect(confirmations).toHaveLength(1));
    expect(confirmations[0]).toEqual({ confirmationToken: 'confirmation-token-1' });
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    // The screen names the two steps that are left, and offers the first of them. Ending on "sign in to continue"
    // alone was what left the invitee with no way back into the ceremony (R2).
    expect(await screen.findByRole('status')).toHaveTextContent(/open your invitation email again to set up your second factor/i);
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/login');
  });

  it('shows an invalid confirmation exactly, never shows success and re-enables the action', async () => {
    server.use(
      antiforgery(),
      contextIs(null),
      http.post('/api/platform/invitations/confirm', () => problem(400, 'invalid_confirmation')),
    );
    withToken('expired-platform-confirmation');

    renderPage(<ConfirmPlatformInviteePage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Confirm my address' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('That confirmation link is not usable.');
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Confirm my address' })).toBeEnabled();
  });

  it('does not offer the MFA ceremony to a visitor with no session', async () => {
    server.use(antiforgery(), contextIs(null));

    renderPage(<PlatformMfaEnrollmentPage />);

    expect(await screen.findByText(/sign in with the invited address, then open your invitation email again/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Begin enrollment' })).not.toBeInTheDocument();
  });

  /**
   * The whole ceremony in order. The key and the codes are shown once, and the membership only becomes active at
   * the last step — which is why acknowledging is a separate deliberate action rather than part of verifying.
   */
  it('walks enrol, verify and acknowledge in order and shows the codes once', async () => {
    const calls = [];
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/platform/mfa/enroll', () => {
      calls.push('enroll');
      return HttpResponse.json({
        sharedKey: 'JBSWY3DPEHPK3PXP',
        provisioningUri: 'otpauth://totp/Platform:owner@example.test?secret=JBSWY3DPEHPK3PXP',
        recoveryCodes: ['aaaaa-bbbbb-ccccc-ddddd', 'eeeee-fffff-ggggg-hhhhh'],
      });
    }));
    server.use(http.post('/api/platform/mfa/verify', () => { calls.push('verify'); return new HttpResponse(null, { status: 204 }); }));
    server.use(http.post('/api/platform/mfa/recovery-acknowledge', () => { calls.push('acknowledge'); return new HttpResponse(null, { status: 204 }); }));
    withToken('platform-token-1');

    renderPage(<PlatformMfaEnrollmentPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Begin enrollment' }));

    expect(await screen.findByTestId('platform-shared-key')).toHaveTextContent('JBSWY3DPEHPK3PXP');
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
    expect(screen.queryByRole('button', { name: /saved my recovery codes/i })).not.toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(/code from your authenticator/i), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Verify' }));

    await userEvent.click(await screen.findByRole('button', { name: /saved my recovery codes/i }));

    expect(await screen.findByRole('status')).toHaveTextContent(/second factor is active/i);
    expect(calls).toEqual(['enroll', 'verify', 'acknowledge']);
  });

  it('keeps a successful acknowledgement successful when selecting the new Platform context is refused', async () => {
    const calls = [];
    const selections = [];
    let contextReads = 0;
    const initialContext = signedInContext({ activeTenant: null, availableTenants: [], permissions: [] });
    const refreshedContext = signedInContext({
      activeTenant: null,
      availableTenants: [{ id: 'platform-1', type: 'Platform', name: 'Platform' }],
      permissions: [],
    });
    server.use(
      antiforgery(),
      http.get('/api/identity/context', () => {
        contextReads += 1;
        return HttpResponse.json(contextReads === 1 ? initialContext : refreshedContext);
      }),
      http.post('/api/platform/mfa/enroll', () => HttpResponse.json({
        sharedKey: 'JBSWY3DPEHPK3PXP',
        provisioningUri: 'otpauth://x',
        recoveryCodes: ['aaaaa-bbbbb-ccccc-ddddd'],
      })),
      http.post('/api/platform/mfa/verify', () => new HttpResponse(null, { status: 204 })),
      http.post('/api/platform/mfa/recovery-acknowledge', () => {
        calls.push('acknowledge');
        return new HttpResponse(null, { status: 204 });
      }),
      http.put('/api/identity/context/tenant', async ({ request }) => {
        selections.push(await request.json());
        return problem(409, 'session_concurrency_conflict');
      }),
    );
    withToken('platform-token-1');

    renderPage(<PlatformMfaEnrollmentPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Begin enrollment' }));
    await userEvent.type(screen.getByLabelText(/code from your authenticator/i), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Verify' }));
    await userEvent.click(await screen.findByRole('button', { name: /saved my recovery codes/i }));

    const success = await screen.findByRole('status');
    const alert = await screen.findByRole('alert');
    expect(success).toHaveTextContent('Your second factor is active.');
    expect(alert).toHaveTextContent('Something changed while you were working. Try again.');
    expect(calls).toEqual(['acknowledge']);
    expect(selections).toEqual([{ tenantId: 'platform-1' }]);
    expect(contextReads).toBe(2);
    expect(screen.queryByRole('button', { name: /saved my recovery codes/i })).not.toBeInTheDocument();
  });

  it('binds a refused authenticator code to its field without advancing to acknowledgement', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/platform/mfa/enroll', () => HttpResponse.json({
      sharedKey: 'JBSWY3DPEHPK3PXP', provisioningUri: 'otpauth://x', recoveryCodes: ['aaaaa'],
    })));
    server.use(http.post('/api/platform/mfa/verify', () => problem(400, 'invalid_mfa_code')));
    withToken('platform-token-1');

    renderPage(<PlatformMfaEnrollmentPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Begin enrollment' }));
    const code = screen.getByLabelText(/code from your authenticator/i);
    await userEvent.type(code, '000000');
    await userEvent.click(screen.getByRole('button', { name: 'Verify' }));

    const refusal = 'That authenticator code was not accepted. Check the code and try again.';
    expect(await screen.findAllByText(refusal)).toHaveLength(1);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(code).toHaveAttribute('aria-invalid', 'true');
    expect(code).toHaveAccessibleDescription(refusal);
    expect(code).toHaveFocus();
    expect(screen.queryByRole('button', { name: /saved my recovery codes/i })).not.toBeInTheDocument();

    await userEvent.type(code, '1');

    expect(code).not.toHaveAttribute('aria-invalid', 'true');
    expect(code).not.toHaveAccessibleDescription(refusal);
    expect(screen.queryByText(refusal)).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('associates and focuses an enrollment code detail without duplicating the detail', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/platform/mfa/enroll', () => HttpResponse.json({
      sharedKey: 'JBSWY3DPEHPK3PXP', provisioningUri: 'otpauth://x', recoveryCodes: ['aaaaa'],
    })));
    server.use(http.post('/api/platform/mfa/verify', () => problem(400, 'validation_failed', {
      errors: { code: [{ code: 'too_long', params: { max: 16 } }] },
    })));
    withToken('platform-token-validation');

    renderPage(<PlatformMfaEnrollmentPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Begin enrollment' }));
    const code = screen.getByLabelText(/code from your authenticator/i);
    await userEvent.type(code, '000000');
    await userEvent.click(screen.getByRole('button', { name: 'Verify' }));

    await waitFor(() => expect(code).toHaveAttribute('aria-invalid', 'true'));
    expect(code).toHaveAccessibleDescription('Must be at most 16 characters.');
    expect(code).toHaveFocus();
    expect(screen.getByRole('alert')).not.toHaveTextContent('Must be at most 16 characters.');
  });

  it('recovers the bootstrap invitation without sending an address', async () => {
    const bodies = [];
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/platform/bootstrap/recover', async ({ request }) => {
      bodies.push(await request.text());
      return new HttpResponse(null, { status: 202 });
    }));

    renderPage(<RecoverPlatformBootstrapPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Resend' }));

    expect(await screen.findByRole('status')).toHaveTextContent(/if an owner invitation is waiting/i);
    expect(bodies).toEqual(['']);
    expect(screen.queryByLabelText(/email|address|recipient/i)).not.toBeInTheDocument();
  });

  it('shows an exhausted recovery limit with the wait the server asked for', async () => {
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/platform/bootstrap/recover', () =>
      problem(429, 'rate_limit_exceeded', {}, { 'Retry-After': '90' })));

    renderPage(<RecoverPlatformBootstrapPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Resend' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/too many attempts/i);
    expect(alert).toHaveTextContent(/90 seconds/);
  });

  it('shows a refused antiforgery on recovery rather than a neutral success', async () => {
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/platform/bootstrap/recover', () => problem(400, 'antiforgery_validation_failed')));

    renderPage(<RecoverPlatformBootstrapPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Resend' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/session moved on/i);
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});
