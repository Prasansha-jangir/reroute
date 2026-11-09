import { useEffect, type ReactNode } from 'react';
import { useAuth } from './AuthProvider';

export function RequireAuth({ children, role }: { children: ReactNode; role?: string }) {
  const { user, isLoading, login, hasRole } = useAuth();

  useEffect(() => {
    if (!isLoading && !user) login();
  }, [isLoading, user, login]);

  if (isLoading || !user) return <p aria-busy="true">Signing you in…</p>;
  if (role && !hasRole(role)) return <p role="alert">You don't have access to this page.</p>;
  return <>{children}</>;
}
