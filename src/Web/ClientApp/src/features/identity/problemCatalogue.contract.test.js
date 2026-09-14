import { describe, expect, it } from 'vitest';
import { problem } from '../../test/identityServer';
import languages from '../../i18n/languages.json';
import enErrors from '../../i18n/locales/en/errors.json';
import esErrors from '../../i18n/locales/es/errors.json';
import problemCodes from '../../api/problemCodes.json';

const CLIENT_CODES = [
  'client_failure',
  'network_unavailable',
  'request_timeout',
  'unreadable_response',
];
const PRESENTATION_KEYS = new Set(['reference', 'retryAfter', 'unknown', 'validation']);
const VOCABULARY = [...Object.keys(problemCodes), ...CLIENT_CODES].sort();
const errorCatalogues = import.meta.glob('../../i18n/locales/*/errors.json', { eager: true, import: 'default' });
const errorsFor = (language) => errorCatalogues[`../../i18n/locales/${language}/errors.json`];

const messageKeys = (catalogue) => Object.keys(catalogue)
  .filter((key) => !PRESENTATION_KEYS.has(key))
  .sort();

const keyTree = (value, prefix = '') => Object.entries(value).flatMap(([key, child]) => {
  const path = prefix ? `${prefix}.${key}` : key;
  return child && typeof child === 'object' && !Array.isArray(child)
    ? keyTree(child, path)
    : [path];
}).sort();

describe('problem catalogue contract', () => {
  it('keeps transport-only codes out of the API catalogue', () => {
    expect(problemCodes.recovery_admission_closed).toBe(503);
    expect(CLIENT_CODES.filter((code) => code in problemCodes)).toEqual([]);
  });

  it('reads every supported language, the source included, from the registry for the words gate', () => {
    expect(languages.supported).toContain(languages.source);
    expect(languages.supported.filter((language) => errorsFor(language) === undefined)).toEqual([]);
  });

  it.each(languages.supported)('covers the complete API and client vocabulary in %s without orphaned message keys', (language) => {
    const errors = errorsFor(language);
    expect(messageKeys(errors)).toEqual(VOCABULARY);
    expect(VOCABULARY.every((code) => (
      typeof errors[code] === 'string' && errors[code].trim().length > 0
    ))).toBe(true);
  });

  it('keeps the localized error catalogues structurally identical', () => {
    expect(keyTree(esErrors)).toEqual(keyTree(enErrors));
    for (const key of PRESENTATION_KEYS) expect(enErrors[key]).toBeDefined();
  });

  it('allows MSW fixtures only for status and code pairs declared by the API', () => {
    expect(() => problem(400, 'validation_failed')).not.toThrow();
    expect(() => problem(409, 'validation_failed')).toThrow(
      'Problem fixture 409 validation_failed is not declared in problemCodes.json.',
    );
    expect(() => problem(400, 'not_a_problem_code')).toThrow(
      'Problem fixture 400 not_a_problem_code is not declared in problemCodes.json.',
    );
  });
});
