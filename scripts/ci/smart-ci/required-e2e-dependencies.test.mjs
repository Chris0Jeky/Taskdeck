import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { test } from 'node:test'

const requiredWorkflowUrl = new URL('../../../.github/workflows/ci-required.yml', import.meta.url)
const reusableE2eWorkflowUrl = new URL('../../../.github/workflows/reusable-e2e-smoke.yml', import.meta.url)

function extractTopLevelJob(workflow, jobName) {
  const lines = workflow.replaceAll('\r\n', '\n').split('\n')
  const jobsIndex = lines.findIndex((line) => line.trim() === 'jobs:')
  assert.notEqual(jobsIndex, -1, 'ci-required.yml is missing the top-level jobs mapping')

  const jobPattern = new RegExp(`^  ${jobName.replace(/[.*+?^${}()|[\\]\\\\]/g, '\\$&')}:\\s*$`)
  const jobIndex = lines.findIndex((line, index) => index > jobsIndex && jobPattern.test(line))
  assert.notEqual(jobIndex, -1, `ci-required.yml is missing the top-level ${jobName} job`)

  const nextJobIndex = lines.findIndex(
    (line, index) => index > jobIndex && /^  [A-Za-z0-9_-]+:\s*$/.test(line),
  )
  return lines.slice(jobIndex, nextJobIndex === -1 ? lines.length : nextJobIndex).join('\n')
}

function extractNeeds(job) {
  const lines = job.split('\n')
  const needsIndex = lines.findIndex((line) => line.trim() === 'needs:')
  assert.notEqual(needsIndex, -1, 'e2e-smoke is missing its needs list')

  const needsIndent = lines[needsIndex].search(/\S/)
  const dependencies = []
  for (const line of lines.slice(needsIndex + 1)) {
    const item = line.match(new RegExp(`^\\s{${needsIndent + 2}}-\\s*([A-Za-z0-9_-]+)\\s*$`))
    if (item) {
      dependencies.push(item[1])
      continue
    }
    if (line.trim() === '') continue
    break
  }
  return dependencies
}

test('required E2E waits on the independent required prerequisites', async () => {
  const workflow = await readFile(requiredWorkflowUrl, 'utf8')
  const e2eJob = extractTopLevelJob(workflow, 'e2e-smoke')

  assert.deepEqual(extractNeeds(e2eJob).sort(), [
    'api-integration',
    'backend-architecture',
    'backend-unit',
    'docs-governance',
    'migration-validation',
  ])
  assert.doesNotMatch(e2eJob, /^      - frontend-unit\s*$/m)
  assert.match(e2eJob, /^    uses:\s*\.\/\.github\/workflows\/reusable-e2e-smoke\.yml\s*$/m)
})

test('reusable E2E owns its runtime setup and execution', async () => {
  const workflow = await readFile(reusableE2eWorkflowUrl, 'utf8')

  assert.match(workflow, /- name: Checkout[\s\S]*?uses: actions\/checkout@/)
  assert.match(workflow, /- name: Setup \.NET[\s\S]*?uses: actions\/setup-dotnet@/)
  assert.match(workflow, /- name: Setup Node[\s\S]*?uses: actions\/setup-node@/)
  assert.match(
    workflow,
    /- name: Install frontend dependencies[\s\S]*?working-directory: frontend\/taskdeck-web[\s\S]*?run: npm ci/,
  )
  assert.match(workflow, /- name: Run Playwright smoke tests[\s\S]*?run: npx playwright test/)
})
