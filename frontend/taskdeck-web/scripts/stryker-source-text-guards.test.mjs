import assert from 'node:assert/strict'
import { access, readFile } from 'node:fs/promises'
import { test } from 'node:test'

import config, { sourceTextGuardTests } from '../stryker.config.mjs'

const parityGuard = 'src/tests/views/paper/boardMutationCapabilityParity.spec.ts'

/**
 * Source-text guards are ordinary required-CI tests. They must not read Stryker's
 * instrumented copies of the very production files whose source shape they prove.
 */
test('sandbox isolation omits only explicitly registered source-text specs', () => {
  assert.ok(Array.isArray(sourceTextGuardTests))
  assert.ok(sourceTextGuardTests.length > 0)
  assert.equal(new Set(sourceTextGuardTests).size, sourceTextGuardTests.length)
  for (const guard of sourceTextGuardTests) {
    assert.match(guard, /^src\/tests\/(?:[\w-]+\/)*[\w-]+\.spec\.ts$/)
  }
  assert.deepEqual(
    config.ignorePatterns,
    sourceTextGuardTests.map((guard) => `/${guard}`),
    'omit literal, root-anchored guard files from the sandbox, not production code',
  )
  assert.equal(
    config.testFiles,
    undefined,
    'negative testFiles patterns did not exclude this guard in the hosted dry run',
  )
  assert.equal(config.vitest.configFile, 'vitest.config.ts')
  assert.ok(config.mutate.includes('src/store/boardStore.ts'))
})

test('registered source-text guards still exist in the ordinary test tree', async () => {
  for (const guard of sourceTextGuardTests) {
    await access(new URL(`../${guard}`, import.meta.url))
  }
})

test('the board mutation parity source guard stays explicitly registered', async () => {
  const source = await readFile(new URL(`../${parityGuard}`, import.meta.url), 'utf8')

  assert.match(source, /query:\s*['"]\?raw['"]/)
  assert.match(source, /store\/boardStore\.ts/)
  assert.ok(
    sourceTextGuardTests.includes(parityGuard),
    'a source-text guard over a mutate target must be registered beside the mutate list',
  )
})
