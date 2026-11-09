import { useMutation } from '@tanstack/react-query';
import type { FormEvent } from 'react';
import { z } from 'zod';
import { http } from '@/shared/api/httpClient';

const watchResponse = z.object({ flightKey: z.string() });

/** Register flights to watch; disruptions then arrive in real time as toasts (Kafka → RabbitMQ → SignalR). */
export default function FlightsPage() {
  const watch = useMutation({
    mutationFn: (input: { flightNumber: string; flightDate: string }) =>
      http('/api/flights/watch', { method: 'POST', body: input, schema: watchResponse }),
  });

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    watch.mutate({ flightNumber: String(form.get('flightNumber')), flightDate: String(form.get('flightDate')) });
  }

  return (
    <section className="card">
      <h2>Watch a flight</h2>
      <p className="muted">We'll alert you within seconds if it's delayed, cancelled or diverted.</p>
      <form onSubmit={onSubmit} className="row">
        <input name="flightNumber" placeholder="KL1000" aria-label="Flight number" required />
        <input name="flightDate" type="date" aria-label="Flight date" required />
        <button type="submit" disabled={watch.isPending}>Watch</button>
      </form>
      {watch.isSuccess && <p role="status">Watching {watch.data.flightKey}. Keep this tab open for live alerts.</p>}
      {watch.isError && <p role="alert">{watch.error.message}</p>}
    </section>
  );
}
