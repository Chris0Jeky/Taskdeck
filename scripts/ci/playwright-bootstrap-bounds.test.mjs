import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const workflowPath = fileURLToPath(new URL('../../.github/workflows/reusable-e2e-smoke.yml', import.meta.url))
const workflowLintPath = fileURLToPath(new URL('../../.github/workflows/ci-extended.yml', import.meta.url))
const packageLockPath = fileURLToPath(new URL('../../frontend/taskdeck-web/package-lock.json', import.meta.url))

async function loadWorkflow() {
  return readFile(workflowPath, 'utf8')
}

async function lockedPlaywrightVersion() {
  const packageLock = JSON.parse(await readFile(packageLockPath, 'utf8'))
  const version = packageLock.packages?.['node_modules/@playwright/test']?.version
  assert.match(version ?? '', /^\d+\.\d+\.\d+$/, 'package-lock must pin @playwright/test exactly')
  return version
}

function stepBlock(workflow, stepName) {
  const marker = `      - name: ${stepName}`
  const start = workflow.indexOf(marker)
  assert.notEqual(start, -1, `Missing workflow step: ${stepName}`)

  const nextStep = workflow.indexOf('\n      - name:', start + marker.length)
  return workflow.slice(start, nextStep === -1 ? workflow.length : nextStep)
}

test('runs required E2E Smoke in the exact lockfile-matched Playwright image', async () => {
  const [workflow, playwrightVersion] = await Promise.all([
    loadWorkflow(),
    lockedPlaywrightVersion(),
  ])
  const escapedVersion = playwrightVersion.replaceAll('.', '\\.')

  assert.match(
    workflow,
    new RegExp(
      String.raw`e2e-smoke:\r?\n\s+name: E2E Smoke\r?\n\s+runs-on: ubuntu-latest\r?\n\s+timeout-minutes: 35\r?\n\s+container:\r?\n\s+image: mcr\.microsoft\.com/playwright:v${escapedVersion}-noble\r?\n\s+env:\r?\n\s+PLAYWRIGHT_BROWSERS_PATH: /ms-playwright\r?\n\s+steps:`,
    ),
  )
  assert.match(
    workflow,
    /- name: Run Playwright smoke tests\r?\n\s+timeout-minutes: 12\r?\n[\s\S]*?npx playwright test --project=chromium --reporter=line/,
  )
})

test('uses preinstalled Chromium without apt, browser downloads, or a redundant browser cache', async () => {
  const workflow = await loadWorkflow()
  const smokeJob = workflow.slice(workflow.indexOf('  e2e-smoke:'))
  const verificationStep = stepBlock(workflow, 'Verify Playwright container browser')

  assert.doesNotMatch(smokeJob, /playwright install-deps|playwright install(?:\s+--with-deps)?\s+chromium/)
  assert.doesNotMatch(smokeJob, /Cache Playwright browsers|\.cache\/ms-playwright/)
  assert.doesNotMatch(smokeJob, /\bapt(?:-get)?\b/)

  assert.match(verificationStep, /timeout-minutes: 1/)
  assert.match(verificationStep, /working-directory: frontend\/taskdeck-web/)
  assert.match(verificationStep, /chromium\.executablePath\(\)/)
  assert.match(verificationStep, /existsSync\(executable\)/)

  assert.ok(
    workflow.indexOf('      - name: Install frontend dependencies') < workflow.indexOf('      - name: Verify Playwright container browser'),
    'npm ci must install the lockfile-matched Playwright package before browser verification',
  )
  assert.ok(
    workflow.indexOf('      - name: Verify Playwright container browser') < workflow.indexOf('      - name: Run Playwright smoke tests'),
    'the preinstalled browser must be verified before the smoke journey starts',
  )
})

test('does not add automatic retries and runs this contract in workflow lint', async () => {
  const workflow = await loadWorkflow()
  const smokeJob = workflow.slice(workflow.indexOf('  e2e-smoke:'))
  const workflowLint = await readFile(workflowLintPath, 'utf8')

  assert.doesNotMatch(smokeJob, /--retries?\b|\bretry\s*:/i)
  assert.doesNotMatch(smokeJob, /^\s*(?:for|while)\b/m)
  assert.match(
    workflowLint,
    /- name: Test Playwright bootstrap bounds contract\r?\n\s+run: node --test scripts\/ci\/playwright-bootstrap-bounds\.test\.mjs/,
  )
})
