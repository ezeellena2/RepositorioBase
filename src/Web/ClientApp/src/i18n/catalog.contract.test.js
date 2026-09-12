import { describe, expect, it } from 'vitest';
import languages from './languages.json';
import commonEn from './locales/en/common.json';
import errorsEn from './locales/en/errors.json';
import enumsEn from './locales/en/enums.json';
import identityEn from './locales/en/identity.json';
import platformEn from './locales/en/platform.json';
import commonEs from './locales/es/common.json';
import errorsEs from './locales/es/errors.json';
import enumsEs from './locales/es/enums.json';
import identityEs from './locales/es/identity.json';
import platformEs from './locales/es/platform.json';
import validationErrorSchema from '../../../../Application/Common/Validation/validationErrorSchema.json';
import { VALIDATION_CODE_SCHEMA } from '../features/identity/api/problemDetails';

const namespaces = ['common', 'errors', 'enums', 'identity', 'platform'];
const pluralSuffix = /_(zero|one|two|few|many|other)$/;

const catalogs = {
  en: { common: commonEn, errors: errorsEn, enums: enumsEn, identity: identityEn, platform: platformEn },
  es: { common: commonEs, errors: errorsEs, enums: enumsEs, identity: identityEs, platform: platformEs },
};

function flatten(value, prefix = '', entries = new Map()) {
  for (const [key, child] of Object.entries(value)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (child && typeof child === 'object' && !Array.isArray(child)) flatten(child, path, entries);
    else entries.set(path, child);
  }
  return entries;
}

function placeholders(value) {
  return [...String(value).matchAll(/{{\s*([\w.-]+)/g)].map((match) => match[1]).sort();
}

function richTextTags(value) {
  return [...String(value).matchAll(/<\/?([\w-]+)\s*\/?\s*>/g)].map((match) => match[0]).sort();
}

function semanticKeys(entries) {
  return new Set([...entries.keys()].map((key) => key.replace(pluralSuffix, '')));
}

export function catalogViolations(source, candidate, language) {
  const sourceEntries = flatten(source);
  const candidateEntries = flatten(candidate);
  const violations = [];
  const sourceKeys = semanticKeys(sourceEntries);
  const candidateKeys = semanticKeys(candidateEntries);

  for (const key of sourceKeys) {
    if (!candidateKeys.has(key)) violations.push(`missing key ${key}`);
  }
  for (const key of candidateKeys) {
    if (!sourceKeys.has(key)) violations.push(`unexpected key ${key}`);
  }

  for (const [key, sourceValue] of sourceEntries) {
    const candidateValue = candidateEntries.get(key);
    if (candidateValue === undefined) continue;
    if (typeof candidateValue !== 'string' || candidateValue.trim() === '') {
      violations.push(`empty value ${key}`);
      continue;
    }
    if (JSON.stringify(placeholders(candidateValue)) !== JSON.stringify(placeholders(sourceValue))) {
      violations.push(`placeholder mismatch ${key}`);
    }
    if (JSON.stringify(richTextTags(candidateValue)) !== JSON.stringify(richTextTags(sourceValue))) {
      violations.push(`rich-text tag mismatch ${key}`);
    }
  }

  const requiredPluralForms = new Intl.PluralRules(language).resolvedOptions().pluralCategories;
  for (const key of sourceKeys) {
    const hasPluralSource = [...sourceEntries.keys()].some((sourceKey) => sourceKey.startsWith(`${key}_`) && pluralSuffix.test(sourceKey));
    if (!hasPluralSource) continue;
    for (const category of requiredPluralForms) {
      if (!candidateEntries.has(`${key}_${category}`)) violations.push(`missing plural ${key}_${category}`);
    }
  }

  return violations;
}

describe('language registry contract', () => {
  it('matches the server-owned validation schema and contains every code plus the client fallback', () => {
    const serverCodes = Object.keys(validationErrorSchema).sort();
    expect(Object.keys(VALIDATION_CODE_SCHEMA).sort()).toEqual(serverCodes);

    for (const language of languages.supported) {
      for (const code of [...serverCodes, 'unknown']) {
        expect(catalogs[language].errors.validation[code]).toEqual(expect.any(String));
        expect(catalogs[language].errors.validation[code].trim()).not.toBe('');
        if (code in validationErrorSchema) {
          expect(placeholders(catalogs[language].errors.validation[code]))
            .toEqual([...validationErrorSchema[code]].sort());
        }
      }
    }
  });

  it('declares a source and default that are supported, and no overlap with in-progress languages', () => {
    expect(languages.source).toBe('en');
    expect(languages.default).toBe('en');
    expect(languages.supported).toContain(languages.source);
    expect(languages.supported).toContain(languages.default);
    expect(new Set(languages.supported).size).toBe(languages.supported.length);
    expect(new Set(languages.inProgress).size).toBe(languages.inProgress.length);
    expect(languages.inProgress.some((language) => languages.supported.includes(language))).toBe(false);
  });

  for (const language of languages.supported) {
    for (const namespace of namespaces) {
      it(`${language}/${namespace} matches the English source catalog`, () => {
        expect(catalogViolations(catalogs.en[namespace], catalogs[language][namespace], language)).toEqual([]);
      });
    }
  }

  it('detects missing keys, placeholders, empty values, and required plural forms', () => {
    const source = { message: 'Hello {{name}}', link: '<signIn>Sign in</signIn>', invitations_one: '{{count}} invitation', invitations_other: '{{count}} invitations' };
    const candidate = { message: 'Hello', link: 'Sign in', invitations_other: '' };

    expect(catalogViolations(source, candidate, 'en')).toEqual(expect.arrayContaining([
      'placeholder mismatch message',
      'rich-text tag mismatch link',
      'empty value invitations_other',
      'missing plural invitations_one',
    ]));
  });

  for (const language of languages.inProgress) {
    for (const namespace of namespaces) {
      it(`reports ${language}/${namespace} gaps without making the language selectable`, () => {
        const violations = catalogViolations(catalogs.en[namespace], catalogs[language][namespace], language);
        expect(languages.supported).not.toContain(language);
        if (violations.length > 0) console.info(`In-progress catalog ${language}/${namespace}: ${violations.join(', ')}`);
      });
    }
  }
});
