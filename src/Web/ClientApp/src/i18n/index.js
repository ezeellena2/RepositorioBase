import i18n from 'i18next';
import {
  initReactI18next,
  Trans,
  useTranslation as reactUseTranslation,
} from 'react-i18next';

import languages from './languages.json';
import { createPseudoResources, PSEUDO_LANGUAGE, resolvePseudoLanguage } from './pseudoLocale';
export { PSEUDO_LANGUAGE } from './pseudoLocale';

const cultureCookieName = '.AspNetCore.Culture';
export const namespaceNames = Object.freeze(['common', 'errors', 'enums', 'identity', 'platform']);

export const sourceLanguage = languages.source;
const defaultLanguage = languages.default ?? sourceLanguage;
export const supportedLanguages = languages.supported ?? [];

export function assembleCatalogResources(modules, registry = languages, namespaces = namespaceNames) {
  const registeredLanguages = new Set([...(registry.supported ?? []), ...(registry.inProgress ?? [])]);
  const registeredNamespaces = new Set(namespaces);
  const assembled = {};

  for (const [rawPath, catalog] of Object.entries(modules)) {
    const path = rawPath.replaceAll('\\', '/');
    const match = /^\.\/locales\/([^/]+)\/([^/]+)\.json$/.exec(path);
    if (!match) throw new Error(`Unexpected locale catalog path: ${rawPath}`);

    const [, language, namespace] = match;
    if (!registeredLanguages.has(language)) {
      throw new Error(`Locale catalog language '${language}' is not registered.`);
    }
    if (!registeredNamespaces.has(namespace)) {
      throw new Error(`Locale catalog namespace '${namespace}' is not registered.`);
    }
    assembled[language] ??= {};
    if (assembled[language][namespace] !== undefined) {
      throw new Error(`Duplicate locale catalog: ${language}/${namespace}`);
    }
    assembled[language][namespace] = catalog;
  }

  for (const language of registry.supported ?? []) {
    for (const namespace of namespaces) {
      if (assembled[language]?.[namespace] === undefined) {
        throw new Error(`Supported locale catalog is missing: ${language}/${namespace}`);
      }
    }
  }

  return assembled;
}

const catalogModules = import.meta.glob('./locales/*/*.json', { eager: true, import: 'default' });
const catalogResources = assembleCatalogResources(catalogModules);

const currentPseudoLanguage = () => resolvePseudoLanguage({
  isDevelopment: import.meta.env?.DEV === true,
  search: typeof window === 'undefined' ? '' : window.location.search,
});

export const isPseudoLanguageOverrideActive = () => currentPseudoLanguage() === PSEUDO_LANGUAGE;

const initialPseudoLanguage = currentPseudoLanguage();
const resources = initialPseudoLanguage === null
  ? catalogResources
  : { ...catalogResources, [PSEUDO_LANGUAGE]: createPseudoResources(catalogResources[sourceLanguage]) };
const runtimeSupportedLanguages = initialPseudoLanguage === null
  ? supportedLanguages
  : [...supportedLanguages, PSEUDO_LANGUAGE];

export const isSupportedLanguage = (candidate) => {
  if (!candidate) return false;
  return supportedLanguages.includes(candidate);
};

const normalizeLanguage = (value) => {
  if (!value) return null;

  const normalized = value.trim().toLowerCase();
  if (isSupportedLanguage(normalized)) return normalized;

  const dash = normalized.indexOf('-');
  if (dash >= 0) {
    const base = normalized.substring(0, dash);
    if (isSupportedLanguage(base)) return base;
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
    if (isSupportedLanguage(candidate)) return candidate;
  }

  return null;
};

const readLanguageFromCookie = () => {
  if (typeof document === 'undefined') return null;
  return readCultureCookie(document.cookie);
};

export const resolveLanguage = (preferredLanguage = null) => {
  const fromPreference = normalizeLanguage(preferredLanguage);
  if (fromPreference) return fromPreference;

  const fromCookie = readLanguageFromCookie();
  if (fromCookie) return fromCookie;

  const fromBrowser = readLanguageFromBrowser();
  if (fromBrowser) return fromBrowser;

  return isSupportedLanguage(defaultLanguage) ? defaultLanguage : sourceLanguage;
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
  if (isPseudoLanguageOverrideActive()) {
    if (!i18n.hasResourceBundle(PSEUDO_LANGUAGE, namespaceNames[0])) {
      for (const namespace of namespaceNames) {
        i18n.addResourceBundle(
          PSEUDO_LANGUAGE,
          namespace,
          createPseudoResources(catalogResources[sourceLanguage][namespace]),
          true,
          true,
        );
      }
      i18n.options.supportedLngs = [...supportedLanguages, PSEUDO_LANGUAGE];
    }
    i18n.changeLanguage(PSEUDO_LANGUAGE);
    if (typeof document !== 'undefined' && document.documentElement) {
      document.documentElement.lang = PSEUDO_LANGUAGE;
    }
    return;
  }

  const normalized = normalizeLanguage(language);
  if (!normalized) return;

  i18n.changeLanguage(normalized);
  writeLanguageCookie(normalized);
  if (typeof document !== 'undefined' && document.documentElement) {
    document.documentElement.lang = normalized;
  }
};

const initialLanguage = initialPseudoLanguage ?? resolveLanguage();

i18n
  .use(initReactI18next)
  .init({
    resources,
    ns: namespaceNames,
    defaultNS: 'common',
    supportedLngs: runtimeSupportedLanguages,
    fallbackLng: defaultLanguage,
    lng: initialLanguage,
    fallbackNS: false,
    load: 'currentOnly',
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
export { useFormat } from './useFormat';

/** Only server-designated system roles have catalog names; custom names are user content. */
export const roleName = (role, translate) => role.isSystem
  ? translate(`enums:roles.system.${role.name}`)
  : role.name;
