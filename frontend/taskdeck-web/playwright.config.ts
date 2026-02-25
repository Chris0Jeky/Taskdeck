import { defineConfig } from '@playwright/test'

const e2eDbPath = process.env.TASKDECK_E2E_DB ?? 'taskdeck.e2e.db'
const defaultFrontendHost = 'localhost'
const defaultFrontendPort = 5173
const defaultApiBaseUrl = 'http://localhost:5000/api'

const frontendConfig = resolveFrontendConfig()
const frontendHost = frontendConfig.host
const frontendPort = frontendConfig.port
const frontendBaseUrl = frontendConfig.baseUrl
const apiBaseUrl = process.env.TASKDECK_E2E_API_BASE_URL ?? defaultApiBaseUrl

const backendCorsOrigins = resolveBackendCorsOrigins(
  frontendConfig.origin,
  process.env.TASKDECK_E2E_API_CORS_ORIGINS,
)
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

type FrontendConfig = {
  baseUrl: string
  host: string
  origin: string
  port: number
}

function resolveFrontendConfig(): FrontendConfig {
  const rawFrontendBaseUrl = process.env.TASKDECK_E2E_FRONTEND_BASE_URL
  if (rawFrontendBaseUrl && rawFrontendBaseUrl.trim().length > 0) {
    return resolveFrontendConfigFromBaseUrl(rawFrontendBaseUrl)
  }

  const host = process.env.TASKDECK_E2E_FRONTEND_HOST ?? defaultFrontendHost
  const port = parsePort(process.env.TASKDECK_E2E_FRONTEND_PORT, defaultFrontendPort, 'TASKDECK_E2E_FRONTEND_PORT')
  const origin = `http://${host}:${port}`

  return {
    baseUrl: origin,
    host,
    origin,
    port,
  }
}

function resolveFrontendConfigFromBaseUrl(rawFrontendBaseUrl: string): FrontendConfig {
  const parsedFrontendBaseUrl = parseFrontendBaseUrl(rawFrontendBaseUrl)
  const port =
    parsedFrontendBaseUrl.port.length > 0
      ? parsePort(
          parsedFrontendBaseUrl.port,
          defaultPortForProtocol(parsedFrontendBaseUrl.protocol),
          'TASKDECK_E2E_FRONTEND_BASE_URL',
        )
      : defaultPortForProtocol(parsedFrontendBaseUrl.protocol)

  return {
    baseUrl: parsedFrontendBaseUrl.href,
    host: parsedFrontendBaseUrl.hostname,
    origin: parsedFrontendBaseUrl.origin,
    port,
  }
}

function parseFrontendBaseUrl(rawFrontendBaseUrl: string): URL {
  try {
    const parsedFrontendBaseUrl = new URL(rawFrontendBaseUrl)
    if (parsedFrontendBaseUrl.protocol !== 'http:' && parsedFrontendBaseUrl.protocol !== 'https:') {
      throw new Error('Only http:// and https:// protocols are supported.')
    }

    return parsedFrontendBaseUrl
  } catch (error) {
    const reason = error instanceof Error ? error.message : 'Invalid URL format.'
    throw new Error(
      `[e2e config] TASKDECK_E2E_FRONTEND_BASE_URL must be an absolute http(s) URL (example: "http://localhost:5173"). Received "${rawFrontendBaseUrl}". ${reason}`,
    )
  }
}

function defaultPortForProtocol(protocol: string): number {
  if (protocol === 'http:') {
    return 80
  }

  if (protocol === 'https:') {
    return 443
  }

  throw new Error(
    `[e2e config] Unsupported TASKDECK_E2E_FRONTEND_BASE_URL protocol "${protocol}". Use http:// or https://.`,
  )
}

function parsePort(rawPort: string | undefined, fallbackPort: number, source: string): number {
  if (!rawPort) {
    return fallbackPort
  }

  const normalizedPort = rawPort.trim()
  if (!/^\d+$/.test(normalizedPort)) {
    throw new Error(`[e2e config] ${source} must be an integer between 1 and 65535. Received "${rawPort}".`)
  }

  const parsedPort = Number.parseInt(normalizedPort, 10)
  if (parsedPort < 1 || parsedPort > 65535) {
    throw new Error(`[e2e config] ${source} must be between 1 and 65535. Received "${rawPort}".`)
  }

  return parsedPort
}

function resolveBackendCorsOrigins(frontendOrigin: string, rawOrigins: string | undefined): string[] {
  return dedupeOrigins([frontendOrigin, 'http://localhost:5174', ...parseOriginList(rawOrigins)])
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
