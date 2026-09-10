import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { test } from 'node:test'

const requiredWorkflowUrl = new URL('../../../.github/workflows/ci-required.yml', import.meta.url)
const reusableE2eWorkflowUrl = new URL('../../../.github/workflows/reusable-e2e-smoke.yml', import.meta.url)
const reusableApiWorkflowUrl = new URL('../../../.github/workflows/reusable-api-integration.yml', import.meta.url)
const policyUrl = new URL('../../../ci/policy.v1.json', import.meta.url)

function extractTopLevelJob(workflow, jobName) {
  const lines = workflow.replaceAll('\r\n', '\n').split('\n')
  const jobsIndex = lines.findIndex((line) => line.trim() === 'jobs:')
  assert.notEqual(jobsIndex, -1, 'ci-required.yml is missing the top-level jobs mapping')
  const jobIndex = lines.findIndex((line, index) => index > jobsIndex && line === `  ${jobName}:`)
  assert.notEqual(jobIndex, -1, `ci-required.yml is missing the top-level ${jobName} job`)
  const nextJobIndex = lines.findIndex((line, index) => index > jobIndex && /^  [A-Za-z0-9_-]+:\s*$/.test(line))
  return lines.slice(jobIndex, nextJobIndex === -1 ? lines.length : nextJobIndex).join('\n')
}
function extractNeeds(job) {
  const lines = job.split('\n'), needsIndex = lines.findIndex(line => line.trim() === 'needs:')
  assert.notEqual(needsIndex, -1, 'e2e-smoke is missing its needs list')
  const dependencies = []
  for (const line of lines.slice(needsIndex + 1)) {
    const item = line.match(/^      -\s*([A-Za-z0-9_-]+)\s*$/)
    if (item) dependencies.push(item[1]); else if (line.trim()) break
  }
  return dependencies
}

test('required E2E waits on semantic and cheap control prerequisites', async () => {
  const e2eJob = extractTopLevelJob(await readFile(requiredWorkflowUrl, 'utf8'), 'e2e-smoke')
  assert.deepEqual(extractNeeds(e2eJob).sort(), [
    'api-integration', 'backend-architecture', 'backend-unit', 'docs-governance',
    'frontend-unit', 'migration-validation', 'paper-color-audit', 'release-workflow-contract',
  ])
  // This is a failure barrier, not artifact reuse. The callee still owns setup below.
  // PR-only secret scan must not block push/merge-group through an unconditional needs edge.
  assert.doesNotMatch(e2eJob, /^      - secret-scan\s*$/m)
  assert.match(e2eJob, /^    uses:\s*\.\/\.github\/workflows\/reusable-e2e-smoke\.yml\s*$/m)
})
test('reusable E2E owns its runtime setup and execution', async () => {
  const workflow = await readFile(reusableE2eWorkflowUrl, 'utf8')
  assert.match(workflow, /- name: Checkout[\s\S]*?uses: actions\/checkout@/)
  assert.match(workflow, /- name: Setup \.NET[\s\S]*?uses: actions\/setup-dotnet@/)
  assert.match(workflow, /- name: Setup Node[\s\S]*?uses: actions\/setup-node@/)
  assert.match(workflow, /- name: Install frontend dependencies[\s\S]*?working-directory: frontend\/taskdeck-web[\s\S]*?run: npm ci/)
  assert.match(workflow, /- name: Run Playwright smoke tests[\s\S]*?run: npx playwright test/)
})

test('both complete API platforms qualify while only Linux blocks Linux E2E', async () => {
  const workflow = await readFile(requiredWorkflowUrl, 'utf8')
  const e2e = extractTopLevelJob(workflow, 'e2e-smoke')
  assert.ok(extractNeeds(e2e).includes('api-integration'))
  assert.ok(!extractNeeds(e2e).includes('api-integration-windows'))
  const policy = JSON.parse(await readFile(policyUrl, 'utf8'))
  for (const [id, platform, os] of [['api-integration', 'linux', 'ubuntu-latest'], ['api-integration-windows', 'windows', 'windows-latest']]) {
    const job = extractTopLevelJob(workflow, id)
    assert.match(job, /^    name: API Integration$/m)
    assert.match(job, /^    uses: .\/\.github\/workflows\/reusable-api-integration.yml$/m)
    assert.ok(job.includes(`      platform: ${platform}`))
    assert.doesNotMatch(job, /^    (if|continue-on-error):/m)
    assert.deepEqual(extractNeeds(job).sort(), ['backend-architecture', 'release-workflow-contract'])
    assert.equal(policy.lanes[`api-integration-${platform}`].checkName, `API Integration / API Integration (${os})`)
  }
  const reusable = (await readFile(reusableApiWorkflowUrl, 'utf8')).replaceAll('\r\n', '\n')
  assert.match(reusable, /default: all\n/)
  assert.ok(reusable.includes(`inputs.platform == 'linux' && '["ubuntu-latest"]' || inputs.platform == 'windows' && '["windows-latest"]' || '["ubuntu-latest", "windows-latest"]'`))
  assert.match(reusable, /name: API Integration \(\$\{\{ matrix.os \}\}\)/)
  assert.match(reusable, /fail-fast: false/)
  assert.match(reusable, /run: dotnet test backend\/tests\/Taskdeck.Api.Tests\/Taskdeck.Api.Tests.csproj --configuration Release --no-restore/)
  assert.doesNotMatch(reusable, /--filter|continue-on-error: \$\{\{ inputs/)
})
