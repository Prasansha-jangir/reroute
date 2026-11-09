import { QueryClient } from '@tanstack/react-query';
import { ApiError } from './errors';

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000, // real-time updates arrive via SignalR, so polling-style refetching is unnecessary
        gcTime: 5 * 60_000,
        refetchOnWindowFocus: true,
        // Retry only what can succeed on retry: never 4xx (validation, not found, forbidden).
        retry: (failureCount, error) => failureCount < 3 && (!(error instanceof ApiError) || error.isRetryable),
        retryDelay: (attempt) => Math.min(1000 * 2 ** attempt, 10_000) + Math.random() * 250, // backoff + jitter
      },
      mutations: { retry: false },
    },
  });
}
