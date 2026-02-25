import { defineConfig } from '@playwright/test'

const e2eDbPath = process.env.TASKDECK_E2E_DB ?? 'taskdeck.e2e.db'
const defaultFrontendHost = 'localhost'
const defaultFrontendPort = 5173
const defaultApiBaseUrl = 'http://localhost:5000/api'

const frontendHost = process.env.TASKDECK_E2E_FRONTEND_HOST ?? defaultFrontendHost
const frontendPort = parsePort(process.env.TASKDECK_E2E_FRONTEND_PORT, defaultFrontendPort)
const frontendBaseUrl =
  process.env.TASKDECK_E2E_FRONTEND_BASE_URL ?? `http://${frontendHost}:${frontendPort}`
const apiBaseUrl = process.env.TASKDECK_E2E_API_BASE_URL ?? defaultApiBaseUrl

const backendCorsOrigins = resolveBackendCorsOrigins(frontendBaseUrl, process.env.TASKDECK_E2E_API_CORS_ORIGINS)
const backendServerEnv: Record<string, string> = {
  ASPNETCORE_ENVIRONMENT: 'Development',
  ConnectionStrings__DefaultConnection: `Data Source=${e2eDbPath}`,
}

for (const [index, origin] of backendCorsOrigins.entries()) {
  backendServerEnv[`Cors__DevelopmentAllowedOrigins__${index}`] = origin
}

export default defineConfig({
  testDir: './tests/e2e',
  forbidOnly: !!process.env.CI,
  fullyParallel: false,
  workers: process.env.CI ? 1 : undefined,
  maxFailures: process.env.CI ? 3 : undefined,
  globalTimeout: process.env.CI ? 12 * 60_000 : undefined,
  timeout: 45_000,
  expect: {
    timeout: 8_000,
  },
  retries: process.env.CI ? 0 : 0,
  reporter: process.env.CI ? [['line'], ['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: frontendBaseUrl,
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      command: 'dotnet run --project ../../backend/src/Taskdeck.Api/Taskdeck.Api.csproj',
      url: 'http://localhost:5000/api/boards',
      timeout: 120_000,
      reuseExistingServer: !process.env.CI,
      stdout: 'pipe',
      stderr: 'pipe',
      env: backendServerEnv,
    },
    {
      command: `npm run dev -- --host ${frontendHost} --port ${frontendPort}`,
      url: frontendBaseUrl,
      timeout: 120_000,
      reuseExistingServer: !process.env.CI,
      stdout: 'pipe',
      stderr: 'pipe',
      env: {
        VITE_API_BASE_URL: apiBaseUrl,
      },
    },
  ],
})

function parsePort(rawPort: string | undefined, fallbackPort: number): number {
  if (!rawPort) {
    return fallbackPort
  }

  const parsedPort = Number.parseInt(rawPort, 10)
  if (Number.isNaN(parsedPort) || parsedPort <= 0) {
    return fallbackPort
  }

  return parsedPort
}

function resolveBackendCorsOrigins(frontendUrl: string, rawOrigins: string | undefined): string[] {
  const configuredOrigins = parseOriginList(rawOrigins)
  if (configuredOrigins.length > 0) {
    return configuredOrigins
  }

  return dedupeOrigins([new URL(frontendUrl).origin, 'http://localhost:5174'])
}

function parseOriginList(rawOrigins: string | undefined): string[] {
  if (!rawOrigins) {
    return []
  }

  return dedupeOrigins(
    rawOrigins
      .split(',')
      .map((origin) => origin.trim())
      .filter((origin) => origin.length > 0),
  )
}

function dedupeOrigins(origins: string[]): string[] {
  return [...new Set(origins)]
}
