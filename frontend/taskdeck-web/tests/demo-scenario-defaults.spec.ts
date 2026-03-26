import { describe, expect, it } from 'vitest'

import { resolveScenarioDefaultBoardName, resolveScenarioSelectedBoardName } from '../scripts/demo-scenario-defaults.mjs'

describe('demo scenario default board names', () => {
  it('uses the scenario definition board name for client-onboarding', async () => {
    await expect(resolveScenarioDefaultBoardName('client-onboarding')).resolves.toBe(
      'DEMO: Client Onboarding Demo',
    )
  })

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

  it('falls back to client onboarding when no scenario is provided', async () => {
    await expect(resolveScenarioDefaultBoardName('')).resolves.toBe('DEMO: Client Onboarding Demo')
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
