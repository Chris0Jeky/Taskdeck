import { defineConfig, devices } from '@playwright/test'
import {
  buildHttpOrigin,
  defaultFrontendHost,
  defaultFrontendPort,
  parseFrontendHost,
  resolveDefaultFrontendPort,
} from './playwright.port-resolution'
import { resolveDemoBackendLlmEnv, resolvePlaywrightBackendLlmEnv } from './playwright.demo-llm'
import { resolveReuseExistingServer } from './playwright.server-reuse'

const e2eDbPath = process.env.TASKDECK_E2E_DB ?? 'taskdeck.e2e.db'
/*
 * SQLite connection options tuned for E2E parallelization (TST-60, #867).
 *
 * With `fullyParallel: true`, multiple Playwright workers drive the same backend
 * process concurrently. Each test uses a distinct user/board (see
 * `registerUserSession` in tests/e2e/support/authSession.ts), so the logical test
 * data is already isolated. The remaining contention is at the SQLite engine level:
 * parallel writes on the same database file can briefly block each other.
 *
 *   - Pooling=True        — reuse connection objects (also Microsoft.Data.Sqlite default).
 *   - Default Timeout=30  — sets ADO.NET SqliteCommand.CommandTimeout to 30 seconds
 *                           (command cancellation, not PRAGMA busy_timeout). Under
 *                           parallel E2E traffic this prevents premature command
 *                           timeouts; SQLite's actual busy-wait behavior depends on
 *                           busy_timeout PRAGMA (default 0ms in Microsoft.Data.Sqlite).
 *
 * We intentionally do NOT set `Cache=Shared`: shared-cache mode adds internal
 * table-level locking that can increase contention (and SQLITE_BUSY frequency)
 * in multi-threaded scenarios rather than reduce it. Future work may enable WAL
 * (`PRAGMA journal_mode=WAL;`) in the backend for genuine concurrent-read
 * throughput; until then the default private-cache mode plus a generous busy
 * timeout is the safer default.
 *
 * These are additive: they do not alter the on-disk format or the test database path,
 * and they are scoped to the E2E backend process launched by this config.
 */
const e2eSqliteConnectionOptions = 'Pooling=True;Default Timeout=30'
const e2eConnectionString = `Data Source=${e2eDbPath};${e2eSqliteConnectionOptions}`
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
  ConnectionStrings__DefaultConnection: e2eConnectionString,
  ASPNETCORE_URLS: apiConfig.origin,
  ...backendLlmEnv,
}

for (const [index, origin] of backendCorsOrigins.entries()) {
  backendServerEnv[`Cors__DevelopmentAllowedOrigins__${index}`] = origin
}

/*
 * Worker count resolution (TST-60, #867):
 *
 * CI default is 1 worker — conservative, matches the pre-TST-60 status quo,
 * and avoids exposing latent Vue-re-render / Playwright-actionability races
 * that surfaced under 2-worker parallel CPU contention (see #867 PR comments
 * for the WIP-limit smoke test case). Local default is 2 workers — a modest
 * dev-box speedup inside the contention budget this config was tuned for.
 * In both cases we intentionally cap below Playwright's own default
 * (~50% of logical cores), which fans out well past what a single SQLite
 * E2E database can absorb without SQLITE_BUSY bursts.
 *
 * Override via TASKDECK_E2E_WORKERS if needed (integer >= 1). CI consumers
 * that want to adopt parallel runs should flip their workflow env var, so
 * any fallout is scoped to the workflow opting in. Shipping parallel-safe
 * infrastructure (this config, SQLite connection tuning, per-test user
 * isolation, hardened WIP-limit spec) is the TST-60 deliverable; meeting
 * the "40% runtime reduction" acceptance criterion requires follow-up work
 * on the remaining Vue/actionability races and is tracked for a later PR.
 */
const ciDefaultWorkerCount = 1
const localDefaultWorkerCount = 2
const effectiveDefaultWorkerCount = process.env.CI ? ciDefaultWorkerCount : localDefaultWorkerCount
const e2eWorkers = resolveWorkers(process.env.TASKDECK_E2E_WORKERS, effectiveDefaultWorkerCount)

export default defineConfig({
  testDir: './tests/e2e',
  forbidOnly: !!process.env.CI,
  /*
   * Parallel execution is safe because tests provision unique users, boards,
   * columns, and cards per test case (see tests/e2e/support/authSession.ts and
   * boardHelpers.ts — names include Date.now() + random suffixes, and data is
   * scoped server-side by the authenticated user). The opt-in stakeholder demo
   * (stakeholder-demo.spec.ts) is still skipped by default and should remain
   * opt-in serial work.
   */
  fullyParallel: true,
  workers: e2eWorkers,
  maxFailures: process.env.CI ? 3 : undefined,
  globalTimeout: process.env.CI ? 12 * 60_000 : undefined,
  timeout: 45_000,
  expect: {
    timeout: 8_000,
  },
  retries: process.env.CI ? 0 : 0,
  reporter: process.env.CI ? [['line'], ['github'], ['html', { open: 'never' }]] : 'list',
  /* Exclude quarantined tests from all projects (see docs/testing/FLAKY_TEST_POLICY.md). */
  grepInvert: /@quarantine/,
  use: {
    baseURL: frontendBaseUrl,
    trace: 'retain-on-failure',
  },

  /* ---------------------------------------------------------------------------
   * Browser & device projects
   *
   * Tagging strategy (see docs/testing/FLAKY_TEST_POLICY.md):
   *   @smoke          — quick PR gate (Chromium-only, default)
   *   @cross-browser  — full browser matrix (nightly / manual)
   *   @mobile         — mobile viewport scenarios (nightly / manual)
   *
   * CI behaviour:
   *   PR (ci-required)     → "chromium" project only (grep excludes @mobile)
   *   Nightly / manual     → all projects via reusable-e2e-cross-browser.yml
   * -----------------------------------------------------------------------*/
  projects: [
    /* --- Desktop browsers --- */
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
      /* Default project: runs all tests except @mobile-only scenarios.
       * Existing untagged tests continue to run here unchanged.
       *
       * NOTE: @cross-browser tests also run here (in PR gate via ci-required).
       * Adding more @cross-browser tests will increase PR gate time.
       * Keep @cross-browser count lean to preserve fast PR feedback.
       *
       * Combined pattern ensures quarantine exclusion is preserved
       * (project-level grepInvert overrides the global one). */
      grepInvert: /@mobile|@quarantine/,
    },
    {
      name: 'firefox',
      use: { ...devices['Desktop Firefox'] },
      /* Only tests explicitly tagged @cross-browser run on Firefox. */
      grep: /@cross-browser/,
    },
    {
      name: 'webkit',
      use: { ...devices['Desktop Safari'] },
      /* Only tests explicitly tagged @cross-browser run on WebKit. */
      grep: /@cross-browser/,
    },
    /* --- Mobile viewports --- */
    {
      name: 'mobile-chrome',
      use: { ...devices['Pixel 7'] },
      /* Only tests tagged @mobile run on mobile viewports. */
      grep: /@mobile/,
    },
    {
      name: 'mobile-safari',
      use: { ...devices['iPhone 14'] },
      grep: /@mobile/,
    },
  ],

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

/**
 * Resolve the worker count for the E2E runner.
 *
 * Priority:
 *   1. `TASKDECK_E2E_WORKERS` env var (integer >= 1) when set.
 *   2. `fallbackDefault` for every other run (both CI and local), so the
 *      fully-parallel contention budget is respected in all environments.
 */
function resolveWorkers(rawOverride: string | undefined, fallbackDefault: number): number {
  if (rawOverride !== undefined) {
    const trimmed = rawOverride.trim()
    if (!/^\d+$/.test(trimmed)) {
      throw new Error(
        `[e2e config] TASKDECK_E2E_WORKERS must be a positive integer. Received "${rawOverride}".`,
      )
    }

    const parsed = Number.parseInt(trimmed, 10)
    if (parsed < 1) {
      throw new Error(
        `[e2e config] TASKDECK_E2E_WORKERS must be >= 1. Received "${rawOverride}".`,
      )
    }
    return parsed
  }

  return fallbackDefault
}
