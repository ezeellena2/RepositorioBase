import { describe, expect, it } from 'vitest';
import { safeReturnUrl } from './LoginPage';

const ORIGIN = 'https://app.test';

/** Where the value would actually take the browser, which is the only question that matters about it. */
const resolves = (candidate) => new URL(safeReturnUrl(candidate), `${ORIGIN}/login`).origin;

/**
 * The return URL comes from the query string, so it is attacker-chosen. What has to hold is not a spelling rule
 * but a destination rule: whatever survives must resolve to this origin.
 */
describe('safeReturnUrl', () => {
  it.each([
    ['/identity', '/identity'],
    ['/identity/sessions?tab=1', '/identity/sessions?tab=1'],
  ])('keeps the same-origin path %s', (candidate, expected) => {
    expect(safeReturnUrl(candidate)).toBe(expected);
  });

  it.each([
    ['//evil.test', 'protocol-relative'],
    ['/\\evil.test', 'a backslash, which URL parsing treats as a second slash'],
    ['\\\\evil.test', 'two backslashes'],
    ['/\\/evil.test', 'a backslash then a slash'],
    ['https://evil.test/steal', 'an absolute URL'],
    ['http:/evil.test', 'a scheme with one slash'],
    ['javascript:alert(1)', 'a script scheme'],
    ['', 'nothing at all'],
    [null, 'no value'],
  ])('discards %s (%s)', (candidate) => {
    expect(safeReturnUrl(candidate)).toBe('/');
  });

  it.each([
    '//evil.test',
    '/\\evil.test',
    '\\\\evil.test',
    '/\\/evil.test',
    'https://evil.test/steal',
  ])('never lets %s resolve off this origin', (candidate) => {
    expect(resolves(candidate)).toBe(ORIGIN);
  });
});
