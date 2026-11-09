import type { ClaimFilters } from './claimsApi';

/** Query-key factory: one source of truth → precise invalidation, no typo-driven cache bugs. */
export const claimKeys = {
  all: ['claims'] as const,
  lists: () => [...claimKeys.all, 'list'] as const,
  list: (filters: ClaimFilters) => [...claimKeys.lists(), filters] as const,
  details: () => [...claimKeys.all, 'detail'] as const,
  detail: (id: string) => [...claimKeys.details(), id] as const,
  timeline: (id: string) => [...claimKeys.detail(id), 'timeline'] as const,
};
