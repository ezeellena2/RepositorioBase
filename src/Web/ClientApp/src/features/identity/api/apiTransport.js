import { isProblem, readProblem, readSuccess } from './problemDetails';

const ANTIFORGERY = '/api/identity/antiforgery';

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

  const bootstrapAntiforgery = async () => {
    const response = await fetch(ANTIFORGERY);
    if (!response.ok) throw new Error('Unable to establish the request token.');
    const payload = await readSuccess(response, ['requestToken']);
    requestToken = payload.requestToken;
    return requestToken;
  };

  const send = async (path, { method = 'GET', body, expect = [] } = {}) => {
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

    if (response.ok) return readSuccess(response, expect);
    if (!isProblem(response)) throw new Error(`The API answered ${response.status} without a problem document.`);

    const problem = await readProblem(response);
    if (problem.code === 'antiforgery_validation_failed') await bootstrapAntiforgery();
    throw new ApiProblem(problem);
  };

  return {
    bootstrapAntiforgery,
    hasRequestToken: () => requestToken !== null,
    send,
  };
}
