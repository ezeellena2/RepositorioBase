import { act, render, renderHook, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import {
  i18n,
  isPseudoLanguageOverrideActive,
  PSEUDO_LANGUAGE,
  resolveLanguage,
  roleName,
  setLanguage,
  useFormat,
} from './index';
import languages from './languages.json';
import { appTheme, missingMuiLocaleMappings, muiLocaleByLanguage, themeFor } from '../theme';
import { esES } from '@mui/material/locale';
import { NavMenu } from '../components/NavMenu';
import { IdentityProvider } from '../features/identity/context/IdentityProvider';
import { server } from '../test/server';
import { antiforgery, contextIs, problem, signedInContext } from '../test/identityServer';

describe('Spanish language selection', () => {
  it('negotiates preference, cookie, browser and default without writing an inferred cookie', () => {
    const browser = vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['es-AR']);
    expect(resolveLanguage()).toBe('es');
    expect(document.cookie).not.toContain('.AspNetCore.Culture');
    document.cookie = '.AspNetCore.Culture=c=en|uic=en; path=/';
    expect(resolveLanguage()).toBe('en');
    expect(resolveLanguage('es-MX')).toBe('es');
    document.cookie = '.AspNetCore.Culture=c=fr|uic=fr; path=/';
    expect(resolveLanguage('xx')).toBe('es');
    browser.mockReturnValue(['fr-FR']);
    expect(resolveLanguage()).toBe('en');
    browser.mockRestore();
  });

  it('changes the live catalog, ASP.NET cookie and document language', async () => {
    await act(() => setLanguage('es-AR'));
    expect(i18n.resolvedLanguage).toBe('es');
    expect(i18n.t('identity:login.title')).toBe('Iniciar sesión');
    expect(document.documentElement.lang).toBe('es');
    expect(document.cookie).toContain('.AspNetCore.Culture=c=es|uic=es');
  });

  it('changes language locally for a visitor without persisting an account preference', async () => {
    let preferenceWrites = 0;
    await act(() => setLanguage('en'));
    server.use(
      antiforgery(),
      contextIs(null),
      http.put('/api/identity/context/language', () => {
        preferenceWrites += 1;
        return HttpResponse.json(signedInContext({ preferredLanguage: 'es' }));
      }),
    );
    render(<MemoryRouter><IdentityProvider><NavMenu /></IdentityProvider></MemoryRouter>);
    await screen.findByRole('link', { name: 'Log in' });
    const selector = screen.getByRole('combobox', { name: 'Language' });
    expect(selector.tagName).toBe('SELECT');
    expect(selector).toHaveAttribute('name', 'language');
    expect(selector.labels[0]).toHaveAttribute('for', selector.id);
    expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual(['English', 'Español']);
    await userEvent.selectOptions(selector, 'es');
    expect(screen.getByRole('combobox', { name: 'Idioma' })).toHaveValue('es');
    expect(document.documentElement.lang).toBe('es');
    expect(preferenceWrites).toBe(0);
  });

  it('keeps the signed-in language unchanged and shows a refusal when persistence fails', async () => {
    await act(() => setLanguage('en'));
    server.use(
      antiforgery(),
      contextIs(signedInContext({ preferredLanguage: 'en' })),
      http.put('/api/identity/context/language', () => problem(400, 'validation_failed')),
    );
    render(<MemoryRouter><IdentityProvider><NavMenu /></IdentityProvider></MemoryRouter>);
    await screen.findByRole('link', { name: 'Your access' });

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Language' }), 'es');

    const alert = await screen.findByRole('alert');
    await waitFor(() => expect(alert).toHaveFocus());
    expect(screen.getByRole('combobox', { name: 'Language' })).toHaveValue('en');
    expect(i18n.resolvedLanguage).toBe('en');
    expect(document.documentElement.lang).toBe('en');
    expect(document.cookie).toContain('c=en|uic=en');
  });

  it('keeps a failed language detail in the Spanish general alert and returns focus to that alert', async () => {
    await act(() => setLanguage('es'));
    server.use(
      antiforgery(),
      contextIs(signedInContext({ preferredLanguage: 'es' })),
      http.put('/api/identity/context/language', () => problem(400, 'validation_failed', {
        errors: { language: [{ code: 'unsupported_value', params: {} }] },
      })),
    );
    render(<MemoryRouter><IdentityProvider><NavMenu /></IdentityProvider></MemoryRouter>);
    await screen.findByRole('link', { name: 'Su acceso' });

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Idioma' }), 'en');

    const alert = await screen.findByRole('alert');
    await waitFor(() => expect(alert).toHaveFocus());
    expect(alert).toHaveTextContent('Idioma: Este valor no es compatible.');
    expect(screen.getByRole('combobox', { name: 'Idioma' })).toHaveValue('es');
    expect(i18n.resolvedLanguage).toBe('es');
    expect(document.documentElement.lang).toBe('es');
    expect(document.body).not.toHaveTextContent(/unsupported_value/);
  });

  it('ignores unsupported choices without replacing the existing culture cookie', async () => {
    await act(() => setLanguage('es'));
    await act(() => setLanguage('fr'));
    expect(i18n.resolvedLanguage).toBe('es');
    expect(document.cookie).toContain('c=es|uic=es');
  });

  it('requires a reload before a pseudo query added after startup can activate', async () => {
    await act(() => setLanguage('en'));
    window.history.replaceState({}, '', '/?lng=en-XA');

    expect(isPseudoLanguageOverrideActive()).toBe(false);
    await act(() => setLanguage('es'));

    expect(i18n.resolvedLanguage).toBe('es');
    expect(document.documentElement.lang).toBe('es');
    expect(document.cookie).toContain('c=es|uic=es');
    expect(document.cookie).not.toContain(PSEUDO_LANGUAGE);
  });

  it('composes MUI locales while retaining the original English visual theme', () => {
    expect(themeFor('es').components.MuiTablePagination.defaultProps.labelRowsPerPage)
      .toBe(esES.components.MuiTablePagination.defaultProps.labelRowsPerPage);
    // MUI's enUS is empty: English comes from the components' built-in defaults.
    expect(themeFor('en').components.MuiTablePagination).toBeUndefined();
    for (const language of ['en', 'es']) {
      const theme = themeFor(language);
      expect(theme.palette.primary).toEqual(appTheme.palette.primary);
      expect(theme.palette.error).toEqual(appTheme.palette.error);
      expect(theme.palette.success).toEqual(appTheme.palette.success);
      for (const [key, value] of Object.entries(appTheme.typography)) {
        if (typeof value !== 'function') expect(theme.typography[key]).toEqual(value);
      }
      expect(theme.typography.pxToRem(24)).toBe(appTheme.typography.pxToRem(24));
      expect(theme.components.MuiButton).toEqual(appTheme.components.MuiButton);
    }
    expect(appTheme.components.MuiTablePagination).toBeUndefined();
  });

  it('maps every and only supported language to explicit MUI locale data', () => {
    expect(Object.keys(muiLocaleByLanguage).sort()).toEqual([...languages.supported].sort());
    expect(missingMuiLocaleMappings(languages.supported)).toEqual([]);
    expect(missingMuiLocaleMappings(['en', 'es'], { en: muiLocaleByLanguage.en })).toEqual(['es']);
  });

  it('formats dates and numbers in the live language and keeps the browser time zone', async () => {
    const instant = '2026-09-08T23:15:00Z';
    const NativeDateTimeFormat = Intl.DateTimeFormat;
    const dates = vi.spyOn(Intl, 'DateTimeFormat');
    const numbers = vi.spyOn(Intl, 'NumberFormat');
    const { result } = renderHook(() => useFormat());
    expect(result.current.formatDate(instant)).toBe(new NativeDateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(instant)));
    await act(() => setLanguage('es'));
    expect(result.current.formatDate(instant)).toBe(new NativeDateTimeFormat('es', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(instant)));
    expect(result.current.formatNumber(1234567.89)).toBe(new Intl.NumberFormat('es').format(1234567.89));
    expect(dates).toHaveBeenCalledWith('es', { dateStyle: 'medium', timeStyle: 'short' });
    expect(dates.mock.calls.every(([, options]) => !Object.hasOwn(options ?? {}, 'timeZone'))).toBe(true);
    expect(numbers).toHaveBeenCalledWith('es');
    dates.mockRestore();
    numbers.mockRestore();
  });

  it('uses a localized neutral fallback for missing or malformed timestamps', async () => {
    const { result } = renderHook(() => useFormat());
    const invalidDates = [null, undefined, '', '  ', 'not-a-date', '2026-99-99', NaN, new Date(NaN), {}, false];
    for (const value of invalidDates) expect(result.current.formatDate(value)).toBe('Date unavailable');
    await act(() => setLanguage('es'));
    for (const value of invalidDates) expect(result.current.formatDate(value)).toBe('Fecha no disponible');
  });

  it('translates only built-in roles even when a custom name matches a system name', async () => {
    await act(() => setLanguage('es'));
    for (const [name, expected] of [['Owner', 'Propietario'], ['Administrator', 'Administrador']]) {
      expect(roleName({ name, isSystem: true }, i18n.t.bind(i18n))).toBe(expected);
      expect(roleName({ name, isSystem: false }, i18n.t.bind(i18n))).toBe(name);
    }
  });
});
