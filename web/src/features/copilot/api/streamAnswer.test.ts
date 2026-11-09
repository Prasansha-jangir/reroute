import { describe, expect, it } from 'vitest';
import { parseFrame } from './streamAnswer';

describe('parseFrame (Server-Sent Events)', () => {
  it('parses citations', () => {
    const frame = 'event: citations\ndata: [{"number":1,"title":"EU261 guide","section":"Article 7","score":0.82}]';
    expect(parseFrame(frame)).toEqual({ type: 'citations', citations: [{ number: 1, title: 'EU261 guide', section: 'Article 7', score: 0.82 }] });
  });

  it('accepts JSON-encoded and raw text deltas', () => {
    expect(parseFrame('event: delta\ndata: "You are owed "')).toEqual({ type: 'delta', text: 'You are owed ' });
    expect(parseFrame('event: delta\ndata: €400 [1]')).toEqual({ type: 'delta', text: '€400 [1]' });
  });

  it('ignores unknown events', () => {
    expect(parseFrame('event: ping\ndata: {}')).toBeNull();
  });
});
