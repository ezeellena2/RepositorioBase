import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import App from './App';
import { server } from './test/server';
import { antiforgery, contextIs, signedInContext } from './test/identityServer';

const renderAt = (path) => render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>);

/**
 * Every journey the SPEC declares has to be reachable from the router before Playwright is worth writing. A page
 * that exists but is not routed is a page nobody can get to.
 */
describe('identity routes', () => {
  it.each([
    ['/login', 'Sign in'],
    ['/organizations/register', 'Register an organization'],
    ['/invitations/register', 'Set up your account'],
    ['/invitations/accept', 'Accept your invitation'],
    ['/confirm-email', 'Confirm your email'],
  ])('renders the public route %s', async (path, heading) => {
    server.use(antiforgery(), contextIs(null));
    renderAt(path);

    expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument();
  });

  it.each([
    ['/identity', 'Your access'],
    ['/organizations/select', 'Choose an organization'],
    ['/members/invite', 'Invite a member'],
  ])('renders the protected route %s for a signed-in visitor', async (path, heading) => {
    server.use(antiforgery(), contextIs(signedInContext()));
    renderAt(path);

    expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument();
  });

  /**
   * The return URL is rebuilt from where the visitor actually was. Reading it from the incoming query string
   * would let a crafted link choose where someone lands once they hold a session.
   */
  it.each(['/identity', '/organizations/select', '/members/invite'])('sends an unauthenticated visitor from %s to sign in', async (path) => {
    server.use(antiforgery(), contextIs(null));
    renderAt(path);

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId('return-url')).toHaveTextContent(path));
  });

  it('never accepts an off-origin return url', async () => {
    server.use(antiforgery(), contextIs(null));
    renderAt('/login?returnUrl=https://evil.test/steal');

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.getByTestId('return-url')).toHaveTextContent('/');
  });
});
