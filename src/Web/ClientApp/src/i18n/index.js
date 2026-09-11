import i18n from 'i18next';
import {
  initReactI18next,
  Trans,
  useTranslation as reactUseTranslation,
} from 'react-i18next';

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
import languages from './languages.json';

const cultureCookieName = '.AspNetCore.Culture';
const namespaceNames = ['common', 'errors', 'enums', 'identity', 'platform'];

const sourceLanguage = languages.source;
const defaultLanguage = languages.default ?? sourceLanguage;
const supportedLanguages = languages.supported ?? [];

const resources = {
  en: {
    common: commonEn,
    errors: errorsEn,
    enums: enumsEn,
    identity: identityEn,
    platform: platformEn,
  },
  es: {
    common: commonEs,
    errors: errorsEs,
    enums: enumsEs,
    identity: identityEs,
    platform: platformEs,
  },
};

const isSupported = (candidate) => {
  if (!candidate) return false;
  return supportedLanguages.includes(candidate);
};

const normalizeLanguage = (value) => {
  if (!value) return null;

  const normalized = value.trim().toLowerCase();
  if (isSupported(normalized)) return normalized;

  const dash = normalized.indexOf('-');
  if (dash >= 0) {
    const base = normalized.substring(0, dash);
    if (isSupported(base)) return base;
  }

  return null;
};

const readCultureCookie = (cookie = '') => {
  const raw = cookie
    .split(';')
    .map((segment) => segment.trim())
    .find((segment) => segment.startsWith(`${cultureCookieName}=`));

  if (!raw) return null;

  const value = raw.substring(cultureCookieName.length + 1);
  const cultureMatch = /(?:^|\|)c=([^|;]+)/.exec(value);
  return cultureMatch?.[1] ? normalizeLanguage(cultureMatch[1]) : null;
};

const browserLanguageCandidates = () => {
  if (typeof navigator === 'undefined') return [];

  const candidates = new Set();
  const candidatesFromBrowser = [...(navigator.languages ?? [])];
  if (navigator.language) candidatesFromBrowser.push(navigator.language);

  for (const candidate of candidatesFromBrowser) {
    const normalized = normalizeLanguage(candidate);
    if (normalized) {
      candidates.add(normalized);
    }
  }

  return [...candidates];
};

const readLanguageFromBrowser = () => {
  for (const candidate of browserLanguageCandidates()) {
    if (isSupported(candidate)) return candidate;
  }

  return null;
};

const readLanguageFromCookie = () => {
  if (typeof document === 'undefined') return null;
  return readCultureCookie(document.cookie);
};

const resolveLanguage = (preferredLanguage = null) => {
  const fromPreference = normalizeLanguage(preferredLanguage);
  if (fromPreference) return fromPreference;

  const fromCookie = readLanguageFromCookie();
  if (fromCookie) return fromCookie;

  const fromBrowser = readLanguageFromBrowser();
  if (fromBrowser) return fromBrowser;

  return isSupported(defaultLanguage) ? defaultLanguage : sourceLanguage;
};

const isTest = import.meta.env?.MODE === 'test';
const isProduction = import.meta.env?.PROD === true;

const handleMissing = (key) => {
  if (isTest) {
    throw new Error(`i18n key missing: ${key}`);
  }

  if (!isProduction) {
    console.error('Missing i18n key:', key);
  }

  return key;
};

export const writeLanguageCookie = (language) => {
  if (typeof document === 'undefined') return;

  const normalized = normalizeLanguage(language);
  if (!normalized) return;

  document.cookie = `${cultureCookieName}=c=${normalized}|uic=${normalized}; path=/; samesite=lax`;
};

export const setLanguage = (language) => {
  const normalized = normalizeLanguage(language);
  if (!normalized) return;

  i18n.changeLanguage(normalized);
  writeLanguageCookie(normalized);
  if (typeof document !== 'undefined' && document.documentElement) {
    document.documentElement.lang = normalized;
  }
};

const initialLanguage = resolveLanguage();

i18n
  .use(initReactI18next)
  .init({
    resources,
    ns: namespaceNames,
    defaultNS: 'common',
    supportedLngs: supportedLanguages,
    fallbackLng: defaultLanguage,
    lng: initialLanguage,
    fallbackNS: false,
    load: 'languageOnly',
    saveMissing: true,
    parseMissingKeyHandler: handleMissing,
    missingKeyHandler: (_language, _namespace, key) => handleMissing(key),
    returnObjects: false,
    returnEmptyString: false,
    returnNull: false,
    interpolation: { escapeValue: false },
    initImmediate: false,
  });

if (typeof document !== 'undefined' && document.documentElement) {
  document.documentElement.lang = i18n.resolvedLanguage ?? initialLanguage;
}

export { i18n };
export const t = i18n.t.bind(i18n);
export { reactUseTranslation as useTranslation, Trans };
