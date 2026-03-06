import { describe, expect, it } from 'vitest'

import { parseDemoRunArgs } from '../scripts/demo-run-lib.mjs'

describe('demo-run argument parsing', () => {
  it('parses clean dry-run scenarios explicitly', () => {
    expect(parseDemoRunArgs(['node', 'demo-run.mjs', '--clean', '--dry-run', 'support-triage'])).toEqual({
      scenario: 'support-triage',
      clean: true,
      dryRun: true,
      list: false,
      skipLlm: false,
      continueOnError: false,
    })
  })

  it('rejects dry-run without clean to avoid misleading no-op scenarios', () => {
    expect(() => parseDemoRunArgs(['node', 'demo-run.mjs', '--dry-run', 'support-triage'])).toThrow(
      '--dry-run is only supported together with --clean',
    )
  })

  it('rejects unknown flags instead of silently ignoring them', () => {
    expect(() => parseDemoRunArgs(['node', 'demo-run.mjs', '--unknown', 'support-triage'])).toThrow(
      'Unknown demo-run option: --unknown',
    )
  })
})
