import { setupServer } from 'msw/node';

/**
 * The request boundary every test shares. Handlers are added per test rather than declared here: a default that
 * answers everything hides the case a test forgot to describe, and an unhandled request should fail loudly.
 */
export const server = setupServer();
