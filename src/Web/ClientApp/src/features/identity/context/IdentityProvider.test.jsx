import { useState } from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { IdentityProvider, useIdentity } from './IdentityProvider';
import { server } from '../../../test/server';
import { ANTIFORGERY_TOKEN, antiforgery, contextIs, problem, signedInContext } from '../../../test/identityServer';
import { i18n, setLanguage } from '../../../i18n';

function Probe() {
  const identity = useIdentity();
  const [problem, setProblem] = useState(null);
  if (!identity || identity.isLoading) return <p>loading</p>;
  return (
    <>
      <p data-testid="authenticated">{String(identity.isAuthenticated)}</p>
      <p data-testid="tenant">{identity.context?.activeTenant?.name ?? 'none'}</p>
      <p data-testid="permissions">{identity.context?.permissions?.join(',') ?? ''}</p>
      <p data-testid="preferred-language">{identity.context?.preferredLanguage ?? 'none'}</p>
      <p data-testid="pending-language">{identity.pendingLanguage ?? 'none'}</p>
      <p data-testid="language-problem">{identity.languageProblem?.code ?? ''}</p>
      <p data-testid="context-problem">{identity.contextProblem?.code ?? ''}</p>
      <p data-testid="problem">{problem ?? ''}</p>
      <button onClick={() => identity.signIn('ana@example.test', 'Testing1234!').catch((failure) => setProblem(failure.problem?.code ?? 'unknown'))}>sign in</button>
      <button onClick={() => identity.signOut()}>sign out</button>
      <button onClick={() => identity.changeLanguage('es').catch(() => undefined)}>language es</button>
      <button onClick={() => identity.changeLanguage('en').catch(() => undefined)}>language en</button>
      <button onClick={() => identity.selectTenant('tenant-2').catch(() => undefined)}>select tenant</button>
      <button onClick={() => identity.reload()}>reload</button>
    </>
  );
}

const renderProbe = (client) => render(<IdentityProvider client={client}><Probe /></IdentityProvider>);

const deferred = () => {
  let resolve;
  let reject;
  const promise = new Promise((complete, fail) => {
    resolve = complete;
    reject = fail;
  });
  return { promise, resolve, reject };
};

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

  it('persists a signed-in choice before switching the local catalog and cookie', async () => {
    const write = deferred();
    let request;
    await act(() => setLanguage('en'));
    server.use(
      antiforgery(),
      contextIs(signedInContext({ preferredLanguage: 'en' })),
      http.put('/api/identity/context/language', async ({ request: incoming }) => {
        request = {
          body: await incoming.json(),
          antiforgery: incoming.headers.get('X-CSRF-TOKEN'),
        };
        await write.promise;
        return HttpResponse.json(signedInContext({ preferredLanguage: 'es' }));
      }),
    );
    renderProbe();
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'language es' }));
    await waitFor(() => expect(request).toEqual({ body: { language: 'es' }, antiforgery: ANTIFORGERY_TOKEN }));
    expect(screen.getByTestId('pending-language')).toHaveTextContent('es');
    expect(screen.getByTestId('preferred-language')).toHaveTextContent('en');
    expect(i18n.resolvedLanguage).toBe('en');
    expect(document.documentElement.lang).toBe('en');

    await act(async () => { write.resolve(); await write.promise; });
    await waitFor(() => expect(screen.getByTestId('preferred-language')).toHaveTextContent('es'));
    expect(screen.getByTestId('pending-language')).toHaveTextContent('none');
    expect(i18n.resolvedLanguage).toBe('es');
    expect(document.documentElement.lang).toBe('es');
    expect(document.cookie).toContain('c=es|uic=es');
  });

  it('serializes language writes so the latest choice is also the persisted preference', async () => {
    const spanish = deferred();
    const english = deferred();
    const started = [];
    let serverPreference = 'en';
    await act(() => setLanguage('en'));
    const client = {
      bootstrapAntiforgery: async () => undefined,
      getContext: async () => signedInContext({ preferredLanguage: 'en' }),
      updatePreferredLanguage: (language) => {
        started.push(language);
        const write = language === 'es' ? spanish : english;
        return write.promise.then(() => {
          serverPreference = language;
          return signedInContext({ preferredLanguage: language });
        });
      },
    };
    renderProbe(client);
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'language es' }));
    await userEvent.click(screen.getByRole('button', { name: 'language en' }));
    await waitFor(() => expect(started).toEqual(['es']));
    expect(screen.getByTestId('pending-language')).toHaveTextContent('en');

    await act(async () => {
      spanish.resolve();
      await spanish.promise;
    });
    await waitFor(() => expect(started).toEqual(['es', 'en']));

    await act(async () => {
      english.resolve();
      await english.promise;
    });
    await waitFor(() => expect(screen.getByTestId('pending-language')).toHaveTextContent('none'));

    expect(serverPreference).toBe('en');
    expect(screen.getByTestId('preferred-language')).toHaveTextContent('en');
    expect(i18n.resolvedLanguage).toBe('en');
  });

  it('continues a queued language write after the preceding write is refused', async () => {
    const spanish = deferred();
    const english = deferred();
    const started = [];
    let serverPreference = 'en';
    await act(() => setLanguage('en'));
    const client = {
      bootstrapAntiforgery: async () => undefined,
      getContext: async () => signedInContext({ preferredLanguage: 'en' }),
      updatePreferredLanguage: (language) => {
        started.push(language);
        const write = language === 'es' ? spanish : english;
        return write.promise.then(() => {
          serverPreference = language;
          return signedInContext({ preferredLanguage: language });
        });
      },
    };
    renderProbe(client);
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'language es' }));
    await userEvent.click(screen.getByRole('button', { name: 'language en' }));
    await waitFor(() => expect(started).toEqual(['es']));

    await act(async () => {
      spanish.reject({ problem: { code: 'language_preference_unavailable', status: 503 } });
      await spanish.promise.catch(() => undefined);
    });
    await waitFor(() => expect(started).toEqual(['es', 'en']));

    await act(async () => {
      english.resolve();
      await english.promise;
    });
    await waitFor(() => expect(screen.getByTestId('pending-language')).toHaveTextContent('none'));

    expect(serverPreference).toBe('en');
    expect(screen.getByTestId('preferred-language')).toHaveTextContent('en');
    expect(screen.getByTestId('language-problem')).toBeEmptyDOMElement();
    expect(i18n.resolvedLanguage).toBe('en');
  });

  it('does not start a queued language write after sign-out', async () => {
    const spanish = deferred();
    const started = [];
    const client = {
      bootstrapAntiforgery: async () => undefined,
      getContext: async () => signedInContext({ preferredLanguage: 'en' }),
      updatePreferredLanguage: (language) => {
        started.push(language);
        return spanish.promise.then(() => signedInContext({ preferredLanguage: language }));
      },
      signOut: async () => undefined,
    };
    renderProbe(client);
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'language es' }));
    await userEvent.click(screen.getByRole('button', { name: 'language en' }));
    await waitFor(() => expect(started).toEqual(['es']));
    await userEvent.click(screen.getByRole('button', { name: 'sign out' }));
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('false'));

    await act(async () => {
      spanish.resolve();
      await spanish.promise;
    });
    await waitFor(() => expect(started).toEqual(['es']));
    expect(screen.getByTestId('pending-language')).toHaveTextContent('none');
  });

  it('orders a new-session language write after an in-flight write from the signed-out session', async () => {
    const oldSpanish = deferred();
    const newEnglish = deferred();
    const started = [];
    let clientSession = 'old';
    let serverPreference = 'en';
    await act(() => setLanguage('en'));
    const client = {
      bootstrapAntiforgery: async () => undefined,
      getContext: async () => signedInContext({ preferredLanguage: serverPreference }),
      updatePreferredLanguage: (language) => {
        const requestSession = clientSession;
        started.push(`${requestSession}:${language}`);
        const write = requestSession === 'old' ? oldSpanish : newEnglish;
        return write.promise.then(() => {
          serverPreference = language;
          return signedInContext({ preferredLanguage: language });
        });
      },
      signOut: async () => { clientSession = 'signed-out'; },
      signIn: async () => { clientSession = 'new'; },
    };
    renderProbe(client);
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'language es' }));
    await waitFor(() => expect(started).toEqual(['old:es']));
    await userEvent.click(screen.getByRole('button', { name: 'sign out' }));
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('false'));
    await userEvent.click(screen.getByRole('button', { name: 'sign in' }));
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'language en' }));
    await act(async () => {
      newEnglish.resolve();
      await newEnglish.promise;
    });
    await act(async () => {
      oldSpanish.resolve();
      await oldSpanish.promise;
    });

    await waitFor(() => expect(started).toEqual(['old:es', 'new:en']));
    await waitFor(() => expect(screen.getByTestId('pending-language')).toHaveTextContent('none'));
    expect(serverPreference).toBe('en');
    expect(screen.getByTestId('preferred-language')).toHaveTextContent('en');
  });

  it('applies a non-null account preference returned by a context reload', async () => {
    let preferredLanguage = null;
    await act(() => setLanguage('en'));
    server.use(
      antiforgery(),
      http.get('/api/identity/context', () => HttpResponse.json(signedInContext({ preferredLanguage }))),
    );
    renderProbe();
    await waitFor(() => expect(screen.getByTestId('preferred-language')).toHaveTextContent('none'));

    preferredLanguage = 'es';
    await userEvent.click(screen.getByRole('button', { name: 'reload' }));

    await waitFor(() => expect(screen.getByTestId('preferred-language')).toHaveTextContent('es'));
    expect(i18n.resolvedLanguage).toBe('es');
    expect(document.documentElement.lang).toBe('es');
  });

  it('does not let a reload started before a language choice revert the saved preference', async () => {
    const reload = deferred();
    let contextReads = 0;
    await act(() => setLanguage('en'));
    const client = {
      bootstrapAntiforgery: async () => undefined,
      getContext: () => ++contextReads === 1
        ? Promise.resolve(signedInContext({ preferredLanguage: 'en' }))
        : reload.promise,
      updatePreferredLanguage: async () => signedInContext({ preferredLanguage: 'es' }),
    };
    renderProbe(client);
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'reload' }));
    await userEvent.click(screen.getByRole('button', { name: 'language es' }));
    await waitFor(() => expect(screen.getByTestId('preferred-language')).toHaveTextContent('es'));

    await act(async () => {
      reload.resolve(signedInContext({ preferredLanguage: 'en' }));
      await reload.promise;
    });
    expect(screen.getByTestId('preferred-language')).toHaveTextContent('es');
    expect(i18n.resolvedLanguage).toBe('es');
  });

  it('applies a tenant response begun before a language choice without reverting the language', async () => {
    const tenant = deferred();
    await act(() => setLanguage('en'));
    const client = {
      bootstrapAntiforgery: async () => undefined,
      getContext: async () => signedInContext({ preferredLanguage: 'en' }),
      selectTenant: () => tenant.promise,
      updatePreferredLanguage: async () => signedInContext({ preferredLanguage: 'es' }),
    };
    renderProbe(client);
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'select tenant' }));
    await userEvent.click(screen.getByRole('button', { name: 'language es' }));
    await waitFor(() => expect(screen.getByTestId('preferred-language')).toHaveTextContent('es'));

    await act(async () => {
      tenant.resolve(signedInContext({
        preferredLanguage: 'en',
        activeTenant: { id: 'tenant-2', name: 'Beta', type: 'Organization' },
        permissions: ['roles.read'],
      }));
      await tenant.promise;
    });

    expect(screen.getByTestId('tenant')).toHaveTextContent('Beta');
    expect(screen.getByTestId('permissions')).toHaveTextContent('roles.read');
    expect(screen.getByTestId('preferred-language')).toHaveTextContent('es');
    expect(i18n.resolvedLanguage).toBe('es');
  });

  it('ignores a reload failure from before a later language choice', async () => {
    const reload = deferred();
    let reads = 0;
    await act(() => setLanguage('en'));
    const client = {
      bootstrapAntiforgery: async () => undefined,
      getContext: () => ++reads === 1
        ? Promise.resolve(signedInContext({ preferredLanguage: 'en' }))
        : reload.promise,
      updatePreferredLanguage: async () => signedInContext({ preferredLanguage: 'es' }),
    };
    renderProbe(client);
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'reload' }));
    await userEvent.click(screen.getByRole('button', { name: 'language es' }));
    await waitFor(() => expect(screen.getByTestId('preferred-language')).toHaveTextContent('es'));

    await act(async () => {
      reload.reject(new Error('late reload failure'));
      await reload.promise.catch(() => undefined);
    });

    expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
    expect(screen.getByTestId('context-problem')).toBeEmptyDOMElement();
    expect(screen.getByTestId('preferred-language')).toHaveTextContent('es');
    expect(i18n.resolvedLanguage).toBe('es');
  });

  it('reconciles to an acknowledged earlier write when the latest queued write fails', async () => {
    const spanish = deferred();
    const english = deferred();
    const started = [];
    await act(() => setLanguage('en'));
    const client = {
      bootstrapAntiforgery: async () => undefined,
      getContext: async () => signedInContext({ preferredLanguage: 'en' }),
      updatePreferredLanguage: (language) => {
        started.push(language);
        return language === 'es' ? spanish.promise : english.promise;
      },
    };
    renderProbe(client);
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'language es' }));
    await userEvent.click(screen.getByRole('button', { name: 'language en' }));
    await waitFor(() => expect(started).toEqual(['es']));

    await act(async () => {
      spanish.resolve(signedInContext({ preferredLanguage: 'es' }));
      await spanish.promise;
    });
    await waitFor(() => expect(started).toEqual(['es', 'en']));

    await act(async () => {
      english.reject({ problem: { code: 'language_preference_unavailable', status: 503 } });
      await english.promise.catch(() => undefined);
    });

    await waitFor(() => expect(screen.getByTestId('pending-language')).toHaveTextContent('none'));
    expect(screen.getByTestId('preferred-language')).toHaveTextContent('es');
    expect(screen.getByTestId('language-problem')).toHaveTextContent('language_preference_unavailable');
    expect(i18n.resolvedLanguage).toBe('es');
    expect(document.cookie).toContain('c=es|uic=es');
  });

  it.each(['authentication_required', 'invalid_session'])(
    'uses the central session-loss transition when a language write returns %s',
    async (code) => {
      const first = deferred();
      const started = [];
      let sessionLost;
      const client = {
        bootstrapAntiforgery: async () => undefined,
        getContext: async () => signedInContext({ preferredLanguage: 'en' }),
        updatePreferredLanguage: (language) => {
          started.push(language);
          return first.promise;
        },
        transport: {
          onSessionLost: (listener) => {
            sessionLost = listener;
            return () => { sessionLost = undefined; };
          },
        },
      };
      renderProbe(client);
      await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

      await userEvent.click(screen.getByRole('button', { name: 'language es' }));
      await userEvent.click(screen.getByRole('button', { name: 'language en' }));
      await waitFor(() => expect(started).toEqual(['es']));

      await act(async () => {
        sessionLost({ code, status: 401 });
        first.reject({ problem: { code, status: 401 } });
        await first.promise.catch(() => undefined);
      });

      await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('false'));
      expect(started).toEqual(['es']);
      expect(screen.getByTestId('pending-language')).toHaveTextContent('none');
      expect(screen.getByTestId('language-problem')).toBeEmptyDOMElement();
      expect(screen.getByTestId('context-problem')).toHaveTextContent(code);
    },
  );

  it('merges a tenant response started during a language save without reverting the preference', async () => {
    const language = deferred();
    const tenant = deferred();
    await act(() => setLanguage('en'));
    const client = {
      bootstrapAntiforgery: async () => undefined,
      getContext: async () => signedInContext({ preferredLanguage: 'en' }),
      updatePreferredLanguage: () => language.promise,
      selectTenant: () => tenant.promise,
    };
    renderProbe(client);
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'language es' }));
    await userEvent.click(screen.getByRole('button', { name: 'select tenant' }));
    await act(async () => {
      language.resolve(signedInContext({ preferredLanguage: 'es' }));
      await language.promise;
      tenant.resolve(signedInContext({
        preferredLanguage: 'en',
        activeTenant: { id: 'tenant-2', name: 'Beta', type: 'Organization' },
      }));
      await tenant.promise;
    });

    await waitFor(() => expect(screen.getByTestId('tenant')).toHaveTextContent('Beta'));
    expect(screen.getByTestId('preferred-language')).toHaveTextContent('es');
    expect(i18n.resolvedLanguage).toBe('es');
  });

  it('does not let a pre-sign-out reload restore the authenticated context', async () => {
    const reload = deferred();
    let contextReads = 0;
    const client = {
      bootstrapAntiforgery: async () => undefined,
      getContext: () => ++contextReads === 1
        ? Promise.resolve(signedInContext({ preferredLanguage: 'en' }))
        : reload.promise,
      signOut: async () => undefined,
    };
    renderProbe(client);
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('true'));

    await userEvent.click(screen.getByRole('button', { name: 'reload' }));
    await userEvent.click(screen.getByRole('button', { name: 'sign out' }));
    await waitFor(() => expect(screen.getByTestId('authenticated')).toHaveTextContent('false'));

    await act(async () => {
      reload.resolve(signedInContext({ preferredLanguage: 'en' }));
      await reload.promise;
    });
    expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
    expect(screen.getByTestId('tenant')).toHaveTextContent('none');
  });
});
