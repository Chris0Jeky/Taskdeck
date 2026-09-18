import assert from 'node:assert/strict'
import { existsSync } from 'node:fs'
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import test from 'node:test'

import {
  createFixtureCleanupBudget,
  createFixtureLayout,
  removeFixture,
  removeFixtureDirectory,
} from './dev-up-fixture-cleanup.mjs'

function lockedDirectoryError(path) {
  return Object.assign(new Error(`synthetic lock on ${path}`), {
    code: 'EBUSY',
    path,
    syscall: 'rmdir',
  })
}

test('fixture teardown consumes only the remaining cleanup budget and preserves a diagnostic margin', async () => {
  let now = 9_700
  const delays = []
  const cleanupBudget = createFixtureCleanupBudget({
    testStartedAtMs: 0,
    testTimeoutMs: 10_000,
    diagnosticMarginMs: 250,
  })

  await assert.rejects(
    removeFixtureDirectory(
      {
        root: '/synthetic/workspace',
        livePidFile: '/synthetic/live-pids.log',
        cleanupBudget,
      },
      {
        now: () => now,
        remove: async (path) => {
          throw lockedDirectoryError(path)
        },
        sleep: async (delayMs) => {
          delays.push(delayMs)
          now += delayMs
        },
        readText: async () => `node ${process.pid}\n`,
        listEntries: async () => [],
        isProcessAlive: () => true,
      },
    ),
    (error) => {
      assert.match(error.message, /^DEV_UP_FIXTURE_TEARDOWN_FAILED:/)
      assert.match(error.message, /250ms was reserved for teardown diagnostics/)
      assert.match(error.message, new RegExp(`node pid ${process.pid}`))
      return true
    },
  )

  assert.deepEqual(delays, [50])
  assert.equal(now, cleanupBudget.cleanupDeadlineMs)
  assert.equal(cleanupBudget.testDeadlineMs - now, 250)
})

test('fixture teardown surfaces the workspace deadline failure before envelope cleanup', async () => {
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
  let envelopeRemovalAttempts = 0

  await assert.rejects(
    removeFixture(fixture, {
      now: () => cleanupBudget.cleanupDeadlineMs,
      remove: async (path) => {
        throw lockedDirectoryError(path)
      },
      removeEnvelope: async () => {
        envelopeRemovalAttempts += 1
        throw new Error('the evidence envelope must not be retried after the workspace deadline')
      },
      readText: async () => `node ${process.pid}\n`,
      listEntries: async () => [],
      isProcessAlive: () => true,
    }),
    (error) => {
      assert.match(error.message, /^DEV_UP_FIXTURE_TEARDOWN_FAILED:/)
      assert.match(error.message, /100ms was reserved for teardown diagnostics/)
      assert.doesNotMatch(error.message, /evidence envelope must not be retried/)
      return true
    },
  )

  assert.equal(envelopeRemovalAttempts, 0)
})

test('fixture PID evidence survives partial recursive removal of the workspace', async () => {
  const envelopeRoot = await mkdtemp(join(tmpdir(), 'taskdeck-dev-up-cleanup-test-'))
  const layout = createFixtureLayout(envelopeRoot)
  const cleanupBudget = createFixtureCleanupBudget({
    testStartedAtMs: 0,
    testTimeoutMs: 1_000,
    diagnosticMarginMs: 100,
  })

  try {
    await mkdir(layout.root, { recursive: true })
    await writeFile(join(layout.root, 'partial-removal-canary.txt'), 'remove me\n')
    await writeFile(layout.livePidFile, `dotnet ${process.pid}\n`)

    assert.equal(dirname(layout.root), envelopeRoot)
    assert.equal(dirname(layout.livePidFile), envelopeRoot)
    assert.notEqual(layout.livePidFile, join(layout.root, 'live-pids.log'))

    await assert.rejects(
      removeFixtureDirectory(
        { ...layout, cleanupBudget },
        {
          now: () => cleanupBudget.cleanupDeadlineMs,
          remove: async (path) => {
            await rm(path, { recursive: true, force: true })
            throw lockedDirectoryError(path)
          },
        },
      ),
      (error) => {
        assert.match(error.message, /^DEV_UP_FIXTURE_TEARDOWN_FAILED:/)
        assert.match(error.message, new RegExp(`dotnet pid ${process.pid}`))
        assert.match(error.message, /none \(root already gone\)/)
        return true
      },
    )

    assert.equal(existsSync(layout.root), false)
    assert.equal(await readFile(layout.livePidFile, 'utf8'), `dotnet ${process.pid}\n`)
  } finally {
    await rm(envelopeRoot, { recursive: true, force: true })
  }
})
