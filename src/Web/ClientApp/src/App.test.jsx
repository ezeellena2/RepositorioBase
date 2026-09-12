import { render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import App from './App';
import { server } from './test/server';
import { antiforgery, contextIs, problem, signedInContext } from './test/identityServer';

const renderApp = (path = '/') => render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>);

/**
 * What the shell offers follows the session. The permissions decide only what is worth showing: every action
 * behind these links is authorized again by the server, so this is a courtesy rather than a control.
 */
describe('application shell', () => {
  it('waits for identity before showing the anonymous not-found next step', async () => {
    let releaseContext;
    const contextGate = new Promise((resolve) => { releaseContext = resolve; });
    server.use(
      antiforgery(),
      http.get('/api/identity/context', async () => {
        await contextGate;
        return problem(401, 'authentication_required');
      }),
    );
    renderApp('/not-a-route');

    expect(screen.queryByRole('heading', { name: 'That page does not exist' })).not.toBeInTheDocument();
    releaseContext();

    expect(await screen.findByRole('heading', { name: 'That page does not exist', level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/login');
  });

  it('offers an authenticated identity its access page from an unknown route', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    renderApp('/not-a-route');

    expect(await screen.findByRole('heading', { name: 'That page does not exist', level: 1 })).toBeInTheDocument();
    const main = screen.getByRole('main');
    expect(within(main).getByRole('link', { name: 'Your access' })).toHaveAttribute('href', '/identity');
    expect(within(main).queryByRole('link', { name: 'Sign in' })).not.toBeInTheDocument();
  });

  it('offers sign in and registration to a visitor', async () => {
    server.use(antiforgery(), contextIs(null));
    renderApp();

    expect(await screen.findByRole('link', { name: 'Log in' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Register' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Invite a member' })).not.toBeInTheDocument();
  });

  /**
   * The front door, composed the way every site composes one: the two ways in are in the bar, and the page under
   * it is empty. The sidebar is the thing being asserted absent — it names the nineteen screens a session opens,
   * so a visitor standing at the entrance was being handed a menu of things they cannot do yet.
   */
  it('gives a visitor the bar and nothing else on the landing page', async () => {
    server.use(antiforgery(), contextIs(null));
    renderApp();

    const banner = await screen.findByRole('banner');
    expect(within(banner).getByRole('link', { name: 'Log in' })).toBeInTheDocument();
    expect(within(banner).getByRole('link', { name: 'Register' })).toBeInTheDocument();
    // The product name in the bar is styled as a heading and must not be one. Six Playwright page objects find
    // their own page by a document-wide `Locator("h1")`, so a title in the shell breaks all of them at once and
    // the failure reads as if the page under test broke. `component` is what keeps this an anchor: it overrides
    // Typography's variantMapping, which would otherwise turn `variant="h6"` into an `<h6>`.
    expect(within(banner).queryByRole('heading')).not.toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Primary navigation' })).not.toBeInTheDocument();
    // Not `toBeEmptyDOMElement`: `main` still holds the spacer that keeps content clear of the fixed bar, plus
    // the container the routes render into. What the landing page has none of is content, so text is the measure.
    expect(screen.getByRole('main')).toHaveTextContent('');
    expect(within(screen.getByRole('main')).queryByRole('heading')).not.toBeInTheDocument();
  });

  /**
   * The landing page carries no content of its own, and the template's tour of what the project is built with is
   * the content it stopped carrying. Reading it back off the screen is what proves it is gone rather than moved.
   */
  it('prints none of the template welcome on the landing page', async () => {
    server.use(antiforgery(), contextIs(null));
    renderApp();

    await screen.findByRole('link', { name: 'Log in' });
    expect(screen.queryByRole('heading', { name: 'Welcome' })).not.toBeInTheDocument();
    expect(screen.queryByText(/to help you get started/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Material UI' })).not.toBeInTheDocument();
  });

  it('offers the tenant and invite actions once signed in', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    renderApp();

    expect(await screen.findByRole('link', { name: 'Organizations' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Invite a member' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Log in' })).not.toBeInTheDocument();
  });

  it('hides the invite action from a member who cannot invite', async () => {
    server.use(antiforgery(), contextIs(signedInContext({ permissions: ['members.read'] })));
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
    // The session is gone, which is what logging out has to prove. It is read from the absence of the signed-in
    // navigation rather than the presence of the signed-out one, because the sign-in page is composed without the
    // application shell: there is no navigation on it to name either way.
    expect(screen.queryByRole('link', { name: 'Log out' })).not.toBeInTheDocument();
  });

  it('redirects a mid-flow lost session to sign in with its exact return URL and reason', async () => {
    server.use(
      antiforgery(),
      contextIs(signedInContext()),
      http.post('/api/identity/credentials/reauthenticate', () => problem(401, 'invalid_session')),
    );
    renderApp('/identity/password?panel=security');
    await userEvent.type(await screen.findByLabelText('Current password'), 'Testing1234!');
    await userEvent.type(screen.getByLabelText('New password'), 'Replaced5678!');

    await userEvent.click(screen.getByRole('button', { name: 'Change it' }));

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.getByTestId('return-url')).toHaveTextContent('/identity/password?panel=security');
    expect(screen.getByTestId('context-problem')).toHaveTextContent('invalid_session');
    expect(screen.getByRole('alert')).toHaveTextContent('Your session is no longer valid. Sign in again.');
    expect(screen.queryByRole('link', { name: 'Log out' })).not.toBeInTheDocument();
  });

  it('keeps the authenticated shell and shows the exact refusal when sign out fails', async () => {
    server.use(
      antiforgery(),
      contextIs(signedInContext()),
      http.delete('/api/identity/sessions/current', () => problem(409, 'session_concurrency_conflict')),
    );
    renderApp();
    await screen.findByRole('link', { name: 'Organizations' });

    await userEvent.click(screen.getByRole('link', { name: 'Log out' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Something changed while you were working. Try again.');
    expect(screen.getByRole('link', { name: 'Organizations' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Log out' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Sign in' })).not.toBeInTheDocument();
  });
});
