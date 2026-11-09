import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { server } from './server';

// Mock at the network boundary (MSW): tests exercise the real http client, Zod parsing and React Query.
beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' });

  // jsdom's AbortSignal is not an instance of Node's (undici) AbortSignal, which fetch/Request reject.
  // Cancellation isn't under test here, so drop the signal in the test environment only.
  const patchedFetch = globalThis.fetch;
  globalThis.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
    if (init?.signal) {
      const rest: RequestInit = { ...init };
      delete rest.signal;
      return patchedFetch(input, rest);
    }
    return patchedFetch(input, init);
  };
});
afterEach(() => {
  server.resetHandlers();
  cleanup();
});
afterAll(() => server.close());
