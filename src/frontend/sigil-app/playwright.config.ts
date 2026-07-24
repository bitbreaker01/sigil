import { defineConfig, devices } from '@playwright/test';

// E2E contra la app REAL hosteada en Dev.
// Corre por CI (workflow_dispatch), NUNCA en el CI de PR: necesita secrets + login a Entra.
// La autenticación se hace una sola vez (proyecto `setup`) y se reusa vía storageState.
export default defineConfig({
  testDir: './e2e',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1, // misma cuenta de prueba en todos los tests → serializar para evitar races de estado
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [['html', { open: 'never' }], ['list']],
  use: {
    baseURL: process.env.SIGIL_APP_URL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    { name: 'setup', testMatch: /.*\.setup\.ts/ },
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], storageState: 'e2e/.auth/user.json' },
      dependencies: ['setup'],
    },
  ],
});
