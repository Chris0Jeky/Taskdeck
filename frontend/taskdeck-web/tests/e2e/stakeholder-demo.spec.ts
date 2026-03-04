import { execFileSync } from 'node:child_process'
import { existsSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test } from '@playwright/test'

import type { AuthResult } from './support/authSession'
import { attachSessionToPage } from './support/authSession'

function resolveAppRoot(startDir: string): string {
  let current = startDir

  while (current !== path.dirname(current)) {
    const hasPackageJson = existsSync(path.join(current, 'package.json'))
    const hasDemoSeed = existsSync(path.join(current, 'scripts', 'demo-seed.mjs'))
    const hasDemoRun = existsSync(path.join(current, 'scripts', 'demo-run.mjs'))

    if (hasPackageJson && hasDemoSeed && hasDemoRun) {
      return current
    }

    current = path.dirname(current)
  }

  const hasPackageJson = existsSync(path.join(current, 'package.json'))
  const hasDemoSeed = existsSync(path.join(current, 'scripts', 'demo-seed.mjs'))
  const hasDemoRun = existsSync(path.join(current, 'scripts', 'demo-run.mjs'))
  if (hasPackageJson && hasDemoSeed && hasDemoRun) {
    return current
  }

  throw new Error('Unable to resolve frontend/taskdeck-web app root for stakeholder demo setup.')
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

    const apiBaseUrl = process.env.TASKDECK_E2E_API_BASE_URL || 'http://localhost:5000/api'
    const uiBaseUrl =
      process.env.TASKDECK_E2E_FRONTEND_BASE_URL ||
      process.env.TASKDECK_UI_BASE_URL ||
      'http://localhost:5173'

    execFileSync('node', ['scripts/demo-seed.mjs'], {
      cwd: appRoot,
      stdio: 'inherit',
      env: {
        ...process.env,
        TASKDECK_API_BASE_URL: apiBaseUrl,
        TASKDECK_API_BASE: apiBaseUrl,
        TASKDECK_UI_BASE: uiBaseUrl,
        TASKDECK_UI_BASE_URL: uiBaseUrl,
      },
    })

    execFileSync('node', ['scripts/demo-run.mjs', 'engineering-sprint'], {
      cwd: appRoot,
      stdio: 'inherit',
      env: {
        ...process.env,
        TASKDECK_API_BASE_URL: apiBaseUrl,
        TASKDECK_API_BASE: apiBaseUrl,
        TASKDECK_UI_BASE: uiBaseUrl,
        TASKDECK_UI_BASE_URL: uiBaseUrl,
      },
    })
  })

  test('captures guided stakeholder clickthrough', async ({ page, request }, testInfo) => {
    const apiBaseUrl = process.env.TASKDECK_E2E_API_BASE_URL || 'http://localhost:5000/api'

    const loginResponse = await request.post(`${apiBaseUrl}/auth/login`, {
      data: {
        usernameOrEmail: 'demo',
        password: 'demo123',
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
    await expect(page.getByRole('heading', { name: 'Boards' })).toBeVisible()
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
    await requestComposer.fill('list pending proposals')
    await page.getByRole('button', { name: 'Submit Request' }).click()
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

