import { execFileSync } from 'node:child_process'
import { existsSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test } from '@playwright/test'

import type { AuthResult } from './support/authSession'
import { attachSessionToPage } from './support/authSession'

const DEFAULT_SETUP_TIMEOUT_MS = 120_000

function isAppRoot(candidateDir: string): boolean {
  const hasPackageJson = existsSync(path.join(candidateDir, 'package.json'))
  const hasDemoSeed = existsSync(path.join(candidateDir, 'scripts', 'demo-seed.mjs'))
  const hasDemoRun = existsSync(path.join(candidateDir, 'scripts', 'demo-run.mjs'))
  return hasPackageJson && hasDemoSeed && hasDemoRun
}

function parseSetupTimeoutMs(value: string | undefined): number {
  const parsed = Number(value)
  if (Number.isFinite(parsed) && parsed > 0) {
    return Math.floor(parsed)
  }
  return DEFAULT_SETUP_TIMEOUT_MS
}

function resolveAppRoot(startDir: string): string {
  const cwd = process.cwd()
  if (isAppRoot(cwd)) {
    return cwd
  }

  const fromSpecDir = path.resolve(startDir, '..', '..')
  if (isAppRoot(fromSpecDir)) {
    return fromSpecDir
  }

  let current = startDir

  while (current !== path.dirname(current)) {
    if (isAppRoot(current)) {
      return current
    }

    current = path.dirname(current)
  }

  if (isAppRoot(current)) {
    return current
  }

  throw new Error('Unable to resolve frontend/taskdeck-web app root for stakeholder demo setup.')
}

function runSetupScript({
  appRoot,
  scriptArgs,
  env,
  timeoutMs,
}: {
  appRoot: string
  scriptArgs: string[]
  env: NodeJS.ProcessEnv
  timeoutMs: number
}): void {
  try {
    execFileSync(process.execPath, scriptArgs, {
      cwd: appRoot,
      stdio: 'inherit',
      env,
      timeout: timeoutMs,
      killSignal: 'SIGTERM',
    })
  } catch (err) {
    const error = err as NodeJS.ErrnoException
    if (error.code === 'ETIMEDOUT') {
      throw new Error(
        `Timed out after ${timeoutMs}ms running setup command: node ${scriptArgs.join(' ')} (cwd=${appRoot})`,
      )
    }

    throw err
  }
}

/**
 * Stakeholder demo recorder.
 *
 * Intentionally opt-in so default CI/e2e runs are unaffected.
 *
 * PowerShell:
 *   $env:TASKDECK_RUN_DEMO='1'
 *   npx playwright test tests/e2e/stakeholder-demo.spec.ts --headed
 */

test.use({
  video: 'on',
  trace: 'on',
  screenshot: 'on',
})

test.describe('Stakeholder demo recorder', () => {
  test.setTimeout(180_000)
  test.skip(process.env.TASKDECK_RUN_DEMO !== '1', 'Set TASKDECK_RUN_DEMO=1 to run this opt-in spec.')

  test.beforeAll(() => {
    const __filename = fileURLToPath(import.meta.url)
    const __dirname = path.dirname(__filename)
    const appRoot = resolveAppRoot(__dirname)
    const setupTimeoutMs = parseSetupTimeoutMs(process.env.TASKDECK_DEMO_SETUP_TIMEOUT_MS)

    const apiBaseUrl = process.env.TASKDECK_E2E_API_BASE_URL || 'http://localhost:5000/api'
    const uiBaseUrl =
      process.env.TASKDECK_E2E_FRONTEND_BASE_URL ||
      process.env.TASKDECK_UI_BASE_URL ||
      'http://localhost:5173'
    const setupEnv = {
      ...process.env,
      TASKDECK_API_BASE_URL: apiBaseUrl,
      TASKDECK_API_BASE: apiBaseUrl,
      TASKDECK_UI_BASE: uiBaseUrl,
      TASKDECK_UI_BASE_URL: uiBaseUrl,
    }

    runSetupScript({
      appRoot,
      scriptArgs: ['scripts/demo-seed.mjs'],
      env: setupEnv,
      timeoutMs: setupTimeoutMs,
    })

    runSetupScript({
      appRoot,
      scriptArgs: ['scripts/demo-run.mjs', 'engineering-sprint'],
      env: setupEnv,
      timeoutMs: setupTimeoutMs,
    })
  })

  test('captures guided stakeholder clickthrough', async ({ page, request }, testInfo) => {
    const apiBaseUrl = process.env.TASKDECK_E2E_API_BASE_URL || 'http://localhost:5000/api'
    const demoUsername = process.env.TASKDECK_DEMO_USERNAME || 'demo'
    const demoPassword = process.env.TASKDECK_DEMO_PASSWORD || 'demo123'

    const loginResponse = await request.post(`${apiBaseUrl}/auth/login`, {
      data: {
        usernameOrEmail: demoUsername,
        password: demoPassword,
      },
    })

    expect(loginResponse.ok()).toBeTruthy()
    const auth = (await loginResponse.json()) as AuthResult
    await attachSessionToPage(page, auth)

    await page.addInitScript(() => {
      localStorage.setItem('taskdeck_feature_flags', JSON.stringify({
        newShell: true,
        newAuth: true,
        newAutomation: true,
        newAccess: true,
        newActivity: true,
        newOps: true,
        newArchive: true,
      }))
    })

    await page.goto('/workspace/boards')
    await expect(page.getByRole('heading', { name: /^(My Boards|Boards)$/ })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('01-boards.png'), fullPage: true })

    const captureLoopBoardCard = page.locator('div.cursor-pointer').filter({ hasText: 'DEMO: Capture Loop' }).first()
    await expect(captureLoopBoardCard).toBeVisible()
    await captureLoopBoardCard.click()
    await expect(page.getByRole('heading', { name: 'DEMO: Capture Loop' })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('02-capture-board.png'), fullPage: true })

    const firstCard = page.locator('[data-card-id]').first()
    if (await firstCard.count()) {
      await firstCard.click()
      const cardDialog = page.getByRole('dialog')
      await expect(cardDialog).toBeVisible()
      await page.screenshot({ path: testInfo.outputPath('03-card-modal.png'), fullPage: true })
      await page.keyboard.press('Escape')
    }

    await page.getByRole('link', { name: 'Inbox' }).click()
    await expect(page.getByRole('heading', { name: 'Inbox' })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('04-inbox.png'), fullPage: true })

    await page.getByRole('link', { name: 'Automations' }).click()
    await expect(page.getByRole('heading', { name: 'Automations' })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('05-automations-proposals.png'), fullPage: true })

    await page.getByRole('button', { name: 'Queue' }).click()
    await expect(page.getByRole('button', { name: /New Request/ })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('06-automations-queue.png'), fullPage: true })

    await page.getByRole('button', { name: /New Request/ }).click()
    const requestComposer = page.locator('textarea.td-textarea')
    await expect(requestComposer).toBeVisible()
    await requestComposer.fill('list pending proposals')
    const submitRequestButton = page.getByRole('button', { name: 'Submit Request' })
    await expect(submitRequestButton).toBeVisible()
    await submitRequestButton.click()
    await expect(requestComposer).toBeHidden()
    await page.screenshot({ path: testInfo.outputPath('07-queue-submitted.png'), fullPage: true })

    await page.getByRole('link', { name: 'Ops' }).click()
    await expect(page.getByRole('heading', { name: 'Ops Console' })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('08-ops.png'), fullPage: true })

    await page.getByRole('link', { name: 'Activity' }).click()
    await expect(page.getByRole('heading', { name: 'Activity' })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('09-activity.png'), fullPage: true })

    await page.getByRole('link', { name: 'Notifications' }).click()
    await expect(page.getByRole('heading', { name: 'Notifications' })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('10-notifications.png'), fullPage: true })
  })
})

