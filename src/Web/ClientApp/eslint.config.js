import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';

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
    plugins: { 'react-hooks': reactHooks, 'react-refresh': reactRefresh },
    rules: {
      ...reactHooks.configs.recommended.rules,
      // A component reached only from JSX looks unused to the base rule, which does not read JSX as a reference.
      // The capitalised exception already covered imports; a table-driven test that takes the component as a
      // parameter needs the same accommodation, so the two patterns say the same thing about the same names.
      'no-unused-vars': ['error', { varsIgnorePattern: '^[A-Z_]', argsIgnorePattern: '^(_|[A-Z])' }],
    },
  },
  {
    files: ['**/*.test.{js,jsx}', 'src/test/**'],
    languageOptions: { globals: { ...globals.browser, ...globals.node, ...globals.vitest } },
  },
];
