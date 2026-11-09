import { z } from 'zod';
import { ApiError } from '@/shared/api/errors';

export const citationSchema = z.object({ number: z.number(), title: z.string(), section: z.string(), score: z.number() });
export type Citation = z.infer<typeof citationSchema>;

export type CopilotEvent =
  | { type: 'citations'; citations: Citation[] }
  | { type: 'delta'; text: string }
  | { type: 'done' };

/**
 * Reads Server-Sent Events via fetch + ReadableStream (instead of EventSource) so we can POST a body,
 * send the CSRF header, and abort — all through the cookie-authenticated BFF.
 */
export async function* streamAnswer(
  question: string,
  claimId: string | null,
  signal: AbortSignal,
): AsyncGenerator<CopilotEvent> {
  const response = await fetch(new URL('/api/copilot/ask', window.location.origin), {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream', 'X-CSRF': '1' },
    body: JSON.stringify({ question, claimId }),
    signal,
  });
  if (!response.ok || !response.body) throw new ApiError(response.status);

  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = '';
  for (;;) {
    const { value, done } = await reader.read();
    if (done) return;
    buffer += value;

    let boundary: number;
    while ((boundary = buffer.indexOf('\n\n')) >= 0) {
      const frame = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      const event = parseFrame(frame);
      if (event) yield event;
    }
  }
}

export function parseFrame(frame: string): CopilotEvent | null {
  let name = 'message';
  const data: string[] = [];
  for (const line of frame.split('\n')) {
    if (line.startsWith('event:')) name = line.slice(6).trim();
    else if (line.startsWith('data:')) data.push(line.slice(5).replace(/^ /, ''));
  }
  const payload = data.join('\n');

  switch (name) {
    case 'citations':
      return { type: 'citations', citations: z.array(citationSchema).parse(JSON.parse(payload)) };
    case 'delta':
      return { type: 'delta', text: decodeText(payload) };
    case 'done':
      return { type: 'done' };
    default:
      return null;
  }
}

/** The server may send a delta as a JSON string ("\"text\"") or as raw text; accept both. */
function decodeText(payload: string): string {
  try {
    const value: unknown = JSON.parse(payload);
    return typeof value === 'string' ? value : payload;
  } catch {
    return payload;
  }
}
