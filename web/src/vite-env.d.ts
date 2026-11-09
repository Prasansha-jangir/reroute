/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** "true" when started with `npm run dev:mock` (MSW fake backend, no Docker needed). */
  readonly VITE_MOCK?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
