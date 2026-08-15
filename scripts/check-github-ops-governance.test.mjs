import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

import {
  hasExpectedParkedStagingGateWorkflow,
  retainsReleaseEventHandling,
  validateParkedStagingGateWorkflow,
} from './check-github-ops-governance.mjs'
import './ci/prepare-staging-compose-inputs.test.mjs'

const canonicalWorkflow = readFileSync(
  new URL('../.github/workflows/cd-staging-gate.yml', import.meta.url),
  'utf8',
)

function workflowJobSection(startMarker, endMarker) {
  const start = canonicalWorkflow.indexOf(startMarker)
  const end = canonicalWorkflow.indexOf(endMarker, start + startMarker.length)
  assert.notEqual(start, -1, `missing workflow marker: ${startMarker}`)
  assert.notEqual(end, -1, `missing workflow marker: ${endMarker}`)
  return canonicalWorkflow.slice(start, end)
}

function workflowStepNames(jobSection) {
  return [...jobSection.matchAll(/^      - name: (.+)$/gm)].map((match) => match[1])
}

test('accepts the complete reviewed parked workflow', () => {
  assert.equal(hasExpectedParkedStagingGateWorkflow(canonicalWorkflow), true)
  assert.equal(
    hasExpectedParkedStagingGateWorkflow(canonicalWorkflow.replace(/\r?\n/g, '\r\n')),
    true,
  )
  assert.deepEqual(validateParkedStagingGateWorkflow(canonicalWorkflow), [])
})

test('prepares ephemeral inputs immediately before each first Compose invocation', () => {
  const buildSteps = workflowStepNames(
    workflowJobSection('  build-verification:', '  staging-smoke:'),
  )
  const smokeSection = workflowJobSection('  staging-smoke:', '  parked-handoff:')
  const smokeSteps = workflowStepNames(smokeSection)

  assert.deepEqual(buildSteps.slice(-4, -1), [
    'Build container images',
    'Prepare ephemeral Compose inputs',
    'Verify compose configuration',
  ])
  assert.deepEqual(smokeSteps.slice(1, 4), [
    'Build container images',
    'Prepare ephemeral Compose inputs',
    'Start stack',
  ])
  assert.match(
    smokeSection,
    /if: \$\{\{ failure\(\) && steps\.compose-inputs\.outcome == 'success' \}\}/,
  )
  assert.match(
    smokeSection,
    /if: \$\{\{ always\(\) && steps\.compose-inputs\.outcome == 'success' \}\}/,
  )
})

test('rejects an inline flow-style release trigger', () => {
  const workflow = `on:
  workflow_dispatch:
    inputs:
      image_tag:
        required: true
        type: string
  release: { types: [published] }
jobs: {}
`
  assert.equal(hasExpectedParkedStagingGateWorkflow(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /reviewed parked-workflow digest/)
})

test('rejects a quoted release trigger', () => {
  const workflow = `on:
  workflow_dispatch:
  "release": { types: [published] }
jobs: {}
`

  assert.equal(hasExpectedParkedStagingGateWorkflow(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /reviewed parked-workflow digest/)
})

test('rejects a comment-suffixed release trigger', () => {
  const workflow = `on:
  workflow_dispatch:
  release: # publish events
    types: [published]
jobs: {}
`

  assert.equal(hasExpectedParkedStagingGateWorkflow(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /reviewed parked-workflow digest/)
})

test('rejects an unfamiliar indentation-two trigger entry', () => {
  const workflow = `on:
  workflow_dispatch:
  ? release
jobs: {}
`

  assert.equal(hasExpectedParkedStagingGateWorkflow(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /reviewed parked-workflow digest/)
})

test('recognizes stale release-event branches despite shell quoting variations', () => {
  assert.equal(retainsReleaseEventHandling('if [[ "$EVENT_NAME" == "release" ]]; then'), true)
  assert.equal(retainsReleaseEventHandling("if: github.event_name == 'release'"), true)
  assert.equal(retainsReleaseEventHandling('tag: ${{ github.event.release.tag_name }}'), true)
  assert.equal(retainsReleaseEventHandling("if: github['event_name'] == 'release'"), true)
  assert.equal(retainsReleaseEventHandling("tag: ${{ github['event']['release']['tag_name'] }}"), true)
  assert.equal(retainsReleaseEventHandling('tag: ${{ github.event["release"].tag_name }}'), true)
  assert.equal(retainsReleaseEventHandling('if [[ "$GITHUB_EVENT_NAME" == "release" ]]; then'), true)
  assert.equal(retainsReleaseEventHandling('tag: ${{ inputs.image_tag }}'), false)
})

test('accepts event-name diagnostics and non-release conditions', () => {
  assert.equal(retainsReleaseEventHandling("if: github.event_name == 'workflow_dispatch'"), false)
  assert.equal(retainsReleaseEventHandling('run: echo "${{ github.event_name }}"'), false)
  assert.equal(retainsReleaseEventHandling("if: github['event_name'] != ''"), false)
  assert.equal(retainsReleaseEventHandling('run: echo "$GITHUB_EVENT_NAME"'), false)
})

test('detects a non-required image_tag input', () => {
  const workflow = `on:
  workflow_dispatch:
    inputs:
      image_tag:
        required: false
        type: string
jobs: {}
`
  assert.equal(hasExpectedParkedStagingGateWorkflow(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /reviewed parked-workflow digest/)
})

test('detects a non-string image_tag input', () => {
  const workflow = `on:
  workflow_dispatch:
    inputs:
      image_tag:
        required: true
        type: boolean
jobs: {}
`
  assert.equal(hasExpectedParkedStagingGateWorkflow(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /reviewed parked-workflow digest/)
})

test('rejects a defaulted image_tag input', () => {
  const workflow = `on:
  workflow_dispatch:
    inputs:
      image_tag:
        required: true
        type: string
        default: latest
jobs: {}
`
  assert.equal(hasExpectedParkedStagingGateWorkflow(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /reviewed parked-workflow digest/)
})

test('rejects an event after a column-zero comment inside the on mapping', () => {
  const workflow = canonicalWorkflow.replace(
    '\npermissions:',
    '\n# A column-zero comment does not end the YAML mapping.\n  release:\n    types: [published]\npermissions:',
  )

  assert.equal(hasExpectedParkedStagingGateWorkflow(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /reviewed parked-workflow digest/)
})

test('rejects multiline scalar text that forges apparent image_tag properties', () => {
  const workflow = canonicalWorkflow.replace(
    '        description: "Container image tag to build and verify (e.g., v0.2.0)"',
    '        description: |\n          required: true\n          type: string',
  )

  assert.equal(hasExpectedParkedStagingGateWorkflow(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /reviewed parked-workflow digest/)
})

test('rejects escaped YAML property keys that decode to a different input contract', () => {
  const workflow = canonicalWorkflow
    .replace('        required: true', '        "requ\\u0069red": false')
    .replace('        type: string', '        "t\\u0079pe": boolean')

  assert.equal(hasExpectedParkedStagingGateWorkflow(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /reviewed parked-workflow digest/)
})

test('rejects hiding the reviewed bytes inside a scalar before an effective release trigger', () => {
  const workflow = canonicalWorkflow
    .replace('name: CD Staging Gate', "name: '")
    .replace(
      '\npermissions:',
      `\npermissions:\n  contents: read\n'\n"on":\n  workflow_dispatch:\n    inputs:\n      image_tag:\n        required: true\n        type: string\n      skip_smoke:\n        required: false\n        type: boolean\n        default: false\n  release: { types: [published] }\npermissions:`,
    )

  assert.equal(hasExpectedParkedStagingGateWorkflow(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /reviewed parked-workflow digest/)
})
