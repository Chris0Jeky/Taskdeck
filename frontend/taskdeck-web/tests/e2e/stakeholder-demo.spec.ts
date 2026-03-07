import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'

import { parseTrueishEnv } from '../../scripts/demo-shared.mjs'
import { resolveScenarioSelectedBoardName } from '../../scripts/demo-scenario-defaults.mjs'
import type { AuthResult } from './support/authSession'
import { attachSessionToPage } from './support/authSession'

const DEFAULT_SETUP_TIMEOUT_MS = 120_000
const DEFAULT_SETUP_SCRIPT_MAX_BUFFER_BYTES = 64 * 1024 * 1024
const MIN_SETUP_SCRIPT_MAX_BUFFER_BYTES = 1024 * 1024
const DEFAULT_PLAYWRIGHT_TEST_TIMEOUT_MS = 180_000
const REQUIRED_WALKTHROUGH_FEATURE_FLAGS = ['Activity & Audit Views', 'Ops Console']

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

function parseSetupScriptMaxBufferBytes(value: string | undefined): number {
  const parsed = Number(value)
  if (!Number.isFinite(parsed) || !Number.isInteger(parsed) || parsed < MIN_SETUP_SCRIPT_MAX_BUFFER_BYTES) {
    return DEFAULT_SETUP_SCRIPT_MAX_BUFFER_BYTES
  }
  return parsed
}

function parseOptionalPositiveInteger(value: string | undefined, fallback: number): number {
  const parsed = Number(value)
  if (!Number.isFinite(parsed) || !Number.isInteger(parsed) || parsed <= 0) {
    return fallback
  }
  return parsed
}

function parseOptionalNonNegativeInteger(value: string | undefined, fallback: number): number {
  const parsed = Number(value)
  if (!Number.isFinite(parsed) || !Number.isInteger(parsed) || parsed < 0) {
    return fallback
  }
  return parsed
}

function computeStakeholderDemoTimeoutMs(): number {
  const setupTimeoutMs = parseSetupTimeoutMs(process.env.TASKDECK_DEMO_SETUP_TIMEOUT_MS)
  const scenarioId = (process.env.TASKDECK_DEMO_SCENARIO || 'engineering-sprint').trim()
  const skipSeed = parseTrueishEnv(process.env.TASKDECK_DEMO_SKIP_SEED)
  const autopilotTurns = parseOptionalPositiveInteger(process.env.TASKDECK_DEMO_AUTOPILOT_TURNS, 0)
  const snapshotPath = (process.env.TASKDECK_DEMO_SNAPSHOT_PATH || '').trim()

  let setupSteps = 0
  if (!skipSeed) setupSteps += 1
  if (scenarioId) setupSteps += 1
  if (autopilotTurns > 0) setupSteps += 1
  if (snapshotPath) setupSteps += 1

  const walkthroughBudgetMs = 120_000
  const setupBudgetMs = setupSteps * setupTimeoutMs
  return Math.max(DEFAULT_PLAYWRIGHT_TEST_TIMEOUT_MS, setupBudgetMs + walkthroughBudgetMs)
}

async function ensureWalkthroughFeatureFlagsEnabled(page: Page) {
  await page.goto('/workspace/settings/profile')
  await expect(page.getByRole('heading', { name: 'Settings' })).toBeVisible()

  for (const flagLabel of REQUIRED_WALKTHROUGH_FEATURE_FLAGS) {
    const flagToggle = page.getByLabel(flagLabel)
    await expect(flagToggle).toBeVisible()
    if (!(await flagToggle.isChecked())) {
      await flagToggle.check()
    }
  }
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
  label,
  logPath,
  echoOutput,
}: {
  appRoot: string
  scriptArgs: string[]
  env: NodeJS.ProcessEnv
  timeoutMs: number
  label: string
  logPath: string | null
  echoOutput: boolean
}): void {
  const useStreamingConsoleOutput = echoOutput && !logPath
  const result = useStreamingConsoleOutput
    ? spawnSync(process.execPath, scriptArgs, {
        cwd: appRoot,
        env,
        stdio: 'inherit',
        timeout: timeoutMs,
        killSignal: 'SIGTERM',
      })
    : spawnSync(process.execPath, scriptArgs, {
        cwd: appRoot,
        env,
        encoding: 'utf8',
        maxBuffer: parseSetupScriptMaxBufferBytes(env.TASKDECK_DEMO_SETUP_MAX_BUFFER_BYTES),
        timeout: timeoutMs,
        killSignal: 'SIGTERM',
      })

  const combined = useStreamingConsoleOutput ? '' : `${result.stdout || ''}${result.stderr || ''}`
  if (logPath) {
    writeFileSync(logPath, combined, 'utf8')
  }

  if (!useStreamingConsoleOutput && echoOutput && combined.trim()) {
    process.stdout.write(combined)
  }

  if (result.error) {
    const error = result.error as NodeJS.ErrnoException
    if (error.code === 'ETIMEDOUT') {
      throw new Error(
        `Timed out after ${timeoutMs}ms running setup command: node ${scriptArgs.join(' ')} (cwd=${appRoot})`,
      )
    }

    const command = `node ${scriptArgs.join(' ')}`
    const statusInfo = `status=${String(result.status ?? 'null')}, signal=${String(result.signal ?? 'null')}`
    const messageLines = [
      `Demo bootstrap failed at ${label} due to a spawn error.`,
      `Command: ${command}`,
      `CWD: ${appRoot}`,
      `Result: ${statusInfo}`,
      `Underlying error: ${error.message}${error.code ? ` (code=${error.code})` : ''}`,
    ]
    if (combined.trim()) {
      messageLines.push('')
      messageLines.push(combined)
    }
    throw new Error(messageLines.join('\n'))
  }

  if ((result.status ?? 1) !== 0) {
    throw new Error(`Demo bootstrap failed at ${label} (exit=${result.status}).\n\n${combined}`)
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
  test.setTimeout(computeStakeholderDemoTimeoutMs())
  test.skip(!parseTrueishEnv(process.env.TASKDECK_RUN_DEMO), 'Set TASKDECK_RUN_DEMO=1 to run this opt-in spec.')

  test.beforeAll(async () => {
    const __filename = fileURLToPath(import.meta.url)
    const __dirname = path.dirname(__filename)
    const appRoot = resolveAppRoot(__dirname)
    const setupTimeoutMs = parseSetupTimeoutMs(process.env.TASKDECK_DEMO_SETUP_TIMEOUT_MS)

    const apiBaseUrl = process.env.TASKDECK_E2E_API_BASE_URL || 'http://localhost:5000/api'
    const uiBaseUrl =
      process.env.TASKDECK_E2E_FRONTEND_BASE_URL ||
      process.env.TASKDECK_UI_BASE_URL ||
      'http://localhost:5173'

    const isDirector = parseTrueishEnv(process.env.TASKDECK_DEMO_DIRECTOR)
    const artifactDir = (process.env.TASKDECK_DEMO_ARTIFACT_DIR || '').trim() || null
    const logsDir = artifactDir ? path.join(artifactDir, 'logs') : null

    const scenarioId = (process.env.TASKDECK_DEMO_SCENARIO || 'engineering-sprint').trim()
    const skipSeed = parseTrueishEnv(process.env.TASKDECK_DEMO_SKIP_SEED)
    const skipLlm = parseTrueishEnv(process.env.TASKDECK_DEMO_SKIP_LLM)

    const autopilotTurns = parseOptionalPositiveInteger(process.env.TASKDECK_DEMO_AUTOPILOT_TURNS, 0)
    const walkthroughBoardName = (process.env.TASKDECK_DEMO_WALKTHROUGH_BOARD || '').trim()
    const autopilotBoardOverride = (process.env.TASKDECK_DEMO_AUTOPILOT_BOARD || '').trim()
    const autopilotBoardName = await resolveScenarioSelectedBoardName({
      scenarioIdOrPath: scenarioId,
      explicitBoardName: walkthroughBoardName || autopilotBoardOverride,
    })
    const autopilotLoop = (process.env.TASKDECK_DEMO_AUTOPILOT_LOOP || 'mixed').trim() || 'mixed'
    const autopilotBrain = (process.env.TASKDECK_DEMO_AUTOPILOT_BRAIN || 'heuristic').trim() || 'heuristic'
    const autopilotIntervalMs = parseOptionalNonNegativeInteger(process.env.TASKDECK_DEMO_AUTOPILOT_INTERVAL_MS, 700)
    const autopilotSeed = (process.env.TASKDECK_DEMO_AUTOPILOT_RNG_SEED || '').trim() || null

    const snapshotPath = (process.env.TASKDECK_DEMO_SNAPSHOT_PATH || '').trim() || null

    if (logsDir) {
      mkdirSync(logsDir, { recursive: true })
    }

    const setupEnv = {
      ...process.env,
      TASKDECK_API_BASE_URL: apiBaseUrl,
      TASKDECK_API_BASE: apiBaseUrl,
      TASKDECK_UI_BASE: uiBaseUrl,
      TASKDECK_UI_BASE_URL: uiBaseUrl,
      TASKDECK_E2E_API_BASE_URL: apiBaseUrl,
      TASKDECK_E2E_FRONTEND_BASE_URL: uiBaseUrl,
    }

    const runScript = (label: string, scriptArgs: string[], logFileName: string | null) => {
      const logPath = logFileName && logsDir ? path.join(logsDir, logFileName) : null
      runSetupScript({
        appRoot,
        scriptArgs,
        env: setupEnv,
        timeoutMs: setupTimeoutMs,
        label,
        logPath,
        echoOutput: !isDirector,
      })
    }

    if (!skipSeed) {
      runScript('demo-seed', ['scripts/demo-seed.mjs'], isDirector ? 'seed.log' : null)
    }

    if (scenarioId) {
      const runArgs = ['scripts/demo-run.mjs', scenarioId, '--clean']
      if (skipLlm) {
        runArgs.push('--skip-llm')
      }
      runScript('demo-run', runArgs, isDirector ? 'scenario.log' : null)
    }

    if (autopilotTurns > 0) {
      const autopilotArgs = [
        'scripts/demo-autopilot.mjs',
        '--board',
        autopilotBoardName,
        '--turns',
        String(autopilotTurns),
        '--interval-ms',
        String(autopilotIntervalMs),
        '--loop',
        autopilotLoop,
        '--brain',
        autopilotBrain,
      ]
      if (autopilotSeed) {
        autopilotArgs.push('--rng-seed', autopilotSeed)
      }

      runScript('demo-autopilot', autopilotArgs, isDirector ? 'autopilot.log' : null)
    }

    if (snapshotPath) {
      runScript('demo-snapshot', ['scripts/demo-snapshot.mjs', '--out', snapshotPath], isDirector ? 'snapshot.log' : null)
    }
  })

  test('captures guided stakeholder clickthrough', async ({ page, request }, testInfo) => {
    const apiBaseUrl = process.env.TASKDECK_E2E_API_BASE_URL || 'http://localhost:5000/api'
    const demoUsername = process.env.TASKDECK_DEMO_USERNAME || 'demo'
    const demoPassword = process.env.TASKDECK_DEMO_PASSWORD || 'demo123'
    const scenarioId = (process.env.TASKDECK_DEMO_SCENARIO || 'engineering-sprint').trim()
    const scenarioBoardName = await resolveScenarioSelectedBoardName({
      scenarioIdOrPath: scenarioId,
      explicitBoardName:
        (process.env.TASKDECK_DEMO_WALKTHROUGH_BOARD || '').trim() ||
        (process.env.TASKDECK_DEMO_AUTOPILOT_BOARD || '').trim(),
    })

    const loginResponse = await request.post(`${apiBaseUrl}/auth/login`, {
      data: {
        usernameOrEmail: demoUsername,
        password: demoPassword,
      },
    })

    expect(loginResponse.ok()).toBeTruthy()
    const auth = (await loginResponse.json()) as AuthResult
    await attachSessionToPage(page, auth)
    await ensureWalkthroughFeatureFlagsEnabled(page)

    await page.goto('/workspace/boards')
    await expect(page.getByRole('heading', { name: /^(My Boards|Boards)$/ })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('01-boards.png'), fullPage: true })

    const scenarioBoardCard = page.locator('div.cursor-pointer').filter({ hasText: scenarioBoardName }).first()
    await expect(scenarioBoardCard).toBeVisible()
    await scenarioBoardCard.click()
    await expect(page.getByRole('heading', { name: scenarioBoardName })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('02-capture-board.png'), fullPage: true })

    const firstCard = page.locator('[data-card-id]').first()
    await expect(firstCard, `Scenario board "${scenarioBoardName}" should contain at least one seeded card.`).toBeVisible()
    await firstCard.click()
    const cardEditorHeading = page.getByRole('heading', { name: 'Edit Card' })
    await expect(cardEditorHeading).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('03-card-modal.png'), fullPage: true })
    await page.keyboard.press('Escape')

    await page.getByRole('link', { name: 'Inbox' }).click()
    await expect(page.getByRole('heading', { name: 'Inbox' })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('04-inbox.png'), fullPage: true })

    await page.getByRole('link', { name: 'Review' }).click()
    await expect(page.getByRole('heading', { name: 'Review', exact: true })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('05-automations-proposals.png'), fullPage: true })

    await page.getByRole('button', { name: 'Open Queue (Advanced)' }).click()
    await expect(page.getByRole('heading', { name: 'Automation Queue', exact: true })).toBeVisible()
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
