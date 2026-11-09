import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router';
import { Providers } from './app/providers';
import { router } from './app/router';
import './styles.css';

/** In mock mode, install the fake backend BEFORE the app makes its first request. */
async function enableMocking(): Promise<void> {
  if (import.meta.env.VITE_MOCK !== 'true') return;
  // Dynamic import: the mock code is never included in the production bundle.
  const { startMockBackend } = await import('./mocks/browser');
  startMockBackend();
}

await enableMocking();

const root = document.getElementById('root');
if (!root) throw new Error('#root element missing');

createRoot(root).render(
  <StrictMode>
    <Providers>
      <RouterProvider router={router} />
    </Providers>
  </StrictMode>,
);
