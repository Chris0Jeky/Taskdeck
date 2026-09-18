import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

import { createFixtureCleanupBudget, removeFixture } from './dev-up-fixture-cleanup.mjs'

for (const failureStage of ['workspace', 'envelope']) {
  for (const diagnosticStage of ['ledger read', 'process probe']) {
    test(`${failureStage} cleanup preserves its cause when the ${diagnosticStage} fails`, async () => {
      const cleanupBudget = createFixtureCleanupBudget({
        testStartedAtMs: 0,
        testTimeoutMs: 1_000,
        diagnosticMarginMs: 100,
      })
      const fixture = {
        envelopeRoot: '/synthetic',
        root: '/synthetic/workspace',
        livePidFile: '/synthetic/live-pids.log',
        cleanupBudget,
      }
      const removalFailure = Object.assign(new Error('workspace locked'), { code: 'EBUSY' })
      const diagnosticFailure = Object.assign(new Error('PID evidence unavailable'), { code: 'EACCES' })
      const removed = []

      await assert.rejects(
        removeFixture(fixture, {
          now: () => cleanupBudget.cleanupDeadlineMs,
          remove: async (path) => {
            removed.push(path)
            if (failureStage === 'workspace') throw removalFailure
          },
          removeEnvelope: async (path) => {
            removed.push(path)
            throw removalFailure
          },
          readText: async () => {
            if (diagnosticStage === 'ledger read') throw diagnosticFailure
            return 'node 123\n'
          },
          isProcessAlive: () => {
            throw diagnosticFailure
          },
          listEntries: async () => ['held.log'],
        }),
        (error) => {
          assert.match(error.message, /^DEV_UP_FIXTURE_TEARDOWN_FAILED:/)
          assert.equal(error.cause, removalFailure)
          assert.match(error.message, /Fixture processes still alive: unavailable \(EACCES\)/)
          assert.doesNotMatch(error.message, /none (recorded|still alive)/)
          if (failureStage === 'workspace') {
            assert.match(error.message, /100ms was reserved for teardown diagnostics/)
            assert.match(error.message, /Remaining entries: held\.log/)
          }
          return true
        },
      )
      assert.deepEqual(
        removed,
        failureStage === 'workspace' ? [fixture.root] : [fixture.root, fixture.envelopeRoot],
      )
    })
  }
}

test('required source-launcher lane discovers the cleanup regression family', async () => {
  const workflow = await readFile(
    new URL('../../.github/workflows/reusable-frontend-unit.yml', import.meta.url),
    'utf8',
  )
  assert.match(
    workflow,
    /^\s*run: node --test\b[^\r\n]* scripts\/ci\/dev-up\*\.test\.mjs\s*$/m,
    'required CI must execute the fixture cleanup regressions, not just dev-up.test.mjs',
  )
})
