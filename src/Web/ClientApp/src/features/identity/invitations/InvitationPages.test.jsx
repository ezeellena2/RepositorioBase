import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { IdentityProvider } from '../context/IdentityProvider';
import { antiforgery, contextIs, problem } from '../../../test/identityServer';
import { server } from '../../../test/server';
import { RegisterFromInvitationPage } from './InvitationPages';

const withToken = (token) => window.history.replaceState({}, '', `/invitations/register#token=${encodeURIComponent(token)}`);
const withoutToken = () => window.history.replaceState({}, '', '/invitations/register');
const renderPage = () => render(
  <MemoryRouter>
    <IdentityProvider><RegisterFromInvitationPage /></IdentityProvider>
  </MemoryRouter>,
);

describe('organization invitation registration page', () => {
  it('identifies an incomplete link, disables the action and sends nothing without a fragment token', async () => {
    const submissions = [];
    server.use(
      antiforgery(),
      contextIs(null),
      http.post('/api/invitations/register', async ({ request }) => {
        submissions.push(await request.json());
        return new HttpResponse(null, { status: 202 });
      }),
    );
    withoutToken();

    renderPage();
    const password = await screen.findByLabelText('Choose a password');
    await userEvent.type(password, 'Testing1234!');

    expect(screen.getByText('This invitation link is incomplete. Open it again from the invitation email.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
    expect(password).toBeRequired();
    expect(submissions).toHaveLength(0);
  });

  it("puts the server's password policy descriptions on the password and focuses it", async () => {
    const descriptions = [
      'Passwords must be at least 12 characters.',
      "Passwords must have at least one digit ('0'-'9').",
    ];
    const submissions = [];
    server.use(
      antiforgery(),
      contextIs(null),
      http.post('/api/invitations/register', async ({ request }) => {
        submissions.push(await request.json());
        return problem(400, 'validation_failed', {
          status: 400,
          type: 'about:blank',
          title: 'Bad Request',
          errors: { password: descriptions },
        });
      }),
    );
    withToken('organization-invitation-token');

    renderPage();
    const password = await screen.findByLabelText('Choose a password');
    await userEvent.type(password, 'short');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));

    await waitFor(() => expect(password).toHaveAttribute('aria-invalid', 'true'));
    expect(password).toHaveAccessibleDescription(descriptions.join(' '));
    expect(password).toHaveFocus();
    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Some of what you sent was not accepted. Check the details and try again.');
    expect(alert).not.toHaveTextContent(descriptions[0]);
    expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled();
    expect(submissions).toEqual([{ token: 'organization-invitation-token', password: 'short' }]);
  });

  it('keeps the neutral successful acknowledgement for a well-shaped submission', async () => {
    server.use(
      antiforgery(),
      contextIs(null),
      http.post('/api/invitations/register', () => new HttpResponse(null, { status: 202 })),
    );
    withToken('unknown-token');

    renderPage();
    await userEvent.type(await screen.findByLabelText('Choose a password'), 'Testing1234!');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByRole('status')).toHaveTextContent(/if that invitation is still open/i);
    expect(window.location.hash).toBe('');
  });
});
