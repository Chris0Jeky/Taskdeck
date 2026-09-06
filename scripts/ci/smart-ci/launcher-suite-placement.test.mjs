import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { test } from 'node:test'

const frontendUnitWorkflowUrl = new URL(
  '../../../.github/workflows/reusable-frontend-unit.yml',
  import.meta.url,
)

const LAUNCHER_STEP_NAME = 'Run source launcher regression suite'
const UNCONDITIONAL_STEP_NAMES = [
  'Run frontend lint',
  'Run frontend typecheck',
  'Run frontend build',
  'Run frontend tests with coverage thresholds',
]

function workflowLines(workflow) {
  return workflow.replaceAll('\r\n', '\n').split('\n')
}

function extractFrontendUnitJob(workflow) {
  const lines = workflowLines(workflow)
  const jobsIndex = lines.findIndex((line) => line.trim() === 'jobs:')
  assert.notEqual(jobsIndex, -1, 'reusable-frontend-unit.yml is missing the top-level jobs mapping')

  const jobIndex = lines.findIndex(
    (line, index) => index > jobsIndex && /^ {2}frontend-unit:\s*$/.test(line),
  )
  assert.notEqual(jobIndex, -1, 'reusable-frontend-unit.yml is missing the frontend-unit job')

  const nextJobIndex = lines.findIndex(
    (line, index) => index > jobIndex && /^ {2}[A-Za-z0-9_-]+:\s*$/.test(line),
  )
  return lines.slice(jobIndex, nextJobIndex === -1 ? lines.length : nextJobIndex)
}

function extractMatrixOperatingSystems(jobLines) {
  const osIndex = jobLines.findIndex((line) => /^ {8}os:\s*$/.test(line))
  assert.notEqual(osIndex, -1, 'frontend-unit is missing its matrix.os list')

  const operatingSystems = []
  for (const line of jobLines.slice(osIndex + 1)) {
    const item = line.match(/^ {10}-\s*([A-Za-z0-9_.-]+)\s*$/)
    if (item) {
      operatingSystems.push(item[1])
      continue
    }
    if (line.trim() === '') continue
    break
  }
  return operatingSystems
}

function extractSteps(jobLines) {
  const stepsIndex = jobLines.findIndex((line) => /^ {4}steps:\s*$/.test(line))
  assert.notEqual(stepsIndex, -1, 'frontend-unit is missing its steps list')

  const steps = []
  let current = null
  for (const line of jobLines.slice(stepsIndex + 1)) {
    if (/^ {6}- /.test(line)) {
      const name = line.match(/^ {6}-\s*name:\s*(.+?)\s*$/)
      current = { name: name ? name[1] : null, body: [line] }
      steps.push(current)
      continue
    }
    if (current) current.body.push(line)
  }
  return steps.map((step) => ({ name: step.name, body: step.body.join('\n') }))
}

function findStep(steps, name) {
  const step = steps.find((candidate) => candidate.name === name)
  assert.ok(step, `frontend-unit is missing the "${name}" step`)
  return step
}

function extractStepCondition(step) {
  const condition = step.body.match(/^ {8}if:\s*(.+?)\s*$/m)
  return condition ? condition[1] : null
}

test('the launcher regression suite runs on the Linux leg only (#2331, SC-3)', async () => {
  const workflow = await readFile(frontendUnitWorkflowUrl, 'utf8')
  const steps = extractSteps(extractFrontendUnitJob(workflow))
  const launcherStep = findStep(steps, LAUNCHER_STEP_NAME)

  assert.match(
    launcherStep.body,
    /^ {8}run: node --test --test-concurrency=1 --test-timeout=30000 scripts\/ci\/dev-up\.test\.mjs$/m,
    'the launcher step must still run scripts/ci/dev-up.test.mjs',
  )
  assert.equal(
    extractStepCondition(launcherStep),
    "runner.os == 'Linux'",
    'the launcher step must be gated to the Linux leg',
  )
})

test('the frontend-unit matrix still covers both hosted operating systems', async () => {
  const workflow = await readFile(frontendUnitWorkflowUrl, 'utf8')
  const operatingSystems = extractMatrixOperatingSystems(extractFrontendUnitJob(workflow))

  assert.deepEqual([...operatingSystems].sort(), ['ubuntu-latest', 'windows-latest'])
})

test('lint, typecheck, build and coverage stay unconditional on both legs', async () => {
  const workflow = await readFile(frontendUnitWorkflowUrl, 'utf8')
  const steps = extractSteps(extractFrontendUnitJob(workflow))

  for (const name of UNCONDITIONAL_STEP_NAMES) {
    assert.equal(
      extractStepCondition(findStep(steps, name)),
      null,
      `"${name}" must carry no OS condition`,
    )
  }
})
