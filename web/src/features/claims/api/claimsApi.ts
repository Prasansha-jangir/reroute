import { z } from 'zod';
import { http } from '@/shared/api/httpClient';

// Runtime contracts for the Claims API. (Types can also be generated from OpenAPI: `npm run gen:api`.)
export const claimStatusSchema = z.enum([
  'Filed', 'Eligible', 'Ineligible', 'SubmittedToAirline', 'AcceptedByAirline', 'RejectedByAirline', 'Escalated', 'Paid',
]);
export type ClaimStatus = z.infer<typeof claimStatusSchema>;

export const disruptionTypeSchema = z.enum(['Delay', 'Cancellation', 'DeniedBoarding']);
export type DisruptionType = z.infer<typeof disruptionTypeSchema>;

export const claimSummarySchema = z.object({
  claimId: z.string().uuid(),
  flightNumber: z.string(),
  flightDate: z.string(),
  status: claimStatusSchema,
  compensationAmount: z.number(),
  currency: z.string(),
  filedAt: z.string(),
});
export type ClaimSummary = z.infer<typeof claimSummarySchema>;

export const pagedClaimsSchema = z.object({
  items: z.array(claimSummarySchema),
  page: z.number(),
  pageSize: z.number(),
  totalCount: z.number(),
});
export type PagedClaims = z.infer<typeof pagedClaimsSchema>;

export const claimSchema = claimSummarySchema.extend({
  travellerId: z.string(),
  origin: z.string(),
  destination: z.string(),
  disruptionType: disruptionTypeSchema,
  eligibilityReason: z.string(),
  distanceKm: z.number(),
  version: z.number(),
  updatedAt: z.string(),
});
export type Claim = z.infer<typeof claimSchema>;

export const timelineEntrySchema = z.object({
  version: z.number(),
  eventType: z.string(),
  occurredAt: z.string(),
  data: z.string(),
});
export type TimelineEntry = z.infer<typeof timelineEntrySchema>;

export const fileClaimResponseSchema = z.object({
  claimId: z.string().uuid(),
  isEligible: z.boolean(),
  compensationAmount: z.number(),
  currency: z.string(),
  reason: z.string(),
});
export type FileClaimResponse = z.infer<typeof fileClaimResponseSchema>;

export interface FileClaimInput {
  flightNumber: string;
  flightDate: string;
  origin: string;
  destination: string;
  disruptionType: DisruptionType;
  arrivalDelayMinutes: number;
  cancellationNoticeDays?: number | undefined;
  extraordinaryCircumstances?: boolean;
}

export interface ClaimFilters {
  status?: ClaimStatus | undefined;
  page: number;
}

export const claimsApi = {
  list: (filters: ClaimFilters, signal?: AbortSignal) => {
    const params = new URLSearchParams({ page: String(filters.page), pageSize: '20' });
    if (filters.status) params.set('status', filters.status);
    return http(`/api/claims?${params}`, { schema: pagedClaimsSchema, ...(signal ? { signal } : {}) });
  },
  get: (id: string, signal?: AbortSignal) => http(`/api/claims/${id}`, { schema: claimSchema, ...(signal ? { signal } : {}) }),
  timeline: (id: string, signal?: AbortSignal) =>
    http(`/api/claims/${id}/timeline`, { schema: z.array(timelineEntrySchema), ...(signal ? { signal } : {}) }),
  file: (input: FileClaimInput, idempotencyKey: string) =>
    http('/api/claims', { method: 'POST', body: input, schema: fileClaimResponseSchema, idempotencyKey }),
  submit: (id: string, version: number) =>
    http(`/api/claims/${id}/submit`, { method: 'POST', headers: { 'If-Match': `"${version}"` } }),
};
