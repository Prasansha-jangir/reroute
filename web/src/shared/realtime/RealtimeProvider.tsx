import { useEffect, type ReactNode } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { useAuth } from '../auth/AuthProvider';
import { useUiStore } from '../store/uiStore';

const notificationSchema = z.object({
  id: z.string(),
  category: z.enum(['disruption', 'claim']).or(z.string()),
  title: z.string(),
  body: z.string(),
  link: z.string().nullable(),
  sentAt: z.string(),
});

/**
 * Real-time events are treated as cache invalidation signals: components keep reading from TanStack Query
 * and never need to know whether data arrived via HTTP or WebSocket.
 */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const pushToast = useUiStore((s) => s.pushToast);

  useEffect(() => {
    if (!user) return;

    const handle = (raw: unknown) => {
      const parsed = notificationSchema.safeParse(raw);
      if (!parsed.success) return;
      const n = parsed.data;
      pushToast({ id: n.id, title: n.title, body: n.body, link: n.link ?? undefined });
      if (n.category === 'claim') void queryClient.invalidateQueries({ queryKey: ['claims'] });
      if (n.category === 'disruption') void queryClient.invalidateQueries({ queryKey: ['disruptions'] });
    };

    // Mock mode (no backend): the fake backend dispatches DOM events instead of SignalR messages.
    if (import.meta.env.VITE_MOCK === 'true') {
      const listener = (e: Event) => handle((e as CustomEvent).detail);
      window.addEventListener('reroute:mock-notification', listener);
      return () => window.removeEventListener('reroute:mock-notification', listener);
    }

    // Same-origin via the BFF: the session cookie authenticates the WebSocket upgrade; the gateway relays the JWT.
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/notifications')
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('notification', handle);

    // Reconnected after a gap → we may have missed events; refetch everything that's visible.
    connection.onreconnected(() => void queryClient.invalidateQueries());

    void connection.start().catch((error: unknown) => console.warn('Realtime connection failed', error));
    return () => void connection.stop();
  }, [user, queryClient, pushToast]);

  return <>{children}</>;
}
