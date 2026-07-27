import test from 'node:test'
import assert from 'node:assert/strict'

import {
  inspectParkedStagingGateTriggers,
  inspectWorkflowDispatchImageTagInput,
  retainsReleaseEventHandling,
  validateParkedStagingGateWorkflow,
} from './check-github-ops-governance.mjs'

const canonicalWorkflow = `name: gate
on:
  workflow_dispatch:
    inputs:
      image_tag:
        description: image
        required: true
        type: string
jobs: {}
`

test('accepts the canonical workflow_dispatch mapping and ignores nested inputs', () => {
  const result = inspectParkedStagingGateTriggers(canonicalWorkflow)

  assert.deepEqual(result, {
    onBlockFound: true,
    triggerNames: ['workflow_dispatch'],
    unsupportedEntries: [],
  })
  assert.deepEqual(inspectWorkflowDispatchImageTagInput(canonicalWorkflow), {
    imageTagFound: true,
    requiredValues: ['true'],
    typeValues: ['string'],
  })
  assert.deepEqual(validateParkedStagingGateWorkflow(canonicalWorkflow), [])
})

test('discovers an inline flow-style release trigger', () => {
  const workflow = `on:
  workflow_dispatch:
    inputs:
      image_tag:
        required: true
        type: string
  release: { types: [published] }
jobs: {}
`
  const result = inspectParkedStagingGateTriggers(workflow)

  assert.deepEqual(result.triggerNames, ['workflow_dispatch', 'release'])
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /must remain manual-only/)
})

test('discovers a quoted release trigger', () => {
  const result = inspectParkedStagingGateTriggers(`on:
  workflow_dispatch:
  "release": { types: [published] }
jobs: {}
`)

  assert.deepEqual(result.triggerNames, ['workflow_dispatch', 'release'])
})

test('discovers a comment-suffixed release trigger', () => {
  const result = inspectParkedStagingGateTriggers(`on:
  workflow_dispatch:
  release: # publish events
    types: [published]
jobs: {}
`)

  assert.deepEqual(result.triggerNames, ['workflow_dispatch', 'release'])
})

test('fails closed on an unfamiliar indentation-two trigger entry', () => {
  const result = inspectParkedStagingGateTriggers(`on:
  workflow_dispatch:
  ? release
jobs: {}
`)

  assert.deepEqual(result.triggerNames, ['workflow_dispatch'])
  assert.deepEqual(result.unsupportedEntries, ['? release'])
})

test('recognizes stale release-event branches despite shell quoting variations', () => {
  assert.equal(retainsReleaseEventHandling('if [[ "$EVENT_NAME" == "release" ]]; then'), true)
  assert.equal(retainsReleaseEventHandling("if: github.event_name == 'release'"), true)
  assert.equal(retainsReleaseEventHandling('tag: ${{ github.event.release.tag_name }}'), true)
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
  const result = inspectWorkflowDispatchImageTagInput(workflow)

  assert.deepEqual(result.requiredValues, ['false'])
  assert.deepEqual(result.typeValues, ['string'])
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /required: true/)
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
  const result = inspectWorkflowDispatchImageTagInput(workflow)

  assert.deepEqual(result.requiredValues, ['true'])
  assert.deepEqual(result.typeValues, ['boolean'])
  assert.match(validateParkedStagingGateWorkflow(workflow).join('\n'), /type: string/)
})
