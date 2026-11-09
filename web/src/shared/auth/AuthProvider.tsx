import { createContext, use, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { z } from 'zod';
import { ApiError } from '../api/errors';

const userSchema = z.object({
  id: z.string(),
  name: z.string().nullable(),
  email: z.string().nullable(),
  roles: z.array(z.string()),
});

export type User = z.infer<typeof userSchema>;

interface AuthState {
  user: User | null;
  isLoading: boolean;
  hasRole: (role: string) => boolean;
  login: (returnUrl?: string) => void;
  logout: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

/** BFF pattern: the SPA never sees tokens. It only asks the gateway "who am I?" (cookie-authenticated). */
async function fetchUser(): Promise<User | null> {
  const response = await fetch(new URL('/bff/user', window.location.origin), { credentials: 'include', headers: { 'X-CSRF': '1' } });
  if (response.status === 401) return null;
  if (!response.ok) throw new ApiError(response.status);
  return userSchema.parse(await response.json());
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const { data, isLoading } = useQuery({ queryKey: ['bff', 'user'], queryFn: fetchUser, staleTime: 5 * 60_000 });
  const user = data ?? null;

  const value: AuthState = {
    user,
    isLoading,
    hasRole: (role) => user?.roles.includes(role) ?? false,
    login: (returnUrl = window.location.pathname) => window.location.assign(`/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`),
    logout: () => {
      if (import.meta.env.VITE_MOCK === 'true') {
        window.location.assign('/'); // mock mode: you're always "logged in"
        return;
      }
      // POST (not GET) so a malicious <img src="/bff/logout"> can't log users out.
      const form = document.createElement('form');
      form.method = 'POST';
      form.action = '/bff/logout';
      document.body.appendChild(form);
      form.submit();
    },
  };

  return <AuthContext value={value}>{children}</AuthContext>;
}

export function useAuth(): AuthState {
  const ctx = use(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>');
  return ctx;
}
