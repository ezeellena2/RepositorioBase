import { describe, expect, it } from 'vitest';
import {
  createPseudoResources,
  PSEUDO_LANGUAGE,
  pseudoLocalizeText,
  resolvePseudoLanguage,
} from './pseudoLocale';

describe('development pseudo-language', () => {
  it('requires development mode and the exact lng=en-XA query value', () => {
    expect(resolvePseudoLanguage({ isDevelopment: true, search: '?lng=en-XA' })).toBe(PSEUDO_LANGUAGE);
    expect(resolvePseudoLanguage({ isDevelopment: true, search: '?next=%2F&lng=en-XA' })).toBe(PSEUDO_LANGUAGE);
    expect(resolvePseudoLanguage({ isDevelopment: true, search: '?lng=en-xa' })).toBeNull();
    expect(resolvePseudoLanguage({ isDevelopment: true, search: '?lng=en-XA-extra' })).toBeNull();
    expect(resolvePseudoLanguage({ isDevelopment: false, search: '?lng=en-XA' })).toBeNull();
  });

  it('accents and expands visible text while preserving tokens, tags, whitespace, and source resources', () => {
    const source = {
      message: '  Welcome <strong>{{name}}</strong> to your account.  ',
      nested: ['Sign in', { count: '{{count}} invitations' }],
    };
    const before = structuredClone(source);
    const pseudo = createPseudoResources(source);

    expect(source).toEqual(before);
    expect(pseudo).not.toBe(source);
    expect(pseudo.message).toContain('<strong>{{name}}</strong>');
    expect(pseudo.message).toMatch(/^ {2}/);
    expect(pseudo.message).toMatch(/ {2}$/);
    expect(pseudo.message).not.toContain('Welcome');
    expect(pseudo.nested[1].count).toContain('{{count}}');

    const literal = 'Localization reveals cramped layouts';
    const transformed = pseudoLocalizeText(literal);
    expect(transformed.length).toBeGreaterThanOrEqual(Math.ceil(literal.length * 1.35));
    expect(transformed).not.toMatch(/[A-Za-z]{5}/);
  });
});
