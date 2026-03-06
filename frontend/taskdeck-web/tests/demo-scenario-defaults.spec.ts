import { describe, expect, it } from 'vitest'

import { resolveScenarioDefaultBoardName } from '../scripts/demo-scenario-defaults.mjs'

describe('demo scenario default board names', () => {
  it('uses the scenario definition board name for content-calendar', async () => {
    await expect(resolveScenarioDefaultBoardName('content-calendar')).resolves.toBe(
      'DEMO: Content Calendar Scenario',
    )
  })

  it('falls back to the engineering sprint default for unknown scenarios', async () => {
    await expect(resolveScenarioDefaultBoardName('missing-scenario')).rejects.toThrow(
      'Unknown scenario "missing-scenario"',
    )
  })

  it('still falls back to engineering sprint when no scenario is provided', async () => {
    await expect(resolveScenarioDefaultBoardName('')).resolves.toBe('DEMO: Engineering Sprint')
  })
})
