/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';

// In dev, the SPA is served by Vite and every backend path is proxied to the gateway/BFF,
// so the browser sees ONE origin → the session cookie just works (no CORS, no tokens in JS).
const gateway = process.env.GATEWAY_URL ?? 'http://localhost:8080';

export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: gateway, changeOrigin: false },
      '/bff': { target: gateway, changeOrigin: false },
      '/signin-oidc': { target: gateway, changeOrigin: false },
      '/signout-callback-oidc': { target: gateway, changeOrigin: false },
      '/hubs': { target: gateway, ws: true, changeOrigin: false },
    },
  },
  build: {
    sourcemap: true,
    rollupOptions: {
      output: {
        // Long-term caching: vendor code changes rarely.
        manualChunks: { react: ['react', 'react-dom', 'react-router'], data: ['@tanstack/react-query', 'zod', 'zustand'] },
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    coverage: { reporter: ['text', 'lcov'] },
  },
});
