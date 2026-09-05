/**
 * Reads what the API actually answers with. It models the external RFC 9457 shape and the declared endpoint DTOs
 * and nothing else: the server's internal Result type never crosses the wire, and a client that learned to read
 * one would keep working while the boundary it exists to police drifted away underneath it (IA-REQ-038).
 */
export const PROBLEM_MEDIA_TYPE = 'application/problem+json';

/** Shapes the API promised never to send. Seeing one is drift, and drift is reported rather than absorbed. */
const FORBIDDEN_SUCCESS_KEYS = ['succeeded', 'success', 'data', 'error', 'errors', 'value', 'items', 'nextCursor'];

const mediaTypeOf = (response) => (response.headers.get('Content-Type') ?? '').split(';')[0].trim().toLowerCase();

export function isProblem(response) {
  return mediaTypeOf(response) === PROBLEM_MEDIA_TYPE;
}

/**
 * A failure is only a failure this client understands if it arrived as problem+json with a stable code. Anything
 * else — a proxy's HTML error page, a bare JSON body, a problem without a code — is drift, and guessing at it
 * would show the user a message the server never authored.
 */
export async function readProblem(response) {
  if (!isProblem(response)) {
    throw new Error(`Expected ${PROBLEM_MEDIA_TYPE} but the response was ${mediaTypeOf(response) || 'untyped'}.`);
  }

  let body;
  try {
    body = await response.json();
  } catch {
    throw new Error('The problem response was not valid JSON.');
  }

  if (typeof body?.code !== 'string' || body.code.length === 0) {
    throw new Error('A problem response must carry a stable code.');
  }

  const retryAfter = Number.parseInt(response.headers.get('Retry-After') ?? '', 10);
  return {
    status: response.status,
    code: body.code,
    traceId: typeof body.traceId === 'string' ? body.traceId : undefined,
    // Field errors exist only where the API indexes them, and a server fault carries no detail worth repeating:
    // whatever diagnostics it holds are the server's business, not the user's.
    errors: body.errors && typeof body.errors === 'object' ? body.errors : undefined,
    detail: response.status >= 500 ? undefined : (typeof body.detail === 'string' ? body.detail : undefined),
    retryAfterSeconds: Number.isFinite(retryAfter) ? retryAfter : undefined,
  };
}

/**
 * Reads a declared success. The expected members are named by the caller because they are the endpoint's
 * contract; a body that carries none of them, or that is shaped like an internal Result or a universal envelope,
 * is refused rather than half-read.
 */
export async function readSuccess(response, expectedMembers) {
  if (response.status === 204 || response.headers.get('Content-Length') === '0') return null;

  const raw = await response.text();
  if (raw.length === 0) return null;

  let body;
  try {
    body = JSON.parse(raw);
  } catch {
    throw new Error('The success response was not valid JSON.');
  }

  if (body === null || typeof body !== 'object' || Array.isArray(body)) {
    throw new Error('An identity endpoint answers with an object.');
  }

  const declared = new Set(expectedMembers);
  const smuggled = FORBIDDEN_SUCCESS_KEYS.filter((key) => key in body && !declared.has(key));
  if (smuggled.length > 0) {
    throw new Error(`The response carried ${smuggled.join(', ')}, which the identity contract does not use.`);
  }

  const missing = expectedMembers.filter((member) => !(member in body));
  if (missing.length > 0) {
    throw new Error(`The response is missing ${missing.join(', ')}.`);
  }

  return body;
}
