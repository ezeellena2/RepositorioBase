import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { NavMenu } from './NavMenu';
import { IdentityProvider } from '../features/identity/context/IdentityProvider';
import { server } from '../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../test/identityServer';

const renderMenu = () => render(
  <MemoryRouter><IdentityProvider><NavMenu /></IdentityProvider></MemoryRouter>
);

const hrefOf = (name) => screen.getByRole('link', { name }).getAttribute('href');

/**
 * What a person can actually reach. A page that is routed but named nowhere is a page only somebody who already
 * knows the URL can open, which is the same as not shipping it — every self-service screen the SPEC declares has
 * to appear here for a signed-in visitor.
 */
describe('navigation', () => {
  it('names every self-service screen for a signed-in visitor', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));

    renderMenu();
    await screen.findByRole('link', { name: 'Your access' });

    expect(hrefOf('Your access')).toBe('/identity');
    expect(hrefOf('Your profile')).toBe('/identity/profile');
    expect(hrefOf('Your devices')).toBe('/identity/sessions');
    expect(hrefOf('Your password')).toBe('/identity/password');
    expect(hrefOf('Sign-in providers')).toBe('/identity/external');
    expect(hrefOf('Organizations')).toBe('/organizations/select');
    expect(hrefOf('Add an organization')).toBe('/organizations/register');
  });

  it('offers a visitor with no session only the two ways to get one', async () => {
    server.use(antiforgery(), contextIs(null));

    renderMenu();

    expect(await screen.findByRole('link', { name: 'Log in' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Register' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Sign-in providers' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Your devices' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Add an organization' })).not.toBeInTheDocument();
  });

  /**
   * Hiding a link is a courtesy, not a control — the server reauthorizes every one of these — but offering an
   * action nobody can perform is still a broken screen.
   */
  it('offers the permissioned entries only to a session that holds the permission', async () => {
    server.use(antiforgery(), contextIs(signedInContext({ permissions: [] })));

    renderMenu();

    await waitFor(() => expect(screen.getByRole('link', { name: 'Your access' })).toBeInTheDocument());
    expect(screen.queryByRole('link', { name: 'Roles' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Members' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Invite a member' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Platform' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Platform identities' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Retention' })).not.toBeInTheDocument();
  });

  /**
   * Reading the account directory, reading the retention policy and operating the panel are granted separately, so
   * each operator entry answers to its own permission rather than to whichever one the panel happens to hold.
   */
  it('names each operator screen only to the permission that opens it', async () => {
    server.use(antiforgery(), contextIs(signedInContext({ permissions: ['platform.identities.read'] })));

    renderMenu();

    expect(await screen.findByRole('link', { name: 'Platform identities' })).toBeInTheDocument();
    expect(hrefOf('Platform identities')).toBe('/platform/identities');
    expect(screen.queryByRole('link', { name: 'Retention' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Platform' })).not.toBeInTheDocument();
  });

  it('names the retention screen to the permission that opens it', async () => {
    server.use(antiforgery(), contextIs(signedInContext({ permissions: ['platform.retention.read'] })));

    renderMenu();

    expect(await screen.findByRole('link', { name: 'Retention' })).toBeInTheDocument();
    expect(hrefOf('Retention')).toBe('/platform/retention');
    expect(screen.queryByRole('link', { name: 'Platform identities' })).not.toBeInTheDocument();
  });

  it('keeps the current organization and shows the exact refusal while a tenant switch becomes idle again', async () => {
    const selections = [];
    let releaseSelection;
    const responseGate = new Promise((resolve) => { releaseSelection = resolve; });
    server.use(antiforgery(), contextIs(signedInContext()));
    server.use(http.put('/api/identity/context/tenant', async ({ request }) => {
      selections.push(await request.json());
      await responseGate;
      return problem(409, 'session_concurrency_conflict');
    }));

    renderMenu();
    const switcher = await screen.findByRole('button', { name: 'Change organization' });
    await userEvent.click(switcher);
    await userEvent.click(screen.getByRole('menuitem', { name: /Globex/ }));
    await waitFor(() => expect(selections).toEqual([{ tenantId: 'tenant-2' }]));

    const wasBusy = switcher.disabled;
    releaseSelection();

    const alert = await screen.findByRole('alert');
    expect(wasBusy).toBe(true);
    expect(alert).toHaveTextContent('Something changed while you were working. Try again.');
    expect(switcher).toHaveTextContent('Acme');
    expect(switcher).toBeEnabled();
  });
});
