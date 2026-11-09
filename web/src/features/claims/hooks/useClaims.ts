import { useMutation, useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { useRef } from 'react';
import { newIdempotencyKey } from '@/shared/api/httpClient';
import { claimsApi, type Claim, type ClaimFilters, type FileClaimInput } from '../api/claimsApi';
import { claimKeys } from '../api/queryKeys';

export const useMyClaims = (filters: ClaimFilters) =>
  useQuery({
    queryKey: claimKeys.list(filters),
    queryFn: ({ signal }) => claimsApi.list(filters, signal),
    placeholderData: keepPreviousData, // no flicker when paging
  });

export const useClaim = (id: string) =>
  useQuery({ queryKey: claimKeys.detail(id), queryFn: ({ signal }) => claimsApi.get(id, signal) });

export const useClaimTimeline = (id: string) =>
  useQuery({ queryKey: claimKeys.timeline(id), queryFn: ({ signal }) => claimsApi.timeline(id, signal) });

/** One idempotency key per form "session": double-clicks and retries can never create two claims. */
export function useFileClaim() {
  const queryClient = useQueryClient();
  const keyRef = useRef(newIdempotencyKey());

  return useMutation({
    mutationFn: (input: FileClaimInput) => claimsApi.file(input, keyRef.current),
    onSuccess: () => {
      keyRef.current = newIdempotencyKey(); // next submission is a new logical request
      void queryClient.invalidateQueries({ queryKey: claimKeys.lists() });
    },
  });
}

/** Optimistic update with rollback; If-Match carries the stream version for server-side concurrency control. */
export function useSubmitClaim(claim: Claim) {
  const queryClient = useQueryClient();
  const key = claimKeys.detail(claim.claimId);

  return useMutation({
    mutationFn: () => claimsApi.submit(claim.claimId, claim.version),
    onMutate: async () => {
      await queryClient.cancelQueries({ queryKey: key });
      const previous = queryClient.getQueryData<Claim>(key);
      queryClient.setQueryData<Claim>(key, (c) => (c ? { ...c, status: 'SubmittedToAirline' } : c));
      return { previous };
    },
    onError: (_error, _vars, context) => {
      if (context?.previous) queryClient.setQueryData(key, context.previous);
    },
    onSettled: () => void queryClient.invalidateQueries({ queryKey: claimKeys.all }),
  });
}
