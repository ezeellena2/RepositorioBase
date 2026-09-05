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
  },
});
