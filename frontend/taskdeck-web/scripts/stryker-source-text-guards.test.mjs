import assert from 'node:assert/strict'
import { access, readFile } from 'node:fs/promises'
import { test } from 'node:test'

import config, { sourceTextGuardTests } from '../stryker.config.mjs'

const parityGuard = 'src/tests/views/paper/boardMutationCapabilityParity.spec.ts'

/**
 * Source-text guards are ordinary required-CI tests. They must not read Stryker's
 * instrumented copies of the very production files whose source shape they prove.
 */
test('registered source-text guards are excluded only from the mutation test set', async () => {
  assert.ok(Array.isArray(sourceTextGuardTests))
  assert.ok(Array.isArray(config.testFiles))
  assert.ok(config.testFiles.some((pattern) => !pattern.startsWith('!')))

  for (const guard of sourceTextGuardTests) {
    await access(new URL(`../${guard}`, import.meta.url))
    assert.ok(
      config.testFiles.includes(`!${guard}`),
      `${guard} must be explicitly excluded from Stryker testFiles`,
    )
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
