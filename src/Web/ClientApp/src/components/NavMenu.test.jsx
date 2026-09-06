import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { NavMenu } from './NavMenu';
import { IdentityProvider } from '../features/identity/context/IdentityProvider';
import { server } from '../test/server';
import { antiforgery, contextIs, signedInContext } from '../test/identityServer';

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
  });

  it('offers a visitor with no session only the two ways to get one', async () => {
    server.use(antiforgery(), contextIs(null));

    renderMenu();

    expect(await screen.findByRole('link', { name: 'Log in' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Register' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Sign-in providers' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Your devices' })).not.toBeInTheDocument();
  });

  /**
   * Hiding a link is a courtesy, not a control — the server reauthorizes every one of these — but offering an
   * action nobody can perform is still a broken screen.
   */
  it('offers the permissioned entries only to a session that holds the permission', async () => {
    server.use(antiforgery(), contextIs(signedInContext({ permissions: [] })));

    renderMenu();

    await waitFor(() => expect(screen.getByRole('link', { name: 'Your access' })).toBeInTheDocument());
    expect(screen.queryByRole('link', { name: 'Invite a member' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Platform' })).not.toBeInTheDocument();
  });
});
