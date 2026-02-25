import { defineConfig } from '@playwright/test'
import {
  buildHttpOrigin,
  defaultFrontendHost,
  defaultFrontendPort,
  parseFrontendHost,
  resolveDefaultFrontendPort,
} from './playwright.port-resolution'

const e2eDbPath = process.env.TASKDECK_E2E_DB ?? 'taskdeck.e2e.db'
const defaultApiBaseUrl = 'http://localhost:5000/api'

const frontendConfig = resolveFrontendConfig()
const frontendHost = frontendConfig.host
const frontendPort = frontendConfig.port
const frontendBaseUrl = frontendConfig.baseUrl
const apiConfig = resolveApiConfig(process.env.TASKDECK_E2E_API_BASE_URL ?? defaultApiBaseUrl)
const apiBaseUrl = apiConfig.baseUrl

const backendCorsOrigins = resolveBackendCorsOrigins(
  frontendConfig.origin,
  process.env.TASKDECK_E2E_API_CORS_ORIGINS,
)
const backendServerEnv: Record<string, string> = {
  ASPNETCORE_ENVIRONMENT: 'Development',
  ConnectionStrings__DefaultConnection: `Data Source=${e2eDbPath}`,
  ASPNETCORE_URLS: apiConfig.origin,
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
      command: 'dotnet run --no-launch-profile --project ../../backend/src/Taskdeck.Api/Taskdeck.Api.csproj',
      url: apiConfig.readinessUrl,
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

type ApiConfig = {
  baseUrl: string
  origin: string
  readinessUrl: string
}

function resolveFrontendConfig(): FrontendConfig {
  const rawFrontendBaseUrl = process.env.TASKDECK_E2E_FRONTEND_BASE_URL
  if (rawFrontendBaseUrl && rawFrontendBaseUrl.trim().length > 0) {
    return resolveFrontendConfigFromBaseUrl(rawFrontendBaseUrl)
  }

  const host = parseFrontendHost(
    process.env.TASKDECK_E2E_FRONTEND_HOST ?? defaultFrontendHost,
    'TASKDECK_E2E_FRONTEND_HOST',
  )
  const explicitFrontendPort = process.env.TASKDECK_E2E_FRONTEND_PORT
  const resolvedFrontendPort = process.env.TASKDECK_E2E_RESOLVED_FRONTEND_PORT

  const port = explicitFrontendPort
    ? parsePort(explicitFrontendPort, defaultFrontendPort, 'TASKDECK_E2E_FRONTEND_PORT')
    : resolvedFrontendPort
      ? parsePort(
          resolvedFrontendPort,
          defaultFrontendPort,
          'TASKDECK_E2E_RESOLVED_FRONTEND_PORT',
        )
      : resolveDefaultFrontendPort(host, {
          allowExistingFrontendReuse: !process.env.CI,
        })

  if (!explicitFrontendPort && !resolvedFrontendPort) {
    // Keep runner/worker baseURL aligned by reusing the first resolved port value.
    process.env.TASKDECK_E2E_RESOLVED_FRONTEND_PORT = String(port)
  }

  const origin = buildHttpOrigin(host, port)

  return {
    baseUrl: origin,
    host,
    origin,
    port,
  }
}

function resolveFrontendConfigFromBaseUrl(rawFrontendBaseUrl: string): FrontendConfig {
  const parsedFrontendBaseUrl = parseFrontendBaseUrl(rawFrontendBaseUrl)
  if (parsedFrontendBaseUrl.port.length === 0) {
    throw new Error(
      `[e2e config] TASKDECK_E2E_FRONTEND_BASE_URL must include an explicit port (example: "http://localhost:${defaultFrontendPort}"). Received "${rawFrontendBaseUrl}".`,
    )
  }

  if (normalizePath(parsedFrontendBaseUrl.pathname).length > 0) {
    throw new Error(
      `[e2e config] TASKDECK_E2E_FRONTEND_BASE_URL cannot include a path segment. Use an origin only (example: "http://localhost:${defaultFrontendPort}"). Received "${rawFrontendBaseUrl}".`,
    )
  }

  if (parsedFrontendBaseUrl.search.length > 0 || parsedFrontendBaseUrl.hash.length > 0) {
    throw new Error(
      `[e2e config] TASKDECK_E2E_FRONTEND_BASE_URL cannot include query or hash fragments. Received "${rawFrontendBaseUrl}".`,
    )
  }

  const port = parsePort(
    parsedFrontendBaseUrl.port,
    defaultFrontendPort,
    'TASKDECK_E2E_FRONTEND_BASE_URL',
  )

  return {
    baseUrl: parsedFrontendBaseUrl.origin,
    host: parseFrontendHost(parsedFrontendBaseUrl.hostname, 'TASKDECK_E2E_FRONTEND_BASE_URL'),
    origin: parsedFrontendBaseUrl.origin,
    port,
  }
}

function parseFrontendBaseUrl(rawFrontendBaseUrl: string): URL {
  try {
    const parsedFrontendBaseUrl = new URL(rawFrontendBaseUrl)
    if (parsedFrontendBaseUrl.protocol !== 'http:') {
      throw new Error('Only http:// is supported.')
    }

    return parsedFrontendBaseUrl
  } catch (error) {
    const reason = error instanceof Error ? error.message : 'Invalid URL format.'
    throw new Error(
      `[e2e config] TASKDECK_E2E_FRONTEND_BASE_URL must be an absolute http URL (example: "http://localhost:${defaultFrontendPort}"). Received "${rawFrontendBaseUrl}". ${reason}`,
    )
  }
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

function resolveApiConfig(rawApiBaseUrl: string): ApiConfig {
  const parsedApiBaseUrl = parseApiBaseUrl(rawApiBaseUrl)
  const apiPath = normalizePath(parsedApiBaseUrl.pathname)
  if (apiPath.length === 0) {
    throw new Error(
      `[e2e config] TASKDECK_E2E_API_BASE_URL must include an API path (example: "${defaultApiBaseUrl}"). Received "${rawApiBaseUrl}".`,
    )
  }

  const normalizedBaseUrl = `${parsedApiBaseUrl.origin}${apiPath}`
  return {
    baseUrl: normalizedBaseUrl,
    origin: parsedApiBaseUrl.origin,
    readinessUrl: `${normalizedBaseUrl}/boards`,
  }
}

function parseApiBaseUrl(rawApiBaseUrl: string): URL {
  try {
    const parsedApiBaseUrl = new URL(rawApiBaseUrl)
    if (parsedApiBaseUrl.protocol !== 'http:') {
      throw new Error('Only http:// is supported.')
    }

    if (parsedApiBaseUrl.port.length === 0) {
      throw new Error('An explicit port is required.')
    }

    if (parsedApiBaseUrl.search.length > 0 || parsedApiBaseUrl.hash.length > 0) {
      throw new Error('Query and hash fragments are not supported.')
    }

    return parsedApiBaseUrl
  } catch (error) {
    const reason = error instanceof Error ? error.message : 'Invalid URL format.'
    throw new Error(
      `[e2e config] TASKDECK_E2E_API_BASE_URL must be an absolute http URL with explicit port (example: "${defaultApiBaseUrl}"). Received "${rawApiBaseUrl}". ${reason}`,
    )
  }
}

function normalizePath(pathname: string): string {
  if (!pathname || pathname === '/') {
    return ''
  }

  return pathname.replace(/\/+$/, '')
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
