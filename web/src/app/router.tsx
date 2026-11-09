import { createBrowserRouter, Navigate } from 'react-router';
import { ClaimDetailPage, ClaimsPage } from '@/features/claims';
import { FlightsPage } from '@/features/disruptions';
import { RequireAuth } from '@/shared/auth/RequireAuth';
import { AppShell } from './layout/AppShell';

/** Route-level code splitting: each feature page is a lazy chunk. */
export const router = createBrowserRouter([
  {
    path: '/',
    element: (
      <RequireAuth>
        <AppShell />
      </RequireAuth>
    ),
    children: [
      { index: true, element: <Navigate to="/claims" replace /> },
      { path: 'claims', element: <ClaimsPage /> },
      { path: 'claims/:claimId', element: <ClaimDetailPage /> },
      { path: 'flights', element: <FlightsPage /> },
      { path: 'disruptions/:flightKey', element: <FlightsPage /> },
      { path: '*', element: <p>Page not found.</p> },
    ],
  },
]);
