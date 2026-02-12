import { defineConfig } from '@playwright/test'

const e2eDbPath = process.env.TASKDECK_E2E_DB ?? 'taskdeck.e2e.db'

export default defineConfig({
  testDir: './tests/e2e',
  forbidOnly: !!process.env.CI,
  fullyParallel: false,
  maxFailures: process.env.CI ? 1 : undefined,
  globalTimeout: process.env.CI ? 12 * 60_000 : undefined,
  timeout: 60_000,
  expect: {
    timeout: 10_000,
  },
  retries: process.env.CI ? 0 : 0,
  reporter: process.env.CI ? [['line'], ['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      command: 'dotnet run --project ../../backend/src/Taskdeck.Api/Taskdeck.Api.csproj',
      url: 'http://localhost:5000/api/boards',
      timeout: 120_000,
      reuseExistingServer: !process.env.CI,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ConnectionStrings__DefaultConnection: `Data Source=${e2eDbPath}`,
      },
    },
    {
      command: 'npm run dev -- --host localhost --port 5173',
      url: 'http://localhost:5173',
      timeout: 120_000,
      reuseExistingServer: !process.env.CI,
      env: {
        VITE_API_BASE_URL: 'http://localhost:5000/api',
      },
    },
  ],
})
