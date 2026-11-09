import { QueryClientProvider } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { createQueryClient } from '@/shared/api/queryClient';
import { AuthProvider } from '@/shared/auth/AuthProvider';
import { RealtimeProvider } from '@/shared/realtime/RealtimeProvider';

/** Provider order matters: Query → Auth (uses Query) → Realtime (needs the user + query cache). */
export function Providers({ children }: { children: ReactNode }) {
  const [queryClient] = useState(createQueryClient);
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RealtimeProvider>{children}</RealtimeProvider>
      </AuthProvider>
    </QueryClientProvider>
  );
}
