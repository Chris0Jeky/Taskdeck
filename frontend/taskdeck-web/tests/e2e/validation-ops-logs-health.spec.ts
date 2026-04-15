import { expect, test } from '@playwright/test'
import { API_BASE_URL, API_ORIGIN, registerAndAttachSession, type AuthResult } from './support/authSession'
import { assertOk } from './support/httpAsserts'

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'ops-logs-health')
})

test.describe('Ops Console — CLI Runner', () => {
  test('should load ops console with templates', async ({ page }) => {
    await page.goto('/workspace/ops/cli')

    await expect(page.getByRole('heading', { name: 'Ops Console' })).toBeVisible()
    await expect(page.getByText('CLI Runner')).toBeVisible()
    await expect(page.getByText('Endpoint Explorer')).toBeVisible()
    await expect(page.getByText('Logs')).toBeVisible()

    // Role context panel should be visible
    await expect(page.getByText('Current role:')).toBeVisible()
  })

  test('should execute health.check and show output', async ({ page }) => {
    await page.goto('/workspace/ops/cli')

    const templateInput = page.getByRole('combobox', { name: 'Command template' })
    await templateInput.fill('health.check')
    await page.getByRole('button', { name: 'Run Template' }).click()

    await expect(page.getByText('Health check: OK')).toBeVisible()

    // Verify last run ID is displayed
    await expect(page.getByText('Last run ID:')).toBeVisible()
  })

  test('should show error for invalid parameters', async ({ page }) => {
    await page.goto('/workspace/ops/cli')

    const templateInput = page.getByRole('combobox', { name: 'Command template' })
    await templateInput.fill('health.check')

    // Fill invalid parameters
    await page.locator('#cli-parameters').fill('{"unexpected": "value"}')
    await page.getByRole('button', { name: 'Run Template' }).click()

    // Should show error in output or toast
    await expect(page.getByText('Error').first()).toBeVisible()
  })

  test('should reload templates without error', async ({ page }) => {
    await page.goto('/workspace/ops/cli')

    await page.getByRole('button', { name: 'Reload Templates' }).click()

    // Templates should still be present after reload
    const templateInput = page.getByRole('combobox', { name: 'Command template' })
    await expect(templateInput).toBeVisible()
  })
})

test.describe('Ops Console — Logs Tab', () => {
  test('should load logs tab and display entries after command run', async ({ page, request }) => {
    // First run a command to ensure log entries exist
    await request.post(`${API_BASE_URL}/ops/cli/run`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { templateName: 'health.check' },
    })

    await page.goto('/workspace/ops/logs')

    // Logs tab should be active
    await expect(page.locator('.td-tab--active')).toContainText('Logs')

    // Wait for log entries to load
    await expect(page.locator('.td-log-entry').first()).toBeVisible({ timeout: 10_000 })
  })

  test('should filter logs by level', async ({ page, request }) => {
    // Ensure some log entries exist
    await request.post(`${API_BASE_URL}/ops/cli/run`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { templateName: 'health.check' },
    })

    await page.goto('/workspace/ops/logs')
    await expect(page.locator('.td-log-entry').first()).toBeVisible({ timeout: 10_000 })

    // Filter by Info level
    await page.getByLabel('Log level filter').selectOption('Info')
    await page.getByRole('button', { name: 'Refresh' }).click()

    // All visible entries should be Info level (or empty)
    const entries = page.locator('.td-log-entry')
    const count = await entries.count()
    for (let i = 0; i < count; i++) {
      await expect(entries.nth(i).locator('.td-log-level')).toContainText('Info')
    }
  })

  test('should filter logs by source', async ({ page, request }) => {
    // Ensure some log entries exist
    await request.post(`${API_BASE_URL}/ops/cli/run`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { templateName: 'health.check' },
    })

    await page.goto('/workspace/ops/logs')
    await expect(page.locator('.td-log-entry').first()).toBeVisible({ timeout: 10_000 })

    // Filter by OpsCliService source
    await page.getByLabel('Source filter').fill('OpsCliService')
    await page.getByRole('button', { name: 'Refresh' }).click()

    const entries = page.locator('.td-log-entry')
    const count = await entries.count()
    for (let i = 0; i < count; i++) {
      await expect(entries.nth(i).locator('.td-log-source')).toContainText('OpsCliService')
    }
  })

  test('should show empty state for nonexistent source', async ({ page }) => {
    await page.goto('/workspace/ops/logs')

    await page.getByLabel('Source filter').fill('NonexistentSource12345')
    await page.getByRole('button', { name: 'Refresh' }).click()

    await expect(page.getByText('No logs match the current filters')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Clear Filters' })).toBeVisible()
  })

  test('should show correlation empty state for fabricated ID', async ({ page }) => {
    await page.goto('/workspace/ops/logs')

    await page.getByLabel('Correlation ID').fill('00000000000000000000000000000000')
    await page.getByRole('button', { name: 'Refresh' }).click()

    await expect(page.getByText('No logs for this correlation ID')).toBeVisible()
  })

  test('should clear filters and restore entries', async ({ page, request }) => {
    // Ensure some log entries exist
    await request.post(`${API_BASE_URL}/ops/cli/run`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { templateName: 'health.check' },
    })

    await page.goto('/workspace/ops/logs')
    await expect(page.locator('.td-log-entry').first()).toBeVisible({ timeout: 10_000 })

    // Apply a filter that yields no results
    await page.getByLabel('Source filter').fill('NonexistentSource12345')
    await page.getByRole('button', { name: 'Refresh' }).click()
    await expect(page.getByText('No logs match the current filters')).toBeVisible()

    // Clear filters
    await page.getByRole('button', { name: 'Clear Filters' }).click()

    // Entries should reappear
    await expect(page.locator('.td-log-entry').first()).toBeVisible({ timeout: 10_000 })
  })
})

test.describe('Ops Console — Correlation ID Propagation', () => {
  test('should trace correlation ID from command run to log entries', async ({ page, request }) => {
    // Run a command and capture the correlation ID
    const runResponse = await request.post(`${API_BASE_URL}/ops/cli/run`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { templateName: 'health.check' },
    })
    await assertOk(runResponse, 'run health.check')

    const run = await runResponse.json() as { id: string; correlationId: string; status: string }
    expect(run.correlationId).toBeTruthy()

    // Query logs by correlation ID via API
    const logsResponse = await request.get(
      `${API_BASE_URL}/logs/correlation/${encodeURIComponent(run.correlationId)}`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )
    await assertOk(logsResponse, 'query logs by correlation ID')

    const logs = await logsResponse.json() as Array<{ correlationId: string }>
    expect(logs.length).toBeGreaterThan(0)
    for (const entry of logs) {
      expect(entry.correlationId).toBe(run.correlationId)
    }

    // Verify the same correlation works in the UI
    await page.goto('/workspace/ops/logs')
    await page.getByLabel('Correlation ID').fill(run.correlationId)
    await page.getByRole('button', { name: 'Refresh' }).click()

    await expect(page.locator('.td-log-entry').first()).toBeVisible({ timeout: 10_000 })
    await expect(page.locator('.td-log-correlation').first()).toContainText(run.correlationId)
  })

  test('should return 403 when querying another user correlation via API', async ({ request }) => {
    // User A runs a command
    const runResponse = await request.post(`${API_BASE_URL}/ops/cli/run`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { templateName: 'health.check' },
    })
    await assertOk(runResponse, 'run health.check as user A')
    const run = await runResponse.json() as { correlationId: string }

    // Register User B
    const unique = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
    const regResponse = await request.post(`${API_BASE_URL}/auth/register`, {
      data: {
        username: `e2e-isolation-${unique}`,
        email: `e2e-isolation-${unique}@taskdeck.local`,
        password: 'E2ePassword123!',
      },
    })
    const userB = await regResponse.json() as { token: string }

    // User B tries to access User A's correlation
    const crossResponse = await request.get(
      `${API_BASE_URL}/logs/correlation/${encodeURIComponent(run.correlationId)}`,
      { headers: { Authorization: `Bearer ${userB.token}` } },
    )

    expect(crossResponse.status()).toBe(403)
  })
})

test.describe('Ops Console — Tab Navigation', () => {
  test('should sync tab state with URL', async ({ page }) => {
    // Start on CLI tab
    await page.goto('/workspace/ops/cli')
    await expect(page.locator('.td-tab--active')).toContainText('CLI Runner')

    // Click Endpoint Explorer tab
    await page.getByText('Endpoint Explorer').click()
    await expect(page).toHaveURL(/\/workspace\/ops\/endpoints/)
    await expect(page.locator('.td-tab--active')).toContainText('Endpoint Explorer')

    // Click Logs tab
    await page.getByText('Logs').click()
    await expect(page).toHaveURL(/\/workspace\/ops\/logs/)
    await expect(page.locator('.td-tab--active')).toContainText('Logs')

    // Direct navigation to logs URL
    await page.goto('/workspace/ops/logs')
    await expect(page.locator('.td-tab--active')).toContainText('Logs')

    // Direct navigation to CLI URL
    await page.goto('/workspace/ops/cli')
    await expect(page.locator('.td-tab--active')).toContainText('CLI Runner')
  })
})

test.describe('Ops Console — Endpoint Explorer', () => {
  test('should send GET request and display response', async ({ page }) => {
    await page.goto('/workspace/ops/endpoints')

    // Verify endpoint form loads
    await expect(page.getByLabel('HTTP method')).toBeVisible()
    await expect(page.getByLabel('Request path')).toBeVisible()

    // Send GET /boards
    await page.getByLabel('HTTP method').selectOption('GET')
    await page.getByLabel('Request path').fill('/boards')
    await page.getByRole('button', { name: 'Send' }).click()

    // Verify response panel appears
    await expect(page.locator('.td-response-panel')).toBeVisible()
    await expect(page.locator('.td-status-code')).toBeVisible()
  })
})

test.describe('Health Endpoints', () => {
  test('/health/live should return healthy without auth', async ({ request }) => {
    const response = await request.get(`${API_ORIGIN}/health/live`)

    expect(response.status()).toBe(200)
    const body = await response.json() as { status: string; timestamp: string }
    expect(body.status).toBe('Healthy')
    expect(body.timestamp).toBeTruthy()
  })

  test('/health/ready should return subsystem checks', async ({ request }) => {
    const response = await request.get(`${API_ORIGIN}/health/ready`)

    // Accept 200 or 503 (workers may still be starting)
    expect([200, 503]).toContain(response.status())

    const body = await response.json() as {
      status: string
      timestamp: string
      checks: {
        database: { status: string }
        queue: { status: string; depth: number; totalDepth: number; captureDepth: number; threshold: number }
        signalrBackplane: { status: string }
        workers: {
          queueToProposal: { status: string; stalenessSeconds: number | null; maxStalenessSeconds: number }
          proposalHousekeeping: { status: string; stalenessSeconds: number | null; maxStalenessSeconds: number }
        }
      }
    }

    // Verify top-level fields
    expect(body.status).toMatch(/^(Ready|NotReady)$/)
    expect(body.timestamp).toBeTruthy()

    // Verify database check
    expect(body.checks.database.status).toBeTruthy()

    // Verify queue check structure
    expect(body.checks.queue).toBeDefined()
    expect(typeof body.checks.queue.depth).toBe('number')
    expect(typeof body.checks.queue.totalDepth).toBe('number')
    expect(typeof body.checks.queue.threshold).toBe('number')

    // Verify SignalR backplane (local dev = NotConfigured)
    expect(body.checks.signalrBackplane).toBeDefined()
    expect(body.checks.signalrBackplane.status).toBeTruthy()

    // Verify worker checks
    expect(body.checks.workers.queueToProposal).toBeDefined()
    expect(body.checks.workers.queueToProposal.maxStalenessSeconds).toBeGreaterThan(0)
    expect(body.checks.workers.proposalHousekeeping).toBeDefined()
    expect(body.checks.workers.proposalHousekeeping.maxStalenessSeconds).toBeGreaterThan(0)
  })

  test('/health/ready should separate capture backlog from automation queue depth', async ({ request }) => {
    // Create some capture items to increase capture depth
    for (let i = 0; i < 2; i++) {
      await request.post(`${API_BASE_URL}/capture/items`, {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { boardId: null, text: `health e2e capture ${i} ${Date.now()}` },
      })
    }

    const response = await request.get(`${API_ORIGIN}/health/ready`)
    expect([200, 503]).toContain(response.status())

    const body = await response.json() as {
      checks: {
        queue: { depth: number; totalDepth: number; captureDepth: number }
      }
    }

    // Automation depth should be separate from capture depth
    expect(body.checks.queue.depth).toBeGreaterThanOrEqual(0)
    expect(body.checks.queue.captureDepth).toBeGreaterThanOrEqual(0)
    expect(body.checks.queue.totalDepth).toBe(
      body.checks.queue.depth + body.checks.queue.captureDepth,
    )
  })
})
