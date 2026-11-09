import type { z } from 'zod';
import { ApiError, ContractError, problemDetailsSchema } from './errors';

type HttpMethod = 'GET' | 'POST' | 'PUT' | 'DELETE';

interface RequestOptions<T> {
  method?: HttpMethod;
  body?: unknown;
  /** Validates the response at runtime; the return type is inferred from it. */
  schema?: z.ZodType<T>;
  /** Re-use the same key across retries of ONE logical submission (see newIdempotencyKey). */
  idempotencyKey?: string;
  headers?: Record<string, string>;
  signal?: AbortSignal;
}

/**
 * The single place the SPA talks HTTP.
 * - Same-origin cookie session via the BFF (credentials: 'include'), never a bearer token in JS
 * - X-CSRF header required by the gateway on state-changing requests
 * - problem+json → typed ApiError; Zod-validated responses → ContractError on drift
 * - 401 → redirect to the BFF login, preserving where the user was
 */
export async function http<T = void>(url: string, options: RequestOptions<T> = {}): Promise<T> {
  const { method = 'GET', body, schema, idempotencyKey, headers = {}, signal } = options;

  let response: Response;
  try {
    response = await fetch(new URL(url, window.location.origin), {
      method,
      credentials: 'include',
      headers: {
        Accept: 'application/json',
        'X-CSRF': '1',
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : {}),
        ...headers,
      },
      ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
      ...(signal ? { signal } : {}),
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new ApiError(0, { title: 'Network error — check your connection.' });
  }

  if (response.status === 401) {
    redirectToLogin();
    throw new ApiError(401, { title: 'Your session has expired.' });
  }

  if (!response.ok) {
    const parsed = problemDetailsSchema.safeParse(await response.json().catch(() => ({})));
    throw new ApiError(response.status, parsed.success ? parsed.data : undefined);
  }

  if (response.status === 204 || !schema) return undefined as T;

  const json: unknown = await response.json();
  const result = schema.safeParse(json);
  if (!result.success) throw new ContractError(url, result.error);
  return result.data;
}

export function redirectToLogin(): void {
  const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
  window.location.assign(`/bff/login?returnUrl=${returnUrl}`);
}

/** UUID per logical submission: retries/double-clicks reuse it, so the server creates at most one resource. */
export const newIdempotencyKey = (): string => crypto.randomUUID();
