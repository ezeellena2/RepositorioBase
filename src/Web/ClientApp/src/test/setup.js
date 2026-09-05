import '@testing-library/jest-dom/vitest';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { cleanup } from '@testing-library/react';
import { server } from './server';

// This Node build starts jsdom without a storage backend, so window.localStorage is undefined and any component
// that reads a saved preference throws on render. The identity feature stores nothing (IA-REQ-025); this exists
// only so the surrounding shell — the theme toggle — can render inside a test.
if (!('localStorage' in window) || window.localStorage === undefined) {
  const entries = new Map();
  Object.defineProperty(window, 'localStorage', {
    configurable: true,
    value: {
      getItem: (key) => (entries.has(key) ? entries.get(key) : null),
      setItem: (key, value) => entries.set(key, String(value)),
      removeItem: (key) => entries.delete(key),
      clear: () => entries.clear(),
    },
  });
}

// An unhandled request is an error, not a pass-through. A test that reaches an endpoint it did not describe is
// asserting against the real network, which is neither deterministic nor safe.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));

afterEach(() => {
  cleanup();
  server.resetHandlers();
  window.history.replaceState({}, '', '/');
});

afterAll(() => server.close());
