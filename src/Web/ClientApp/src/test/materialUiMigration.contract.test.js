import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createElement, createRef } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http } from 'msw';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import NativeSelect from '@mui/material/NativeSelect';
import { ThemeProvider as MaterialThemeProvider } from '@mui/material/styles';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import App from '../App';
import { Counter } from '../components/Counter';
import { Layout } from '../components/Layout';
import { NativeDialog } from '../components/NativeDialog';
import { IdentityProvider } from '../features/identity/context/IdentityProvider';
import { appTheme } from '../theme';
import { antiforgery, contextIs, problem, signedInContext } from './identityServer';
import { server } from './server';

function renderWithMui(children) {
  return render(createElement(MaterialThemeProvider, { theme: appTheme }, children));
}

describe('Material UI visual foundation', () => {
  it('centralizes the selected brand palette roles and approved typography', () => {
    expect(appTheme.colorSchemes.light.palette.primary.main).toBe('#0E5C66');
    expect(appTheme.colorSchemes.light.palette.error.main).toBe('#B3412A');
    expect(appTheme.colorSchemes.light.palette.success.main).toBe('#1F6B40');
    expect(appTheme.typography.fontFamily).toContain('Source Sans 3');
    expect(appTheme.typography.h1.fontFamily).toContain('Manrope');
  });

  it('keeps stock Material UI behavior available alongside the selected branding', () => {
    expect(appTheme.palette.info.main).toBeDefined();
    expect(appTheme.palette.warning.main).toBeDefined();
    expect(appTheme.shadows.some((shadow) => shadow !== 'none')).toBe(true);
    expect(appTheme.palette.action.hoverOpacity).toBeGreaterThan(0);
  });

  it('renders existing routes through the Material UI provider, baseline, and color scheme', async () => {
    server.use(antiforgery(), contextIs(null));
    render(createElement(MemoryRouter, null, createElement(App)));

    expect(await screen.findByRole('link', { name: 'Log in' })).toBeInTheDocument();
    expect(document.head.querySelector('style[data-emotion]')).not.toBeNull();
    expect(Object.keys(appTheme.colorSchemes)).toEqual(['light']);
  });

  it('keeps navigation and route content in a semantic MUI shell without legacy theme state', async () => {
    server.use(antiforgery(), contextIs(null));
    renderWithMui(createElement(
      MemoryRouter,
      null,
      createElement(
        IdentityProvider,
        null,
        createElement(Layout, null, createElement('h1', null, 'Shell content')),
      ),
    ));

    expect(await screen.findByRole('link', { name: 'Log in' })).toBeInTheDocument();
    expect(screen.getByRole('banner')).toBeInTheDocument();
    expect(screen.getByRole('navigation')).toBeInTheDocument();
    expect(screen.getByRole('main')).toHaveTextContent('Shell content');
  });

  /**
   * The appearance is not a setting any more. Asserting the absence of the control is only half of it — a theme
   * that still declares a dark scheme goes dark on a dark operating system with no control involved — so the
   * scheme itself is what this pins.
   */
  it('offers no appearance control and no scheme for one to choose', async () => {
    server.use(antiforgery(), contextIs(null));
    renderWithMui(createElement(
      MemoryRouter,
      null,
      createElement(IdentityProvider, null, createElement(Layout, null, null)),
    ));

    await screen.findByRole('link', { name: 'Log in' });
    for (const name of ['auto', 'light', 'dark']) {
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument();
    }
    expect(appTheme.colorSchemes.dark).toBeUndefined();
  });

  it('uses stock NativeSelect with an associated native select element', () => {
    renderWithMui(createElement(
      FormControl,
      null,
      createElement(InputLabel, { htmlFor: 'future-native-select' }, 'Future native select'),
      createElement(
        NativeSelect,
        { inputProps: { id: 'future-native-select', name: 'future-native-select' } },
        createElement('option', { value: 'one' }, 'One'),
      ),
    ));

    const select = screen.getByLabelText('Future native select');
    expect(select.tagName).toBe('SELECT');
    expect(select).toHaveAttribute('name', 'future-native-select');
  });

  it('forwards native dialog methods through a real dialog element', () => {
    const showModal = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'showModal');
    const close = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'close');
    Object.defineProperty(HTMLDialogElement.prototype, 'showModal', {
      configurable: true,
      value() { this.open = true; },
    });
    Object.defineProperty(HTMLDialogElement.prototype, 'close', {
      configurable: true,
      value() { this.open = false; },
    });

    try {
      const ref = createRef();
      renderWithMui(createElement(NativeDialog, { ref }, 'Native dialog content'));
      const dialog = screen.getByText('Native dialog content').closest('dialog');

      expect(dialog).toBe(ref.current);
      expect(dialog.tagName).toBe('DIALOG');
      ref.current.showModal();
      expect(dialog).toHaveAttribute('open');
      ref.current.close();
      expect(dialog).not.toHaveAttribute('open');
    } finally {
      if (showModal) Object.defineProperty(HTMLDialogElement.prototype, 'showModal', showModal);
      else delete HTMLDialogElement.prototype.showModal;
      if (close) Object.defineProperty(HTMLDialogElement.prototype, 'close', close);
      else delete HTMLDialogElement.prototype.close;
    }
  });

  it('gives the counter an explicit non-submit action', async () => {
    const user = userEvent.setup();
    renderWithMui(createElement(Counter));

    const increment = screen.getByRole('button', { name: 'Increment' });
    expect(increment).toHaveAttribute('type', 'button');
    await user.click(increment);
    expect(screen.getByText('1', { selector: 'strong' })).toBeInTheDocument();
  });
});

const renderRoute = (path) =>
  render(createElement(MemoryRouter, { initialEntries: [path] }, createElement(App)));

/**
 * Two stylesheets that both claim the same elements do not compose, they take turns. Pico is classless, so it
 * styles every bare `input`, `button` and `select` — which is exactly what Material UI renders underneath its own
 * classes, and the visible control was Pico's. One of them has to own presentation, and the theme is the one the
 * migration keeps.
 */
describe('Material UI as the only presentation system', () => {
  const app = resolve(import.meta.dirname, '..');

  it('leaves no competing stylesheet or color-mode bootstrap behind', () => {
    expect(existsSync(resolve(app, 'styles.scss'))).toBe(false);
    expect(existsSync(resolve(app, 'components/ThemeContext.jsx'))).toBe(false);
    expect(existsSync(resolve(app, 'components/ThemeToggle.jsx'))).toBe(false);
    expect(readFileSync(resolve(app, 'main.jsx'), 'utf8')).not.toContain('styles.scss');
    expect(readFileSync(resolve(app, 'App.jsx'), 'utf8')).not.toContain('ThemeContext');
    expect(readFileSync(resolve(app, '..', 'index.html'), 'utf8')).not.toContain('picoColorScheme');
  });
});

/**
 * Signing in is the one screen a stranger reaches before anything else, so it is composed as its own page rather
 * than as route content inside the application shell. The product navigation names screens a visitor without a
 * session cannot open — putting it above the sign-in card asks
 * somebody to read a menu when the only thing they came to do is get in.
 */
describe('Material UI authentication entry', () => {
  it('gives the public sign-in route its own composition instead of the application shell', async () => {
    server.use(antiforgery(), contextIs(null));
    renderRoute('/login');

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.queryByRole('banner')).not.toBeInTheDocument();
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
    for (const name of ['Clean Architecture', 'Home', 'Counter']) {
      expect(screen.queryByRole('link', { name })).not.toBeInTheDocument();
    }
  });

  /**
   * The shell is removed from the public entrance, not from the product. A route behind a session still renders
   * inside the AppBar the rest of the application navigates by.
   */
  it('keeps the application shell on a route behind a session', async () => {
    server.use(antiforgery(), contextIs(signedInContext()));
    renderRoute('/identity');

    expect(await screen.findByRole('heading', { name: 'Your access' })).toBeInTheDocument();
    expect(screen.getByRole('banner')).toBeInTheDocument();
    expect(screen.getByRole('navigation')).toBeInTheDocument();
  });

  it('builds the sign-in card from direct Material UI components without losing one contract', async () => {
    server.use(antiforgery(), contextIs(null));
    renderRoute('/login');

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toHaveAttribute('id', 'login-heading');

    const email = screen.getByLabelText('Email');
    expect(email).toHaveAttribute('id', 'login-email');
    expect(email).toHaveAttribute('type', 'email');
    expect(email).toHaveAttribute('autocomplete', 'username');
    expect(email).toBeRequired();
    expect(email.closest('.MuiTextField-root')).not.toBeNull();

    const password = screen.getByLabelText('Password');
    expect(password).toHaveAttribute('id', 'login-password');
    expect(password).toHaveAttribute('type', 'password');
    expect(password).toHaveAttribute('autocomplete', 'current-password');
    expect(password).toBeRequired();
    expect(password.closest('.MuiTextField-root')).not.toBeNull();

    const submit = screen.getByRole('button', { name: 'Sign in' });
    expect(submit).toHaveAttribute('type', 'submit');
    expect(submit).toHaveClass('MuiButton-root');

    const provider = screen.getByRole('button', { name: 'Continue with Google' });
    expect(provider).toHaveAttribute('type', 'button');
    expect(provider).toHaveClass('MuiButton-root');

    expect(screen.getByRole('link', { name: 'Forgot your password?' })).toHaveAttribute('href', '/credentials/forgot');
    expect(screen.getByRole('link', { name: 'Reactivate your account' }))
      .toHaveAttribute('href', '/account/reactivation-request');
    expect(screen.getByTestId('return-url')).toBeInTheDocument();
    expect(screen.getByTestId('context-problem')).toBeInTheDocument();
    // Revealing a typed password is a capability this product does not offer, and a redesign is not where one
    // arrives by accident.
    expect(screen.queryByRole('button', { name: /show|hide|reveal/i })).not.toBeInTheDocument();
  });

  /**
   * What the API refused has to read as a refusal. The wording still comes from the stable code, so the alert is
   * the presentation of an existing message rather than a new one.
   */
  it('reports a refused sign-in through a Material UI alert carrying the existing message', async () => {
    const user = userEvent.setup();
    server.use(antiforgery(), contextIs(null));
    server.use(http.post('/api/identity/sessions', async () => {
      await delay(20);
      return problem(429, 'rate_limit_exceeded');
    }));
    renderRoute('/login');

    await user.type(await screen.findByLabelText('Email'), 'ana@example.test');
    await user.type(screen.getByLabelText('Password'), 'Password1!');
    const submit = screen.getByRole('button', { name: 'Sign in' });
    expect(submit).toBeEnabled();
    await user.click(submit);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Too many attempts. Wait a moment and try again.');
    expect(alert).toHaveClass('MuiAlert-root');
    await waitFor(() => expect(submit).toBeEnabled());
  });
});
