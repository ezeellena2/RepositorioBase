import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { antiforgery, problem } from '../../../test/identityServer';
import { server } from '../../../test/server';
import * as apiTransport from './apiTransport';

const { ApiProblem, createApiTransport } = apiTransport;

const failed = (promise) => promise.catch((failure) => failure);

const refused = async (transport, path) => {
  const failure = await transport.send(path).catch((candidate) => candidate);

  expect(failure).toBeInstanceOf(ApiProblem);
  return failure.problem;
};

afterEach(() => {
  vi.restoreAllMocks();
});

describe('API transport failure classification', () => {
  it('classifies a fetch TypeError as a network failure', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new TypeError('offline'));

    const failure = await failed(createApiTransport().send('/api/read'));

    expect(failure).toBeInstanceOf(apiTransport.ClientFailure);
    expect(failure.problem).toEqual({ code: 'network_unavailable', status: 0 });
  });

  it('uses a 30 second transport deadline and classifies that signal firing as a timeout', async () => {
    const deadline = new AbortController();
    deadline.abort(new DOMException('deadline', 'TimeoutError'));
    const timeout = vi.spyOn(AbortSignal, 'timeout').mockReturnValue(deadline.signal);
    const fetch = vi.spyOn(globalThis, 'fetch').mockImplementation((_path, init) =>
      Promise.reject(init?.signal?.reason ?? new DOMException('missing deadline', 'AbortError')));

    const failure = await failed(createApiTransport().bootstrapAntiforgery());

    expect(timeout).toHaveBeenCalledWith(30_000);
    expect(fetch.mock.calls[0][1].signal).toBe(deadline.signal);
    expect(failure.problem).toEqual({ code: 'request_timeout', status: 0 });
  });

  it('composes a caller signal with the deadline instead of replacing either one', async () => {
    const caller = new AbortController();
    const deadline = new AbortController();
    const combined = new AbortController();
    vi.spyOn(AbortSignal, 'timeout').mockReturnValue(deadline.signal);
    const any = vi.spyOn(AbortSignal, 'any').mockReturnValue(combined.signal);
    const fetch = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(HttpResponse.json({ value: 'ok' }));

    await createApiTransport().send('/api/read', { expect: ['value'], signal: caller.signal });

    expect(any).toHaveBeenCalledWith([caller.signal, deadline.signal]);
    expect(fetch.mock.calls[0][1].signal).toBe(combined.signal);
  });

  it('passes the caller signal through a lazy antiforgery bootstrap and the mutation request', async () => {
    const caller = new AbortController();
    const any = vi.spyOn(AbortSignal, 'any');
    server.use(http.get('/api/identity/antiforgery', () =>
      HttpResponse.json({ requestToken: 'request-token' })));
    server.use(http.post('/api/mutation', () => new HttpResponse(null, { status: 204 })));

    await createApiTransport().send('/api/mutation', { method: 'POST', signal: caller.signal });

    expect(any).toHaveBeenCalledTimes(2);
    expect(any.mock.calls.every(([signals]) => signals[0] === caller.signal)).toBe(true);
  });

  it('keeps caller cancellation distinct from the transport timeout', async () => {
    const caller = new AbortController();
    caller.abort(new DOMException('unmounted', 'AbortError'));
    const deadline = new AbortController();
    vi.spyOn(AbortSignal, 'timeout').mockReturnValue(deadline.signal);
    vi.spyOn(AbortSignal, 'any').mockReturnValue(caller.signal);
    vi.spyOn(globalThis, 'fetch').mockImplementation((_path, init) =>
      Promise.reject(init?.signal?.reason ?? caller.signal.reason));

    const failure = await failed(createApiTransport().send('/api/read', { signal: caller.signal }));

    expect(deadline.signal.aborted).toBe(false);
    expect(failure.problem).toEqual({ code: 'client_failure', status: 0 });
  });

  it('keeps the transport deadline classification while reading the response body', async () => {
    const deadline = new AbortController();
    vi.spyOn(AbortSignal, 'timeout').mockReturnValue(deadline.signal);
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers({ 'Content-Type': 'application/json' }),
      text: vi.fn().mockImplementation(async () => {
        deadline.abort(new DOMException('deadline', 'TimeoutError'));
        throw deadline.signal.reason;
      }),
    });

    const failure = await failed(createApiTransport().send('/api/read', { expect: ['value'] }));

    expect(failure.problem).toEqual({ code: 'request_timeout', status: 0 });
  });

  it('keeps caller cancellation distinct while reading the response body', async () => {
    const caller = new AbortController();
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers({ 'Content-Type': 'application/json' }),
      text: vi.fn().mockImplementation(async () => {
        caller.abort(new DOMException('unmounted', 'AbortError'));
        throw caller.signal.reason;
      }),
    });

    const failure = await failed(createApiTransport().send('/api/read', {
      expect: ['value'],
      signal: caller.signal,
    }));

    expect(failure.problem).toEqual({ code: 'client_failure', status: 0 });
  });

  it.each([
    ['a non-problem refusal', '/api/non-problem', () => new HttpResponse('<html>bad gateway</html>', { status: 502, headers: { 'Content-Type': 'text/html' } })],
    ['a contract-drifting success', '/api/drift', () => HttpResponse.json({ succeeded: true, value: { item: 1 } })],
    ['an empty declared success', '/api/empty-success', () => new HttpResponse(null, { status: 200 })],
  ])('classifies %s as an unreadable response', async (_name, path, response) => {
    server.use(http.get(path, response));

    const failure = await failed(createApiTransport().send(path, { expect: ['item'] }));

    expect(failure).toBeInstanceOf(apiTransport.ClientFailure);
    expect(failure.problem).toEqual({ code: 'unreadable_response', status: 0 });
  });

  it('preserves a valid server 500 as an ApiProblem with its opaque reference', async () => {
    server.use(http.get('/api/fault', () => problem(500, 'internal_server_error', {
      traceId: 'trace-500',
      detail: 'must stay private',
    })));

    const failure = await failed(createApiTransport().send('/api/fault'));

    expect(failure).toBeInstanceOf(ApiProblem);
    expect(failure.problem).toEqual({
      status: 500,
      code: 'internal_server_error',
      traceId: 'trace-500',
      errors: undefined,
      detail: undefined,
      retryAfterSeconds: undefined,
    });
  });

  it('normalizes only failures that do not already carry a problem', () => {
    const serverProblem = { code: 'permission_denied', status: 403, traceId: 'trace-403' };
    const clientProblem = { code: 'network_unavailable', status: 0 };

    expect(apiTransport.toProblem({ problem: serverProblem })).toBe(serverProblem);
    expect(apiTransport.toProblem({ problem: clientProblem })).toBe(clientProblem);
    expect(apiTransport.toProblem(new Error('private'))).toEqual({ code: 'client_failure', status: 0 });
  });

  it.each([
    [0, true],
    [400, false],
    [401, false],
    [403, false],
    [404, false],
    [409, false],
    [429, true],
    [500, true],
    [503, true],
  ])('decides retryability from status %d', (status, expected) => {
    expect(apiTransport.isRetryable({ status })).toBe(expected);
  });
});

describe('API transport antiforgery lifecycle', () => {
  it('classifies an empty successful bootstrap through the shared unreadable response boundary', async () => {
    server.use(http.get('/api/identity/antiforgery', () => new HttpResponse(null, { status: 200 })));

    const failure = await failed(createApiTransport().bootstrapAntiforgery());

    expect(failure).toBeInstanceOf(apiTransport.ClientFailure);
    expect(failure.problem).toEqual({ code: 'unreadable_response', status: 0 });
  });

  it('preserves a problem document answered by antiforgery bootstrap', async () => {
    server.use(http.get('/api/identity/antiforgery', () =>
      problem(503, 'service_unavailable', {}, { 'Retry-After': '7' })));

    const failure = await failed(createApiTransport().bootstrapAntiforgery());

    expect(failure).toBeInstanceOf(ApiProblem);
    expect(failure.problem).toMatchObject({
      status: 503,
      code: 'service_unavailable',
      retryAfterSeconds: 7,
    });
  });

  it('clears a stale token before refresh and bootstraps lazily on the next mutation', async () => {
    let bootstraps = 0;
    const mutationTokens = [];
    server.use(http.get('/api/identity/antiforgery', () => {
      bootstraps += 1;
      if (bootstraps === 2) return problem(503, 'service_unavailable');
      return HttpResponse.json({ requestToken: bootstraps === 1 ? 'token-old' : 'token-new' });
    }));
    server.use(http.post('/api/mutation', ({ request }) => {
      mutationTokens.push(request.headers.get('X-CSRF-TOKEN'));
      return new HttpResponse(null, { status: 204 });
    }));
    const transport = createApiTransport();
    await transport.bootstrapAntiforgery();

    await failed(transport.bootstrapAntiforgery());
    expect(transport.hasRequestToken()).toBe(false);

    await transport.send('/api/mutation', { method: 'POST' });
    expect(bootstraps).toBe(3);
    expect(mutationTokens).toEqual(['token-new']);
  });

  it('keeps the original antiforgery refusal when refreshing the pair also fails and never replays', async () => {
    let bootstraps = 0;
    let attempts = 0;
    server.use(http.get('/api/identity/antiforgery', () => {
      bootstraps += 1;
      return bootstraps === 1
        ? HttpResponse.json({ requestToken: 'token-1' })
        : problem(503, 'service_unavailable');
    }));
    server.use(http.post('/api/mutation', () => {
      attempts += 1;
      return problem(400, 'antiforgery_validation_failed');
    }));
    const transport = createApiTransport();

    const failure = await failed(transport.send('/api/mutation', { method: 'POST' }));

    expect(failure).toBeInstanceOf(ApiProblem);
    expect(failure.problem).toMatchObject({ status: 400, code: 'antiforgery_validation_failed' });
    expect(attempts).toBe(1);
    expect(transport.hasRequestToken()).toBe(false);
  });
});

describe('API transport session loss', () => {
  it.each(['invalid_session', 'authentication_required'])(
    'reports a non-context 401 whose code is exactly %s and still rejects it',
    async (code) => {
      const listener = vi.fn();
      const transport = createApiTransport();
      transport.onSessionLost(listener);
      server.use(http.get('/api/protected-operation', () => problem(401, code)));

      const refusal = await refused(transport, '/api/protected-operation');

      expect(refusal).toMatchObject({ status: 401, code });
      expect(listener).toHaveBeenCalledOnce();
      expect(listener).toHaveBeenCalledWith(expect.objectContaining({ status: 401, code }));
    },
  );

  it.each(['invalid_session', 'authentication_required'])(
    'does not recursively report %s from the identity context read',
    async (code) => {
      const listener = vi.fn();
      const transport = createApiTransport();
      transport.onSessionLost(listener);
      server.use(http.get('/api/identity/context', () => problem(401, code)));

      const refusal = await refused(transport, '/api/identity/context');

      expect(refusal).toMatchObject({ status: 401, code });
      expect(listener).not.toHaveBeenCalled();
    },
  );

  it.each([
    [401, 'credential_superseded'],
    [401, 'recent_proof_required'],
    [401, 'recent_mfa_required'],
    [409, 'session_concurrency_conflict'],
  ])('does not report status %d with code %s as session loss', async (status, code) => {
    const listener = vi.fn();
    const transport = createApiTransport();
    transport.onSessionLost(listener);
    server.use(http.get('/api/protected-operation', () => problem(status, code)));

    const refusal = await refused(transport, '/api/protected-operation');

    expect(refusal).toMatchObject({ status, code });
    expect(listener).not.toHaveBeenCalled();
  });

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
