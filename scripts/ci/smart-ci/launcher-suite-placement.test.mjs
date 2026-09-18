import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'
import { test } from 'node:test'

const frontendUnitWorkflowUrl = new URL('../../../.github/workflows/reusable-frontend-unit.yml', import.meta.url)
const policyUrl = new URL('../../../ci/policy.v1.json', import.meta.url)
const ciScriptsDirUrl = new URL('../', import.meta.url)
const LAUNCHER_STEP_NAME = 'Run source launcher regression suite'
// #3165: the step runs the whole `dev-up*.test.mjs` family, not just the launcher entry point, so
// the fixture-teardown regressions are required-CI coverage rather than an opt-in local run.
const LAUNCHER_SUITE_GLOB = 'scripts/ci/dev-up*.test.mjs'
const LAUNCHER_SUITE_FILE_PATTERN = /^dev-up.*\.test\.mjs$/
const UNCONDITIONAL_STEP_NAMES = [
  'Run frontend lint', 'Run frontend typecheck', 'Run frontend build', 'Run frontend tests with coverage thresholds',
]
function extractJob(workflow, id) {
  const lines = workflow.replaceAll('\r\n', '\n').split('\n')
  const jobsIndex = lines.findIndex(line => line.trim() === 'jobs:')
  assert.notEqual(jobsIndex, -1)
  const jobIndex = lines.findIndex((line, i) => i > jobsIndex && line === `  ${id}:`)
  assert.notEqual(jobIndex, -1, `missing ${id}`)
  const next = lines.findIndex((line, i) => i > jobIndex && /^ {2}[A-Za-z0-9_-]+:\s*$/.test(line))
  return lines.slice(jobIndex, next === -1 ? lines.length : next)
}
function extractSteps(lines) {
  const start = lines.findIndex(line => /^ {4}steps:\s*$/.test(line))
  assert.notEqual(start, -1)
  const steps = []
  for (const line of lines.slice(start + 1)) {
    if (/^ {6}- /.test(line)) steps.push({ name: line.match(/^ {6}-\s*name:\s*(.+?)\s*$/)?.[1], body: [] })
    if (steps.length) steps.at(-1).body.push(line)
  }
  return steps.map(s => ({ name: s.name, body: s.body.join('\n') }))
}
function step(steps, name) { const found = steps.find(s => s.name === name); assert.ok(found, `missing ${name}`); return found }
const condition = s => s.body.match(/^ {8}if:\s*(.+?)\s*$/m)?.[1] ?? null

test('source launcher remains Linux-only with the exact command and step budget (#2331/#2332)', async () => {
  const text = await readFile(frontendUnitWorkflowUrl, 'utf8'), lines = extractJob(text, 'source-launcher')
  assert.ok(lines.includes('    runs-on: ubuntu-latest'))
  assert.ok(!lines.some(line => /^ {4}if:/.test(line)))
  const launcher = step(extractSteps(lines), LAUNCHER_STEP_NAME)
  assert.match(launcher.body, /^ {8}run: node --test --test-concurrency=1 --test-timeout=30000 scripts\/ci\/dev-up\*\.test\.mjs$/m)
  assert.match(launcher.body, /^ {8}timeout-minutes: 10$/m)
  assert.equal(condition(launcher), "runner.os == 'Linux'")
  assert.equal(text.split(LAUNCHER_SUITE_GLOB).length - 1, 1)
})
test('the launcher glob is not vacuous and covers the whole dev-up suite family (#3165)', async () => {
  const suiteFiles = (await readdir(ciScriptsDirUrl)).filter(name => LAUNCHER_SUITE_FILE_PATTERN.test(name)).sort()
  assert.ok(suiteFiles.includes('dev-up.test.mjs'), 'the launcher entry point must stay in the glob')
  assert.deepEqual(
    suiteFiles,
    ['dev-up-fixture-cleanup.test.mjs', 'dev-up-fixture-diagnostics.test.mjs', 'dev-up.test.mjs'],
    'a new scripts/ci/dev-up*.test.mjs file joins the required Linux step: confirm it is bounded before widening this list',
  )
})
test('frontend semantics no longer execute launcher tests or wait for launcher results', async () => {
  const lines = extractJob(await readFile(frontendUnitWorkflowUrl, 'utf8'), 'frontend-unit')
  assert.ok(!lines.some(line => /dev-up\.test|^ {4}needs:/.test(line)))
})
test('frontend-unit retains both hosted operating systems', async () => {
  const lines = extractJob(await readFile(frontendUnitWorkflowUrl, 'utf8'), 'frontend-unit')
  const index = lines.findIndex(line => /^ {8}os:\s*$/.test(line)); assert.notEqual(index, -1)
  const os = []
  for (const line of lines.slice(index + 1)) { const match = line.match(/^ {10}-\s*([A-Za-z0-9_.-]+)\s*$/); if (match) os.push(match[1]); else if (line.trim()) break }
  assert.deepEqual(os.sort(), ['ubuntu-latest', 'windows-latest'])
})
test('lint/typecheck/build/full coverage remain unconditional on both frontend legs', async () => {
  const steps = extractSteps(extractJob(await readFile(frontendUnitWorkflowUrl, 'utf8'), 'frontend-unit'))
  for (const name of UNCONDITIONAL_STEP_NAMES) assert.equal(condition(step(steps, name)), null)
})
test('new launcher checkout has no persisted credentials and no write grants', async () => {
  const text = await readFile(frontendUnitWorkflowUrl, 'utf8'), steps = extractSteps(extractJob(text, 'source-launcher'))
  assert.match(step(steps, 'Checkout').body, /persist-credentials: false/)
  assert.ok(!/\bwrite\b/.test(text)); assert.ok(!extractJob(text, 'source-launcher').some(line => /continue-on-error:/.test(line)))
})
test('canonical lane inherits existing ownership without masking unknown paths', async () => {
  const policy = JSON.parse(await readFile(policyUrl, 'utf8'))
  assert.equal(policy.mode, 'shadow')
  assert.equal(policy.lanes['source-launcher-linux'].checkName, 'Frontend Unit / Source Launcher (Linux)')
  assert.equal(policy.lanes['source-launcher-linux'].runner, 'hostedLinux')
  assert.ok(!policy.pathGroups.some(g => g.id === 'source-launcher-inputs'))
  for (const id of ['backend-domain', 'backend-api', 'frontend-src', 'frontend-e2e', 'scripts-other', 'launchers-windows']) {
    assert.ok(policy.pathGroups.find(g => g.id === id).lanes.includes('source-launcher-linux'))
  }
  assert.ok(!policy.pathGroups.some(g => g.patterns.includes('backend/**') || g.patterns.includes('frontend/**')))
})
