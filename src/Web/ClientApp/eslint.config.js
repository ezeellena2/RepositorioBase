import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import i18next from 'eslint-plugin-i18next';

// The lint script has been in package.json since before this feature but no flat config ever existed, so the
// command failed for everyone. Task 12's verification requires it to pass, so the configuration is supplied here
// rather than the command being dropped.
export default [
  { ignores: ['build/**', 'dist/**', 'node_modules/**', 'src/web-api-client.ts'] },
  js.configs.recommended,
  {
    files: ['**/*.{js,jsx}'],
    languageOptions: {
      ecmaVersion: 'latest',
      sourceType: 'module',
      globals: { ...globals.browser, ...globals.node },
      parserOptions: { ecmaFeatures: { jsx: true } },
    },
    plugins: { 'react-hooks': reactHooks, 'react-refresh': reactRefresh, i18next },
    rules: {
      ...reactHooks.configs.recommended.rules,
      // A component reached only from JSX looks unused to the base rule, which does not read JSX as a reference.
      // The capitalised exception already covered imports; a table-driven test that takes the component as a
      // parameter needs the same accommodation, so the two patterns say the same thing about the same names.
      'no-unused-vars': ['error', { varsIgnorePattern: '^[A-Z_]', argsIgnorePattern: '^(_|[A-Z])' }],
      'i18next/no-literal-string': ['warn', { mode: 'jsx-only' }],
    },
  },
  {
    files: ['src/**/*.{js,jsx}'],
    ignores: ['src/i18n/**'],
    rules: {
      'no-restricted-imports': ['error', {
        paths: [{
          name: 'react-i18next',
          message: 'Import translation APIs from src/i18n.',
        }],
      }],
    },
  },
  {
    files: ['src/components/**/*.{js,jsx}'],
    rules: {
      'i18next/no-literal-string': ['error', {
        mode: 'jsx-only',
        'jsx-attributes': {
          exclude: [
            'className', 'styleName', 'style', 'type', 'key', 'id', 'width', 'height',
            'component', 'to', 'href', 'sx', 'variant', 'color', 'position', 'edge',
            'maxWidth', 'direction', 'role', 'data-testid', 'aria-labelledby', 'aria-describedby',
            'aria-controls', 'aria-current', 'aria-haspopup', 'aria-live',
          ],
        },
      }],
    },
  },
  {
    files: ['src/features/identity/ProblemMessage.jsx'],
    rules: {
      'i18next/no-literal-string': ['error', {
        mode: 'jsx-only',
        'jsx-attributes': {
          exclude: [
            'className', 'styleName', 'style', 'type', 'key', 'id', 'width', 'height',
            'component', 'to', 'href', 'sx', 'variant', 'color', 'position', 'edge', 'severity',
            'maxWidth', 'direction', 'role', 'data-testid', 'aria-labelledby', 'aria-describedby',
            'aria-controls', 'aria-current', 'aria-haspopup', 'aria-live',
          ],
        },
      }],
    },
  },
  {
    files: ['**/*.test.{js,jsx}', 'src/test/**'],
    languageOptions: { globals: { ...globals.browser, ...globals.node, ...globals.vitest } },
  },
];
