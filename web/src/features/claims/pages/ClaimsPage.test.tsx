import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { renderRoute } from '@/test/renderWithProviders';
import ClaimsPage from './ClaimsPage';

describe('ClaimsPage', () => {
  it('lists my claims from the API', async () => {
    renderRoute(<ClaimsPage />, '/claims');

    expect(await screen.findByRole('link', { name: 'KL1234' })).toBeInTheDocument();
    expect(screen.getByText('€400')).toBeInTheDocument();
  });

  it('shows server-side validation errors next to the right field', async () => {
    const user = userEvent.setup();
    renderRoute(<ClaimsPage />, '/claims');

    await user.type(screen.getByLabelText(/Flight number/), 'KL1234');
    await user.type(screen.getByLabelText(/From \(IATA\)/), 'AMS');
    await user.type(screen.getByLabelText(/To \(IATA\)/), 'AMS');
    await user.click(screen.getByRole('button', { name: /file claim/i }));

    await waitFor(() => expect(screen.getByText('Destination must differ from origin.')).toBeInTheDocument());
    expect(screen.getByLabelText(/To \(IATA\)/)).toHaveAttribute('aria-invalid', 'true');
  });
});
