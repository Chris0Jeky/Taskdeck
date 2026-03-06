import { describe, expect, it } from 'vitest'

import { resolveScenarioDefaultBoardName, resolveScenarioSelectedBoardName } from '../scripts/demo-scenario-defaults.mjs'

describe('demo scenario default board names', () => {
  it('uses the scenario definition board name for content-calendar', async () => {
    await expect(resolveScenarioDefaultBoardName('content-calendar')).resolves.toBe(
      'DEMO: Content Calendar Scenario',
    )
  })

  it('rejects unknown scenarios instead of falling back', async () => {
    await expect(resolveScenarioDefaultBoardName('missing-scenario')).rejects.toThrow(
      'Unknown scenario "missing-scenario"',
    )
  })

  it('still falls back to engineering sprint when no scenario is provided', async () => {
    await expect(resolveScenarioDefaultBoardName('')).resolves.toBe('DEMO: Engineering Sprint')
  })

  it('prefers an explicit board override over the scenario default', async () => {
    await expect(
      resolveScenarioSelectedBoardName({
        scenarioIdOrPath: 'content-calendar',
        explicitBoardName: 'DEMO: Manual Override',
      }),
    ).resolves.toBe('DEMO: Manual Override')
  })
})
