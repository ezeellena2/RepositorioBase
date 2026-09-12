import { defineConfig } from 'i18next-cli';
import languages from './src/i18n/languages.json' with { type: 'json' };
import { staticUnusedNamespaces } from './src/i18n/staticUnusedNamespaces.js';

export { staticUnusedNamespaces };

export default defineConfig({
  locales: [...languages.supported, ...languages.inProgress],
  extract: {
    input: ['src/**/*.{js,jsx}'],
    ignore: ['src/**/*.test.{js,jsx}', 'src/test/**'],
    output: 'src/i18n/locales/{{language}}/{{namespace}}.json',
    primaryLanguage: languages.source,
    secondaryLanguages: [...languages.supported, ...languages.inProgress]
      .filter((language) => language !== languages.source),
    defaultNS: 'common',
    fallbackNS: false,
    keySeparator: '.',
    nsSeparator: ':',
    functions: ['t', '*.t'],
    useTranslationNames: ['useTranslation'],
    transComponents: ['Trans'],
    preservePatterns: ['common:language.*'],
    ignoreNamespaces: ['errors', 'enums'],
    removeUnusedKeys: true,
    extractComments: false,
  },
});
