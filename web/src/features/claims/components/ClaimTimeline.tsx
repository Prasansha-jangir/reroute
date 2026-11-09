import { formatDateTime, humanizeEventType } from '@/shared/lib/format';
import type { TimelineEntry } from '../api/claimsApi';

/** Renders the claim's event stream — event sourcing made visible to the user (and to interviewers). */
export function ClaimTimeline({ entries }: { entries: TimelineEntry[] }) {
  if (entries.length === 0) return <p>No history yet.</p>;

  return (
    <ol className="timeline" aria-label="Claim history">
      {entries.map((entry) => (
        <li key={entry.version} className="timeline__item">
          <span className="timeline__version" aria-hidden="true">v{entry.version}</span>
          <div>
            <strong>{humanizeEventType(entry.eventType)}</strong>
            <time dateTime={entry.occurredAt}>{formatDateTime(entry.occurredAt)}</time>
            <Details data={entry.data} />
          </div>
        </li>
      ))}
    </ol>
  );
}

function Details({ data }: { data: string }) {
  const parsed = safeParse(data);
  const reason = typeof parsed?.['reason'] === 'string' ? parsed['reason'] : null;
  return reason ? <p className="timeline__detail">{reason}</p> : null;
}

function safeParse(json: string): Record<string, unknown> | null {
  try {
    return JSON.parse(json) as Record<string, unknown>;
  } catch {
    return null;
  }
}
