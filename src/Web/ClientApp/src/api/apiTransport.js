import { isProblem, readProblem, readSuccess } from './problemDetails';

const ANTIFORGERY = '/api/identity/antiforgery';
const IDENTITY_CONTEXT = '/api/identity/context';
const REQUEST_TIMEOUT_MS = 30_000;
const SESSION_LOST_CODES = new Set(['invalid_session', 'authentication_required']);

export const isSessionLostProblem = (problem) =>
  problem?.status === 401 && SESSION_LOST_CODES.has(problem.code);

export class ApiProblem extends Error {
  constructor(problem) {
    super(problem.code);
    this.name = 'ApiProblem';
    this.problem = problem;
  }
}

export class ClientFailure extends Error {
  constructor(code) {
    super(code);
    this.name = 'ClientFailure';
    this.problem = { code, status: 0 };
  }
}

export const toProblem = (failure) => failure?.problem ?? { code: 'client_failure', status: 0 };

export const isRetryable = (problem) =>
  problem?.status === 0 || problem?.status === 429 || problem?.status >= 500;

/**
 * How this application talks to the API, and the one place that holds the antiforgery request token.
 *
 * There is exactly one holder on purpose. The server rotates the cookie/token pair whenever the authentication
 * state changes, so a second client caching its own copy would be holding an invalidated token from the moment
 * the first one signed in — and would discover it as a refused mutation rather than as the bug it is.
 *
 * Every request is same-origin and carries the session cookie; only mutations carry the request token, which is
 * held in memory and never written to storage (IA-REQ-025). A refused antiforgery replaces the pair and rethrows
 * without replaying: a state change nobody asked for twice is how one sign-in becomes two sessions.
 */
export function createApiTransport() {
  let requestToken = null;
  const sessionLostListeners = new Set();

  const request = async (path, init, callerSignal) => {
    const timeoutSignal = AbortSignal.timeout(REQUEST_TIMEOUT_MS);
    const signal = callerSignal === undefined
      ? timeoutSignal
      : AbortSignal.any([callerSignal, timeoutSignal]);

    try {
      return { response: await fetch(path, { ...init, signal }), timeoutSignal };
    } catch (failure) {
      if (timeoutSignal.aborted) throw new ClientFailure('request_timeout');
      if (callerSignal?.aborted) throw new ClientFailure('client_failure');
      if (failure instanceof TypeError) throw new ClientFailure('network_unavailable');
      throw new ClientFailure('client_failure');
    }
  };

  const read = async (operation, timeoutSignal, callerSignal) => {
    try {
      return await operation();
    } catch {
      if (timeoutSignal.aborted) throw new ClientFailure('request_timeout');
      if (callerSignal?.aborted) throw new ClientFailure('client_failure');
      throw new ClientFailure('unreadable_response');
    }
  };

  const success = (response, expectedMembers, options, timeoutSignal, callerSignal) => read(async () => {
    const payload = await readSuccess(response, expectedMembers, options);
    if (payload === null && expectedMembers.length > 0) {
      throw new Error('The response omitted its declared body.');
    }
    return payload;
  }, timeoutSignal, callerSignal);

  const refusal = async (path, response, timeoutSignal, callerSignal) => {
    if (!isProblem(response)) throw new ClientFailure('unreadable_response');

    const problem = await read(() => readProblem(response), timeoutSignal, callerSignal);
    if (isSessionLostProblem(problem)) {
      requestToken = null;
      if (path !== IDENTITY_CONTEXT) {
        sessionLostListeners.forEach((listener) => listener(problem));
      }
    }
    return problem;
  };

  const bootstrapAntiforgery = async ({ signal } = {}) => {
    requestToken = null;
    const { response, timeoutSignal } = await request(ANTIFORGERY, {}, signal);
    if (!response.ok) {
      throw new ApiProblem(await refusal(ANTIFORGERY, response, timeoutSignal, signal));
    }
    const payload = await success(response, ['requestToken'], {}, timeoutSignal, signal);
    requestToken = payload.requestToken;
    return requestToken;
  };

  const send = async (path, {
    method = 'GET', body, expect = [], expectArray = false, expectStatus, signal,
  } = {}) => {
    const mutation = method !== 'GET';
    if (mutation && requestToken === null) await bootstrapAntiforgery({ signal });

    const { response, timeoutSignal } = await request(path, {
      method,
      headers: {
        Accept: 'application/json, application/problem+json',
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(mutation ? { 'X-CSRF-TOKEN': requestToken } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    }, signal);

    if (response.ok) {
      if (expectStatus !== undefined && response.status !== expectStatus) {
        throw new ClientFailure('unreadable_response');
      }
      return success(response, expect, { asArray: expectArray }, timeoutSignal, signal);
    }

    const problem = await refusal(path, response, timeoutSignal, signal);
    if (problem.code === 'antiforgery_validation_failed') {
      await bootstrapAntiforgery({ signal }).catch(() => undefined);
    }
    throw new ApiProblem(problem);
  };

  return {
    bootstrapAntiforgery,
    hasRequestToken: () => requestToken !== null,
    onSessionLost: (listener) => {
      sessionLostListeners.add(listener);
      return () => { sessionLostListeners.delete(listener); };
    },
    send,
  };
}
