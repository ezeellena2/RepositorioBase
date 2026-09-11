import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { createApiTransport } from './apiTransport';
import { server } from '../../../test/server';
import { antiforgery, problem } from '../../../test/identityServer';

describe('API transport session loss', () => {
  it.each(['authentication_required', 'invalid_session'])(
    'publishes %s from a protected mutation',
    async (code) => {
      server.use(
        antiforgery(),
        http.put('/api/identity/context/language', () => problem(401, code)),
      );
      const transport = createApiTransport();
      const observed = [];
      const unsubscribe = transport.onSessionLost((value) => observed.push(value));

      await expect(transport.send('/api/identity/context/language', {
        method: 'PUT',
        body: { language: 'es' },
      })).rejects.toMatchObject({ problem: { code, status: 401 } });

      expect(observed).toEqual([expect.objectContaining({ code, status: 401 })]);
      expect(transport.hasRequestToken()).toBe(false);
      unsubscribe();
    },
  );

  it('invalidates the session-bound token after an anonymous context response without publishing or replaying it', async () => {
    let bootstraps = 0;
    let contextAttempts = 0;
    const mutationTokens = [];
    server.use(
      http.get('/api/identity/antiforgery', () => {
        bootstraps += 1;
        return HttpResponse.json({ requestToken: `token-${bootstraps}` });
      }),
      http.get('/api/identity/context', () => {
        contextAttempts += 1;
        return problem(401, 'authentication_required');
      }),
      http.put('/api/test/mutation', ({ request }) => {
        mutationTokens.push(request.headers.get('X-CSRF-TOKEN'));
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const transport = createApiTransport();
    const observed = [];
    transport.onSessionLost((value) => observed.push(value));
    await transport.bootstrapAntiforgery();

    await expect(transport.send('/api/identity/context')).rejects.toMatchObject({
      problem: { code: 'authentication_required', status: 401 },
    });

    expect(observed).toEqual([]);
    expect(transport.hasRequestToken()).toBe(false);
    expect(contextAttempts).toBe(1);

    await transport.send('/api/test/mutation', { method: 'PUT' });

    expect(bootstraps).toBe(2);
    expect(mutationTokens).toEqual(['token-2']);
    expect(contextAttempts).toBe(1);
  });
});
