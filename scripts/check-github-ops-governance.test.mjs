import test from 'node:test'
import assert from 'node:assert/strict'

import {
  hasExactParkedStagingGateOnBlock,
  retainsReleaseEventHandling,
  validateParkedStagingGateWorkflow,
} from './check-github-ops-governance.mjs'

const canonicalWorkflow = `name: gate
on:
  workflow_dispatch:
    inputs:
      image_tag:
        description: "Container image tag to deploy (e.g., v0.2.0)"
        required: true
        type: string
      skip_smoke:
        description: "Skip smoke tests (emergency only)"
        required: false
        type: boolean
        default: false
permissions:
  contents: read
jobs: {}
`

test('accepts the exact reviewed manual-only workflow_dispatch block', () => {
  assert.equal(hasExactParkedStagingGateOnBlock(canonicalWorkflow), true)
  assert.deepEqual(validateParkedStagingGateWorkflow(canonicalWorkflow), [])
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
  assert.equal(hasExactParkedStagingGateOnBlock(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /exact reviewed/)
})

test('rejects a quoted release trigger', () => {
  const workflow = `on:
  workflow_dispatch:
  "release": { types: [published] }
jobs: {}
`

  assert.equal(hasExactParkedStagingGateOnBlock(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /exact reviewed/)
})

test('rejects a comment-suffixed release trigger', () => {
  const workflow = `on:
  workflow_dispatch:
  release: # publish events
    types: [published]
jobs: {}
`

  assert.equal(hasExactParkedStagingGateOnBlock(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /exact reviewed/)
})

test('rejects an unfamiliar indentation-two trigger entry', () => {
  const workflow = `on:
  workflow_dispatch:
  ? release
jobs: {}
`

  assert.equal(hasExactParkedStagingGateOnBlock(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /exact reviewed/)
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

test('detects a non-required image_tag input', () => {
  const workflow = `on:
  workflow_dispatch:
    inputs:
      image_tag:
        required: false
        type: string
jobs: {}
`
  assert.equal(hasExactParkedStagingGateOnBlock(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /exact reviewed/)
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
  assert.equal(hasExactParkedStagingGateOnBlock(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /exact reviewed/)
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
  assert.equal(hasExactParkedStagingGateOnBlock(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /exact reviewed/)
})

test('rejects an event after a column-zero comment inside the on mapping', () => {
  const workflow = canonicalWorkflow.replace(
    '\npermissions:',
    '\n# A column-zero comment does not end the YAML mapping.\n  release:\n    types: [published]\npermissions:',
  )

  assert.equal(hasExactParkedStagingGateOnBlock(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /exact reviewed/)
})

test('rejects multiline scalar text that forges apparent image_tag properties', () => {
  const workflow = canonicalWorkflow.replace(
    '        description: "Container image tag to deploy (e.g., v0.2.0)"\n        required: true\n        type: string',
    '        description: |\n          required: true\n          type: string',
  )

  assert.equal(hasExactParkedStagingGateOnBlock(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /exact reviewed/)
})

test('rejects escaped YAML property keys that decode to a different input contract', () => {
  const workflow = canonicalWorkflow
    .replace('        required: true', '        "requ\\u0069red": false')
    .replace('        type: string', '        "t\\u0079pe": boolean')

  assert.equal(hasExactParkedStagingGateOnBlock(workflow), false)
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /exact reviewed/)
})
