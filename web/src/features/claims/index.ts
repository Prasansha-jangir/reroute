// Public API of the claims feature. Other features/app code import ONLY from here (enforced by ESLint).
import { lazy } from 'react';

export const ClaimsPage = lazy(() => import('./pages/ClaimsPage'));
export const ClaimDetailPage = lazy(() => import('./pages/ClaimDetailPage'));
export { claimKeys } from './api/queryKeys';
