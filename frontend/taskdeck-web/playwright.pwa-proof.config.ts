import { defineConfig, devices } from '@playwright/test'

/*
 * Standalone runner for the installed-worker PWA regression
 * (tests/e2e/pwa-api-cache.spec.ts, gated by TASKDECK_E2E_PWA_PREVIEW=1).
 *
 * The default playwright.config.ts launches `npm run dev`, which never emits the
 * generated service worker these tests assert on, and its webServer bootstrap is
 * what stalled in the failure-ledger record
 * `test/playwright-pwa-preview-bootstrap`. This config has NO webServer: the
 * operator starts the backend and a `vite preview` of a production build on fixed
 * ports, and points this config at them.
 */
const frontendBaseUrl = process.env.TASKDECK_E2E_FRONTEND_BASE_URL ?? 'http://localhost:4173'

export default defineConfig({
  testDir: './tests/e2e',
  // Only the two preview-gated PWA regressions. Without this an operator running the
  // config without --grep would drive the whole e2e suite at the preview server.
  testMatch: ['pwa-api-cache.spec.ts', 'pwa-proof-strict.spec.ts'],
  fullyParallel: false,
  workers: 1,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  retries: 0,
  reporter: 'list',
  use: {
    baseURL: frontendBaseUrl,
    serviceWorkers: 'allow',
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
})
