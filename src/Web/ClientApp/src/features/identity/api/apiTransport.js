import { isProblem, readProblem, readSuccess } from './problemDetails';

const ANTIFORGERY = '/api/identity/antiforgery';
const CONTEXT = '/api/identity/context';
const SESSION_LOST = new Set(['authentication_required', 'invalid_session']);

export class ApiProblem extends Error {
  constructor(problem) {
    super(problem.code);
    this.name = 'ApiProblem';
    this.problem = problem;
  }
}

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

  const bootstrapAntiforgery = async () => {
    const response = await fetch(ANTIFORGERY);
    if (!response.ok) throw new Error('Unable to establish the request token.');
    const payload = await readSuccess(response, ['requestToken']);
    requestToken = payload.requestToken;
    return requestToken;
  };

  const send = async (path, { method = 'GET', body, expect = [], expectArray = false } = {}) => {
    const mutation = method !== 'GET';
    if (mutation && requestToken === null) await bootstrapAntiforgery();

    const response = await fetch(path, {
      method,
      headers: {
        Accept: 'application/json, application/problem+json',
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(mutation ? { 'X-CSRF-TOKEN': requestToken } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });

    if (response.ok) return readSuccess(response, expect, { asArray: expectArray });
    if (!isProblem(response)) throw new Error(`The API answered ${response.status} without a problem document.`);

    const problem = await readProblem(response);
    const sessionWasLost = response.status === 401 && SESSION_LOST.has(problem.code);
    if (sessionWasLost) requestToken = null;
    if (problem.code === 'antiforgery_validation_failed') await bootstrapAntiforgery();
    if (sessionWasLost && path !== CONTEXT) {
      sessionLostListeners.forEach((listener) => listener(problem));
    }
    throw new ApiProblem(problem);
  };

  return {
    bootstrapAntiforgery,
    hasRequestToken: () => requestToken !== null,
    onSessionLost: (listener) => {
      sessionLostListeners.add(listener);
      return () => sessionLostListeners.delete(listener);
    },
    send,
  };
}
