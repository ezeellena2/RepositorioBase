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

  it('shows a refused registration as the problem the server sent', async () => {
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/platform/invitations/register', () => problem(400, 'invalid_invitation')));
    withToken('token-1');

    renderPage(<RegisterPlatformInviteePage />);
    await userEvent.type(await screen.findByLabelText('Choose a password'), 'short');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/invitation is not usable/i);
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

  it('shows a refused verification without advancing to acknowledgement', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/platform/mfa/enroll', () => HttpResponse.json({
      sharedKey: 'JBSWY3DPEHPK3PXP', provisioningUri: 'otpauth://x', recoveryCodes: ['aaaaa'],
    })));
    server.use(http.post('/api/platform/mfa/verify', () => problem(400, 'invalid_invitation')));
    withToken('platform-token-1');

    renderPage(<PlatformMfaEnrollmentPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Begin enrollment' }));
    await userEvent.type(screen.getByLabelText(/code from your authenticator/i), '000000');
    await userEvent.click(screen.getByRole('button', { name: 'Verify' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/invitation is not usable/i);
    expect(screen.queryByRole('button', { name: /saved my recovery codes/i })).not.toBeInTheDocument();
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
