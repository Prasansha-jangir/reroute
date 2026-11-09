import { getResponse } from 'msw';
import { handlers } from './handlers';

/**
 * Mock backend without a Service Worker: wraps window.fetch and resolves requests against the MSW handlers.
 * (Service Workers are often disabled in locked-down/VDI browsers; this works everywhere.)
 * Requests with no matching handler fall through to the real network.
 */
export function startMockBackend(): void {
  const realFetch = window.fetch.bind(window);

  window.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const request = new Request(input, init);
    const mocked = await getResponse(handlers, request);
    return mocked ?? realFetch(input, init);
  };

  console.info('%c[Reroute] Mock backend active — no Docker or services needed.', 'color:#0b5cff;font-weight:bold');
}
