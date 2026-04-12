import { defineConfig } from '@playwright/test'
import {
  buildHttpOrigin,
  defaultFrontendHost,
  defaultFrontendPort,
  parseFrontendHost,
  resolveDefaultFrontendPort,
} from './playwright.port-resolution'
import { resolveDemoBackendLlmEnv, resolvePlaywrightBackendLlmEnv } from './playwright.demo-llm'
import { resolveReuseExistingServer } from './playwright.server-reuse'

const e2eDbPath = process.env.TASKDECK_E2E_DB ?? 'taskdeck.e2e.visual.db'
const defaultApiBaseUrl = 'http://localhost:5000/api'
const demoBackendLlmEnv = resolveDemoBackendLlmEnv(process.env)
const backendLlmEnv = resolvePlaywrightBackendLlmEnv(process.env)
const reuseExistingServer = resolveReuseExistingServer(process.env, {
  requiresFreshServer: Object.keys(demoBackendLlmEnv).length > 0,
})

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
  ...backendLlmEnv,
}

for (const [index, origin] of backendCorsOrigins.entries()) {
  backendServerEnv[`Cors__DevelopmentAllowedOrigins__${index}`] = origin
}

/**
 * Playwright configuration for visual regression tests.
 *
 * Key differences from the main E2E config:
 * - testDir points to tests/visual/
 * - Fixed viewport (1280x720) for deterministic screenshots
 * - Animations disabled via reducedMotion to prevent flaky diffs
 * - Screenshot comparison thresholds tuned for cross-platform tolerance
 * - Snapshot path template includes platform for OS-specific baselines
 */
export default defineConfig({
  testDir: './tests/visual',
  forbidOnly: !!process.env.CI,
  fullyParallel: false,
  workers: 1,
  maxFailures: process.env.CI ? 5 : undefined,
  globalTimeout: process.env.CI ? 15 * 60_000 : undefined,
  timeout: 60_000,
  expect: {
    timeout: 10_000,
    toHaveScreenshot: {
      // Allow up to 0.5% pixel difference to absorb font rendering and
      // anti-aliasing variance across platforms and CI environments.
      maxDiffPixelRatio: 0.005,
      // Per-pixel color threshold (0-1). Slightly elevated to handle
      // sub-pixel anti-aliasing differences between local and CI.
      threshold: 0.3,
      // Animation stabilization wait before capture.
      animations: 'disabled',
    },
  },
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI
    ? [['line'], ['github'], ['html', { open: 'never' }]]
    : 'list',
  snapshotPathTemplate: '{testDir}/__screenshots__/{testFilePath}/{arg}{ext}',
  use: {
    baseURL: frontendBaseUrl,
    trace: 'retain-on-failure',
    // Fixed viewport for deterministic screenshots
    viewport: { width: 1280, height: 720 },
    // Disable CSS animations and transitions
    reducedMotion: 'reduce',
    // Consistent color scheme
    colorScheme: 'light',
    screenshot: 'off',
  },
  webServer: [
    {
      command: 'dotnet run --no-launch-profile --project ../../backend/src/Taskdeck.Api/Taskdeck.Api.csproj',
      url: apiConfig.readinessUrl,
      timeout: 120_000,
      reuseExistingServer,
      stdout: 'pipe',
      stderr: 'pipe',
      env: backendServerEnv,
    },
    {
      command: `npm run dev -- --host ${frontendHost} --port ${frontendPort}`,
      url: frontendBaseUrl,
      timeout: 120_000,
      reuseExistingServer,
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
          allowExistingFrontendReuse: reuseExistingServer,
        })

  if (!explicitFrontendPort && !resolvedFrontendPort) {
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
      `[visual config] TASKDECK_E2E_FRONTEND_BASE_URL must include an explicit port (example: "http://localhost:${defaultFrontendPort}"). Received "${rawFrontendBaseUrl}".`,
    )
  }

  if (normalizePath(parsedFrontendBaseUrl.pathname).length > 0) {
    throw new Error(
      `[visual config] TASKDECK_E2E_FRONTEND_BASE_URL cannot include a path segment. Received "${rawFrontendBaseUrl}".`,
    )
  }

  if (parsedFrontendBaseUrl.search.length > 0 || parsedFrontendBaseUrl.hash.length > 0) {
    throw new Error(
      `[visual config] TASKDECK_E2E_FRONTEND_BASE_URL cannot include query or hash fragments. Received "${rawFrontendBaseUrl}".`,
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
      `[visual config] TASKDECK_E2E_FRONTEND_BASE_URL must be an absolute http URL. Received "${rawFrontendBaseUrl}". ${reason}`,
      { cause: error },
    )
  }
}

function parsePort(rawPort: string | undefined, fallbackPort: number, source: string): number {
  if (!rawPort) {
    return fallbackPort
  }

  const normalizedPort = rawPort.trim()
  if (!/^\d+$/.test(normalizedPort)) {
    throw new Error(`[visual config] ${source} must be an integer between 1 and 65535. Received "${rawPort}".`)
  }

  const parsedPort = Number.parseInt(normalizedPort, 10)
  if (parsedPort < 1 || parsedPort > 65535) {
    throw new Error(`[visual config] ${source} must be between 1 and 65535. Received "${rawPort}".`)
  }

  return parsedPort
}

function resolveApiConfig(rawApiBaseUrl: string): ApiConfig {
  const parsedApiBaseUrl = parseApiBaseUrl(rawApiBaseUrl)
  const apiPath = normalizePath(parsedApiBaseUrl.pathname)
  if (apiPath.length === 0) {
    throw new Error(
      `[visual config] TASKDECK_E2E_API_BASE_URL must include an API path (example: "${defaultApiBaseUrl}"). Received "${rawApiBaseUrl}".`,
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
      `[visual config] TASKDECK_E2E_API_BASE_URL must be an absolute http URL with explicit port. Received "${rawApiBaseUrl}". ${reason}`,
      { cause: error },
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
