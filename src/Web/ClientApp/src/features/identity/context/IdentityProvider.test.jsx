import { useState } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { IdentityProvider, useIdentity } from './IdentityProvider';
import { server } from '../../../test/server';
import { ANTIFORGERY_TOKEN, antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';

function Probe() {
  const identity = useIdentity();
  const [problem, setProblem] = useState(null);
  if (!identity || identity.isLoading) return <p>loading</p>;
  return (
    <>
      <p data-testid="authenticated">{String(identity.isAuthenticated)}</p>
      <p data-testid="tenant">{identity.context?.activeTenant?.name ?? 'none'}</p>
      <p data-testid="problem">{problem ?? ''}</p>
      <button onClick={() => identity.signIn('ana@example.test', 'Testing1234!').catch((failure) => setProblem(failure.problem?.code ?? 'unknown'))}>sign in</button>
      <button onClick={() => identity.signOut()}>sign out</button>
    </>
  );
}

const renderProbe = () => render(<IdentityProvider><Probe /></IdentityProvider>);

/**
 * The provider owns the session and the antiforgery pair. The server rotates that pair whenever the
 * authentication state changes, so the client has to fetch a fresh one at exactly those moments — a stale token
 * is rejected, and a client that never noticed would present it as a mysterious failure to the user.
 */
describe('identity provider', () => {
  it('starts unauthenticated when the context says so, without treating 401 as a fault', async () => {
    server.use(antiforgery(), contextIs(null));
    renderProbe();

    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('false'));
  });

  /**
   * An identity that belongs to nothing yet is signed in all the same. The API omits activeTenant rather than
   * sending a null, and reading that omission as drift would lock every new member out of the shell they need
   * in order to accept the invitation that would give them a tenant.
   */
  it('treats an identity with no active tenant as signed in', async () => {
    const { activeTenant: _omitted, ...withoutTenant } = signedInContext({ availableTenants: [], permissions: [] });
    server.use(antiforgery(), contextIs(withoutTenant));
    renderProbe();

    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));
    expect(screen.getByTestId('tenant')).toHaveTextContent('none');
  });

  /**
   * The pair is fetched on load, before any mutation can need it, and an authenticated load stays authenticated
   * while doing so. Both halves matter: dropping the bootstrap leaves the first mutation to discover it is
   * missing, and an early bootstrap that cost the visitor their session is how this requirement was nearly
   * abandoned — the acceptance harness was injecting its cookie through a header the browser discards once its
   * own jar holds anything for the origin, so the bootstrap's own Set-Cookie evicted the session.
   */
  it('bootstraps the antiforgery pair on load without losing the session', async () => {
    let bootstraps = 0;
    const contextCookies = [];
    server.use(
      http.get('/api/identity/antiforgery', () => {
        bootstraps += 1;
        return HttpResponse.json({ requestToken: ANTIFORGERY_TOKEN });
      }),
      http.get('/api/identity/context', ({ request }) => {
        contextCookies.push(request.headers.get('Cookie'));
        return HttpResponse.json(signedInContext());
      }),
    );
    renderProbe();

    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));
    expect(bootstraps).toBe(1);
    expect(contextCookies).toHaveLength(1);
  });

  it('exposes the signed-in context the API answered with', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    renderProbe();

    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));
    expect(screen.getByTestId('tenant')).toHaveTextContent('Acme');
  });

  it('signs in with the antiforgery request token and reloads the context', async () => {
    const sent = [];
    server.use(
      antiforgery(),
      contextIs(null),
      http.post('/api/identity/sessions', async ({ request }) => {
        sent.push(request.headers.get('X-CSRF-TOKEN'));
        server.use(contextIs(signedInContext()));
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderProbe();
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('false'));

    await userEvent.click(screen.getByRole('button', { name: 'sign in' }));

    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));
    expect(sent).toEqual([ANTIFORGERY_TOKEN]);
  });

  /** Signing out clears what the client knows; leaving a stale context on screen invites acting on it. */
  it('clears the context on sign out', async () => {
    server.use(
      antiforgery(),
      contextIs(signedInContext()),
      http.delete('/api/identity/sessions/current', () => {
        server.use(contextIs(null));
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderProbe();
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'sign out' }));

    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('false'));
    expect(screen.getByTestId('tenant')).toHaveTextContent('none');
  });

  /**
   * A rotated pair is the documented reason a mutation is refused. The client fetches a fresh one and requires
   * the caller to retry rather than replaying the mutation itself — replaying a state change nobody asked for
   * twice is how a single sign-in becomes two sessions.
   */
  it('refreshes the pair on antiforgery_validation_failed and does not replay the mutation', async () => {
    let attempts = 0;
    let bootstraps = 0;
    server.use(
      http.get('/api/identity/antiforgery', () => {
        bootstraps += 1;
        return HttpResponse.json({ requestToken: `token-${bootstraps}` });
      }),
      contextIs(null),
      http.post('/api/identity/sessions', () => {
        attempts += 1;
        return problem(400, 'antiforgery_validation_failed');
      }),
    );
    renderProbe();
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('false'));
    const before = bootstraps;

    await userEvent.click(screen.getByRole('button', { name: 'sign in' }));

    await waitFor(() => expect(bootstraps).toBeGreaterThan(before));
    expect(attempts).toBe(1);
    expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
    expect(screen.getByTestId('problem')).toHaveTextContent('antiforgery_validation_failed');
  });
});
