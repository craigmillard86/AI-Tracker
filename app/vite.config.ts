/// <reference types="vitest" />
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The API origin the dev server proxies /auth and /api to. Defaults to a locally-run API
// (`dotnet run` on the host); docker-compose overrides it to the api service's container DNS
// name (VITE_API_PROXY_TARGET=http://api:8080) since "localhost" inside the app container means
// the app container itself, not its sibling. Proxying (rather than CORS) keeps the browser
// talking only to the app origin, so the auth cookie (SameSite=Lax) never crosses an origin.
const apiProxyTarget = process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:8080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    host: true,
    proxy: {
      '/auth': { target: apiProxyTarget, changeOrigin: true },
      '/api': { target: apiProxyTarget, changeOrigin: true },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/setupTests.ts'],
    css: true,
  },
});
