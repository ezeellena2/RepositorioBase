// @vitest-environment-options {"url":"http://localhost/?lng=en-XA"}

import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { NavMenu } from '../components/NavMenu';
import { IdentityProvider, useIdentity } from '../features/identity/context/IdentityProvider';
import { signedInContext } from '../test/identityServer';
import {
  i18n,
  isPseudoLanguageOverrideActive,
  PSEUDO_LANGUAGE,
  sourceLanguage,
  supportedLanguages,
} from './index';

const targetLanguage = 'es';

function LanguagePreferenceProbe() {
  const identity = useIdentity();
  return (
    <button
      type="button"
      data-testid="request-language-change"
      onClick={() => { void identity.changeLanguage(targetLanguage); }}
    />
  );
}

const deferred = () => {
  let resolve;
  const promise = new Promise((complete) => { resolve = complete; });
  return { promise, resolve };
};

describe('development pseudo-language startup override', () => {
  it('survives client-side query removal and blocks preference application and persistence', async () => {
    const context = deferred();
    const client = {
      bootstrapAntiforgery: vi.fn().mockResolvedValue(undefined),
      getContext: vi.fn(() => context.promise),
      updatePreferredLanguage: vi.fn(),
    };
    document.cookie = '.AspNetCore.Culture=c=en|uic=en; path=/';

    expect(isPseudoLanguageOverrideActive()).toBe(true);
    expect(i18n.resolvedLanguage).toBe(PSEUDO_LANGUAGE);

    render(
      <MemoryRouter initialEntries={[window.location.pathname]}>
        <IdentityProvider client={client}>
          <NavMenu />
          <LanguagePreferenceProbe />
        </IdentityProvider>
      </MemoryRouter>,
    );
    await waitFor(() => expect(client.getContext).toHaveBeenCalledOnce());

    const urlWithoutPseudoQuery = new URL(window.location.href);
    urlWithoutPseudoQuery.search = '';
    window.history.replaceState({}, '', urlWithoutPseudoQuery);
    expect(window.location.search).toBe('');
    await act(async () => {
      context.resolve(signedInContext({ preferredLanguage: targetLanguage }));
      await context.promise;
    });

    const pseudoAccessLabel = i18n.t('navigation.yourAccess');
    await screen.findByRole('link', { name: pseudoAccessLabel });
    const selector = screen.getByRole('combobox');
    expect(isPseudoLanguageOverrideActive()).toBe(true);
    expect(selector).toHaveAttribute('aria-disabled', 'true');
    expect(selector).toHaveTextContent(i18n.t(`language.${sourceLanguage}`));
    expect(document.querySelector('input[name="language"]')).toHaveValue(sourceLanguage);
    expect(supportedLanguages).not.toContain(PSEUDO_LANGUAGE);

    await userEvent.click(screen.getByTestId('request-language-change'));

    expect(client.updatePreferredLanguage).not.toHaveBeenCalled();
    expect(i18n.resolvedLanguage).toBe(PSEUDO_LANGUAGE);
    expect(document.documentElement.lang).toBe(PSEUDO_LANGUAGE);
    expect(document.cookie).toContain('c=en|uic=en');
    expect(document.cookie).not.toContain(PSEUDO_LANGUAGE);
  });
});
