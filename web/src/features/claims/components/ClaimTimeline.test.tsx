import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ClaimTimeline } from './ClaimTimeline';

describe('ClaimTimeline', () => {
  it('renders every event in stream order with human-readable names and reasons', () => {
    render(
      <ClaimTimeline
        entries={[
          { version: 1, eventType: 'ClaimFiled', occurredAt: '2026-11-09T10:00:00Z', data: '{}' },
          { version: 2, eventType: 'EligibilityAssessed', occurredAt: '2026-11-09T10:00:01Z', data: '{"reason":"Arrival delay of 210 min ≥ 3 h."}' },
        ]}
      />,
    );

    const items = within(screen.getByRole('list', { name: 'Claim history' })).getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('Claim filed');
    expect(items[1]).toHaveTextContent('Eligibility assessed');
    expect(items[1]).toHaveTextContent('Arrival delay of 210 min ≥ 3 h.');
  });

  it('tolerates malformed event data', () => {
    render(<ClaimTimeline entries={[{ version: 1, eventType: 'ClaimFiled', occurredAt: '2026-11-09T10:00:00Z', data: 'not json' }]} />);
    expect(screen.getByText('Claim filed')).toBeInTheDocument();
  });
});
