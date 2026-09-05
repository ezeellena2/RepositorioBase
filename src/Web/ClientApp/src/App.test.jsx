import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import App from './App';
import { server } from './test/server';
import { antiforgery, contextIs, signedInContext } from './test/identityServer';

const renderApp = (path = '/') => render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>);

/**
 * What the shell offers follows the session. The permissions decide only what is worth showing: every action
 * behind these links is authorized again by the server, so this is a courtesy rather than a control.
 */
describe('application shell', () => {
  it('offers sign in and registration to a visitor', async () => {
    server.use(antiforgery(), contextIs(null));
    renderApp();

    expect(await screen.findByRole('link', { name: 'Log in' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Register' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Invite a member' })).not.toBeInTheDocument();
  });

  it('offers the tenant and invite actions once signed in', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    renderApp();

    expect(await screen.findByRole('link', { name: 'Organizations' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Invite a member' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Log in' })).not.toBeInTheDocument();
  });

  it('hides the invite action from a member who cannot invite', async () => {
    server.use(antiforgery(), contextIs(signedInContext({ permissions: ['members.view'] })));
    renderApp();

    expect(await screen.findByRole('link', { name: 'Organizations' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Invite a member' })).not.toBeInTheDocument();
  });

  it('returns to sign in after logging out', async () => {
    server.use(
      antiforgery(),
      contextIs(signedInContext()),
      http.delete('/api/identity/sessions/current', () => {
        server.use(contextIs(null));
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderApp();
    await screen.findByRole('link', { name: 'Organizations' });

    await userEvent.click(screen.getByRole('link', { name: 'Log out' }));

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument());
    expect(screen.getByRole('link', { name: 'Log in' })).toBeInTheDocument();
  });
});
