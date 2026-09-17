import { spawnSync } from 'node:child_process'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const nativeContractPath = resolve(
  process.cwd(),
  '../../tests/load/k6/actor-board-assignments.test.mjs',
)

describe('k6 actor assignment contract', () => {
  it('passes under the native Node test runner', () => {
    const result = spawnSync(process.execPath, ['--test', nativeContractPath], {
      encoding: 'utf8',
      env: process.env,
    })
    const output = [result.stdout, result.stderr].filter(Boolean).join('\n')

    expect(result.error, output).toBeUndefined()
    expect(result.status, output).toBe(0)
  })
})
