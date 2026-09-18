import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const workflowPath = fileURLToPath(new URL('../../.github/workflows/reusable-e2e-smoke.yml', import.meta.url))
const workflowLintPath = fileURLToPath(new URL('../../.github/workflows/ci-extended.yml', import.meta.url))
const requiredConfigPath = fileURLToPath(new URL('../../frontend/taskdeck-web/playwright.required.config.ts', import.meta.url))
const mobileResponsiveSpecPath = fileURLToPath(new URL('../../frontend/taskdeck-web/tests/e2e/mobile-responsive.spec.ts', import.meta.url))

async function loadWorkflow() {
  return readFile(workflowPath, 'utf8')
}

function stepBlock(workflow, stepName) {
  const marker = `      - name: ${stepName}`
  const start = workflow.indexOf(marker)
  assert.notEqual(start, -1, `Missing workflow step: ${stepName}`)

  const nextStep = workflow.indexOf('\n      - name:', start + marker.length)
  return workflow.slice(start, nextStep === -1 ? workflow.length : nextStep)
}

test('bounds the required E2E Smoke job and preserves the smoke-test ceiling', async () => {
  const workflow = await loadWorkflow()

  assert.match(
    workflow,
    /e2e-smoke:\r?\n\s+name: E2E Smoke\r?\n\s+runs-on: ubuntu-latest\r?\n\s+timeout-minutes: 35\r?\n\s+steps:/,
  )
  assert.match(
    workflow,
    /- name: Run Playwright smoke tests\r?\n\s+timeout-minutes: 12\r?\n[\s\S]*?npx playwright test --config=playwright\.required\.config\.ts --reporter=line/,
  )
})

test('keeps the required config bounded to desktop Chromium and one mobile geometry journey', async () => {
  const [config, mobileResponsiveSpec] = await Promise.all([
    readFile(requiredConfigPath, 'utf8'),
    readFile(mobileResponsiveSpecPath, 'utf8'),
  ])
  const journeyTitle = '@mobile card editing modal follows a contracted visual viewport'

  assert.match(
    config,
    /baseConfig\.projects\?\.find\(\(project\) => project\.name === 'chromium'\)/,
  )
  assert.match(config, /name: 'mobile-required'/)
  assert.match(config, /devices\['Pixel 7'\]/)
  assert.match(config, /grep: \/@mobile card editing modal follows a contracted visual viewport\//)
  assert.match(config, /projects: \[desktopChromium, requiredMobileGeometry\]/)
  assert.equal(
    mobileResponsiveSpec.split(journeyTitle).length,
    2,
    'the required mobile selector must continue to identify exactly one browser journey',
  )
})

test('keeps Playwright-managed Chromium bootstrap explicit and separately bounded', async () => {
  const workflow = await loadWorkflow()
  const dependencyStep = stepBlock(workflow, 'Install Playwright browser dependencies')
  const browserStep = stepBlock(workflow, 'Install Playwright Chromium browser')

  assert.match(dependencyStep, /timeout-minutes: 10/)
  assert.match(dependencyStep, /working-directory: frontend\/taskdeck-web/)
  assert.match(dependencyStep, /run: npx playwright install-deps chromium/)

  assert.match(browserStep, /timeout-minutes: 5/)
  assert.match(browserStep, /working-directory: frontend\/taskdeck-web/)
  assert.match(browserStep, /run: npx playwright install chromium/)

  assert.ok(
    workflow.indexOf('      - name: Install Playwright browser dependencies') < workflow.indexOf('      - name: Install Playwright Chromium browser'),
    'OS dependencies must be installed before the browser',
  )
  assert.ok(
    workflow.indexOf('      - name: Cache Playwright browsers') < workflow.indexOf('      - name: Install Playwright browser dependencies'),
    'Browser cache must remain before the explicit dependency install',
  )
  assert.doesNotMatch(workflow, /npx playwright install --with-deps chromium/)
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
