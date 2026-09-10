import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// The test environment is configured apart from vite.config.ts because that file reads PORT from the Aspire
// process environment, which no test has. Sharing it would make the suite depend on how the app was launched.
export default defineConfig({
  plugins: [react()],
  // The application's own files use the automatic JSX runtime, as tsconfig declares. Saying so here keeps the
  // transform the tests run through identical to the one the build uses.
  esbuild: { jsx: 'automatic' },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.js'],
    css: false,
    // Vitest's default is five seconds, which the pagination fixtures no longer fit inside. They render a
    // hundred-and-one member and role rows, and a row built from Material UI components costs several styled
    // components where it used to cost a bare element — `ExternalProofResume` went from ten seconds to eighteen
    // for the whole file. The work still finishes; measured on its own that test takes under two seconds, and
    // it only overran because the suite runs its files in parallel and starves each one of CPU. This raises the
    // budget rather than the speed: no assertion, fixture or page object changes, and a test that genuinely
    // hangs still fails.
    testTimeout: 15000,
  },
});
