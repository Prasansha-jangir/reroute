import { z } from 'zod';

/** RFC 9457 problem details, as produced by every Reroute service. */
export const problemDetailsSchema = z.object({
  type: z.string().optional(),
  title: z.string().optional(),
  status: z.number().optional(),
  detail: z.string().optional(),
  traceId: z.string().optional(),
  code: z.string().optional(),
  errors: z.record(z.string(), z.array(z.string())).optional(),
});

export type ProblemDetails = z.infer<typeof problemDetailsSchema>;

export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | undefined;

  constructor(status: number, problem?: ProblemDetails) {
    super(problem?.title ?? `Request failed with status ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  /** Field-level validation messages keyed by camelCase field name (FluentValidation → problem+json). */
  get fieldErrors(): Record<string, string[]> {
    const errors = this.problem?.errors ?? {};
    return Object.fromEntries(
      Object.entries(errors).map(([key, value]) => [key.charAt(0).toLowerCase() + key.slice(1), value]),
    );
  }

  get isRetryable(): boolean {
    return this.status >= 500 || this.status === 429 || this.status === 0;
  }
}

/** The backend changed shape without the frontend knowing — fail loudly, close to the cause. */
export class ContractError extends Error {
  constructor(url: string, cause: z.ZodError) {
    super(`Response from ${url} did not match the expected contract: ${cause.message}`);
    this.name = 'ContractError';
  }
}
