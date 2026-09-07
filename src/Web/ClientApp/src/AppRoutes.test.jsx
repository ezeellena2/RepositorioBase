import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import App from './App';
import { server } from './test/server';
import { antiforgery, contextIs, signedInContext } from './test/identityServer';
import { externalNavigation } from './features/identity/externalNavigation';

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
    ['/identity/external', 'Sign-in providers'],
    ['/roles', 'Roles'],
    ['/members', 'Members'],
  ])('renders the protected route %s for a signed-in visitor', async (path, heading) => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.get('/api/identity/external', () => HttpResponse.json({ items: [], available: [] })));
    server.use(http.get('/api/tenants/tenant-1/roles', () => HttpResponse.json({ items: [], nextCursor: null })));
    server.use(http.get('/api/tenants/tenant-1/permission-catalog', () => HttpResponse.json([])));
    server.use(http.get('/api/tenants/tenant-1/members', () => HttpResponse.json({ items: [], nextCursor: null })));
    server.use(http.get('/api/tenants/tenant-1/invitations', () => HttpResponse.json({ items: [], nextCursor: null })));
    renderAt(path);

    expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument();
  });

  /**
   * The return URL is rebuilt from where the visitor actually was. Reading it from the incoming query string
   * would let a crafted link choose where someone lands once they hold a session.
   */
  it.each(['/identity', '/organizations/select', '/members/invite', '/identity/external', '/roles', '/members'])('sends an unauthenticated visitor from %s to sign in', async (path) => {
    server.use(antiforgery(), contextIs(null));
    renderAt(path);

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId('return-url')).toHaveTextContent(path));
  });

  /**
   * The return leg is public because a provider sign-in arrives at it before there is a session to protect it
   * with. It completes and moves on, so what is asserted is both: the route renders, and it lands somewhere.
   */
  it('finishes a provider sign-in on the public return route and lands on the account', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.post('/api/identity/external/complete', () => new HttpResponse(null, { status: 204 })));

    renderAt('/external/return?outcome=signed_in');

    expect(screen.getByRole('heading', { name: 'Finishing up' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: 'Your access' })).toBeInTheDocument();
  });

  it('offers the provider from the sign-in screen and hands the browser to what the server named', async () => {
    const left = [];
    vi.spyOn(externalNavigation, 'leaveFor').mockImplementation((uri) => left.push(uri));
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/external/Google/login/start', () =>
      HttpResponse.json({ authorizationRequestUri: '/api/identity/external/Google/login/challenge' })));

    renderAt('/login');
    await userEvent.click(await screen.findByRole('button', { name: 'Continue with Google' }));

    await waitFor(() => expect(left).toEqual(['/api/identity/external/Google/login/challenge']));
  });

  it('never accepts an off-origin return url', async () => {
    server.use(antiforgery(), contextIs(null));
    renderAt('/login?returnUrl=https://evil.test/steal');

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.getByTestId('return-url')).toHaveTextContent('/');
  });
});
