/// <reference types="vitest" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Code App (doc 05 §1/§10): SPA React, sin orígenes externos (todo se bundlea — CSP).
// `power-apps run` (dev local con conexiones reales) usa este mismo build; el CLI del
// SDK envuelve `vite` — la config es compatible.
export default defineConfig({
  plugins: [react()],
  // base relativa: el host de Power Apps sirve la app embebida (frame-ancestors 'self')
  base: './',
  // `pac code run` espera la app en localAppUrl (power.config.json → localhost:3000). Fijamos el
  // puerto acá para que coincida sin flags; strictPort falla si está ocupado en vez de saltar a otro.
  server: { port: 3000, strictPort: true },
  build: {
    target: 'es2022',
    sourcemap: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    // los binarios y pdf.js quedan fuera de los unit tests (viven en gates/E2E — doc 11)
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
