import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

import { validateApiIntegrationWorkflow } from './api-integration-workflow-contract.mjs'

const canonicalWorkflow = readFileSync(
  new URL('../../.github/workflows/reusable-api-integration.yml', import.meta.url),
  'utf8',
)

function errorsFor(workflow) {
  return validateApiIntegrationWorkflow(workflow).join('\n')
}

test('accepts the bounded API integration workflow and timeout evidence contract', () => {
  assert.deepEqual(validateApiIntegrationWorkflow(canonicalWorkflow), [])
})

test('rejects removing the calibrated outer test-step timeout', () => {
  const workflow = canonicalWorkflow.replace('        timeout-minutes: 45\n', '')

  assert.match(errorsFor(workflow), /45-minute timeout/)
})

test('rejects removing per-test hang termination or its bounded mini dump', () => {
  const withoutHangTimeout = canonicalWorkflow.replace(' --blame-hang-timeout 20m', '')
  const withoutMiniDump = canonicalWorkflow.replace(' --blame-hang-dump-type mini', '')

  assert.match(errorsFor(withoutHangTimeout), /20-minute per-test hang timeout/)
  assert.match(errorsFor(withoutMiniDump), /mini hang dumps/)
})

test('rejects collectors that stop running after a timeout', () => {
  const workflow = canonicalWorkflow.replace(
    '      - name: Upload API integration failure evidence\n        if: ${{ always() && steps.api_integration_tests.outcome != \'success\' }}',
    '      - name: Upload API integration failure evidence\n        if: failure()',
  )

  assert.match(errorsFor(workflow), /failure evidence upload must run after a timed-out test step/)
})

test('rejects making timeout evidence collection fatal or incomplete', () => {
  const fatalWorkflow = canonicalWorkflow.replace(
    '      - name: Upload API integration failure evidence\n        if: ${{ always() && steps.api_integration_tests.outcome != \'success\' }}\n        continue-on-error: true',
    '      - name: Upload API integration failure evidence\n        if: ${{ always() && steps.api_integration_tests.outcome != \'success\' }}',
  )
  const partialWorkflow = canonicalWorkflow.replace(
    '          path: backend/TestResults/api-integration/${{ matrix.os }}/',
    '          path: backend/TestResults/api-integration/${{ matrix.os }}/*.trx',
  )

  assert.match(errorsFor(fatalWorkflow), /failure evidence upload must be non-fatal/)
  assert.match(errorsFor(partialWorkflow), /complete partial-results directory/)
})

test('rejects timing post-processing that can obscure a missing or partial TRX', () => {
  const workflow = canonicalWorkflow.replace(
    '      - name: Summarize API integration timing\n        if: always()\n        continue-on-error: true',
    '      - name: Summarize API integration timing\n        if: always()',
  )

  assert.match(errorsFor(workflow), /timing summarizer must be non-fatal/)
})
