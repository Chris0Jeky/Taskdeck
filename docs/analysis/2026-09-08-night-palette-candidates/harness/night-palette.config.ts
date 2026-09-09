import { defineConfig, devices } from '@playwright/test'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'

const repoRoot = resolve(process.cwd(), '..', '..')
const reportDir = resolve(repoRoot, 'docs', 'analysis', '2026-09-08-night-palette-candidates')
const harnessDir = resolve(reportDir, 'harness')
const frontendDir = resolve(repoRoot, 'frontend', 'taskdeck-web')
const runtimeDir = resolve(reportDir, 'runtime')
mkdirSync(runtimeDir, { recursive: true })

const apiOrigin = 'http://127.0.0.1:58743'
const frontendOrigin = 'http://127.0.0.1:5181'
const e2eDb = resolve(runtimeDir, 'taskdeck.e2e.issue2009-night.db')

export default defineConfig({
  testDir: harnessDir,
  fullyParallel: false,
  workers: 1,
  timeout: 180_000,
  expect: { timeout: 15_000 },
  reporter: [['line'], ['html', { outputFolder: resolve(reportDir, 'playwright-report'), open: 'never' }]],
  use: {
    baseURL: frontendOrigin,
    trace: 'retain-on-failure',
    viewport: { width: 1440, height: 1000 },
    colorScheme: 'dark',
  },
  projects: [{
    name: 'chromium',
    use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 1000 } },
  }],
  webServer: [
    {
      command: 'dotnet run --no-launch-profile --project ../../backend/src/Taskdeck.Api/Taskdeck.Api.csproj',
      cwd: frontendDir,
      url: `${apiOrigin}/api/boards`,
      timeout: 120_000,
      reuseExistingServer: false,
      stdout: 'ignore',
      stderr: 'pipe',
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ConnectionStrings__DefaultConnection: `Data Source=${e2eDb};Pooling=True;Default Timeout=30`,
        ASPNETCORE_URLS: apiOrigin,
        Cors__DevelopmentAllowedOrigins__0: frontendOrigin,
        Llm__EnableLiveProviders: 'false',
        Llm__AllowLiveProvidersInDevelopment: 'false',
        Llm__Provider: 'Mock',
      },
    },
    {
      command: 'npm run dev -- --host 127.0.0.1 --port 5181',
      cwd: frontendDir,
      url: frontendOrigin,
      timeout: 120_000,
      reuseExistingServer: false,
      stdout: 'ignore',
      stderr: 'pipe',
      env: {
        VITE_API_BASE_URL: `${apiOrigin}/api`,
      },
    },
  ],
})
