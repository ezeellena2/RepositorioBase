import { describe, expect, it } from 'vitest';
import { PROBLEM_MEDIA_TYPE, isProblem, readProblem, readSuccess } from './problemDetails';

const problem = (status, body, headers = {}) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': PROBLEM_MEDIA_TYPE, ...headers },
  });

const json = (status, body) =>
  new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
  });

/**
 * The parser reads the external contract of IA-REQ-038 and only that. Everything it accepts is something the API
 * documents; everything it refuses is something the API promised never to send, which is what makes it a
 * boundary rather than a convenience.
 */
describe('problem details', () => {
  it.each([400, 401, 403, 404, 409, 429, 500])('reads the stable code and opaque trace of a %d', async (status) => {
    const parsed = await readProblem(problem(status, { code: 'invalid_invitation', traceId: 'abc123', title: 'Bad Request' }));

    expect(parsed.status).toBe(status);
    expect(parsed.code).toBe('invalid_invitation');
    expect(parsed.traceId).toBe('abc123');
  });

  it('exposes field errors only when the API sent them', async () => {
    const validation = await readProblem(problem(400, { code: 'validation_failed', traceId: 't', errors: { email: ['Required'] } }));
    const conflict = await readProblem(problem(409, { code: 'invitation_conflict', traceId: 't' }));

    expect(validation.errors).toEqual({ email: ['Required'] });
    expect(conflict.errors).toBeUndefined();
  });

  it('surfaces Retry-After on a rate limit so the caller can wait rather than hammer', async () => {
    const parsed = await readProblem(problem(429, { code: 'rate_limit_exceeded', traceId: 't' }, { 'Retry-After': '30' }));

    expect(parsed.retryAfterSeconds).toBe(30);
  });

  /**
   * A 500 is the one answer a client must not repeat to the user. Whatever the server did put in it, the parser
   * hands back a safe code and nothing that could carry a stack, a query or a token.
   */
  it('keeps a server fault opaque', async () => {
    const parsed = await readProblem(problem(500, { code: 'internal_server_error', traceId: 't', detail: 'at Npgsql.Something' }));

    expect(parsed.code).toBe('internal_server_error');
    expect(parsed.detail).toBeUndefined();
  });

  it.each([
    ['a body that is not JSON at all', new Response('<html>gateway</html>', { status: 502, headers: { 'Content-Type': 'text/html' } })],
    ['a JSON body without the media type', json(400, { code: 'invalid_invitation' })],
    ['a problem media type without a code', problem(400, { traceId: 't' })],
  ])('refuses %s', async (_label, response) => {
    await expect(readProblem(response)).rejects.toThrow();
  });

  it('recognises a problem by media type rather than by status alone', () => {
    expect(isProblem(problem(400, { code: 'x', traceId: 't' }))).toBe(true);
    expect(isProblem(json(200, { invitationId: 'x' }))).toBe(false);
  });

  /**
   * The server's internal Result never crosses the wire, so a body shaped like one is drift and the client says
   * so rather than quietly learning to read it (IA-REQ-038).
   */
  it.each([
    ['an internal Result', { succeeded: true, value: { invitationId: 'x' } }],
    ['a universal envelope', { success: true, data: { invitationId: 'x' }, error: null }],
    ['a pagination wrapper', { items: [], nextCursor: null }],
  ])('refuses %s on an identity endpoint', async (_label, body) => {
    await expect(readSuccess(json(200, body), ['invitationId'])).rejects.toThrow();
  });

  it('reads a declared endpoint DTO', async () => {
    const dto = await readSuccess(json(200, { tenantId: 't', membershipId: 'm' }), ['tenantId', 'membershipId']);

    expect(dto).toEqual({ tenantId: 't', membershipId: 'm' });
  });

  it('accepts a bodyless success without inventing a body', async () => {
    await expect(readSuccess(json(204), [])).resolves.toBeNull();
  });
});
