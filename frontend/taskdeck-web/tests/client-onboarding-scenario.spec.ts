import { describe, expect, it } from 'vitest'

import { loadJsonScenario, validateScenarioJson } from '../scripts/scenario-json-runner.mjs'

describe('client onboarding scenario contract', () => {
  it('keeps the shipped client-onboarding scenario deterministic and business-facing', async () => {
    const scenario = await loadJsonScenario('client-onboarding')

    expect(validateScenarioJson(scenario)).toBe(true)
    expect(scenario.id).toBe('client-onboarding')

    const createBoardStep = scenario.steps.find((step) => step.type === 'createBoard')
    expect(createBoardStep?.name).toBe('DEMO: Client Onboarding Demo')

    const starterPackStep = scenario.steps.find((step) => step.type === 'applyStarterPack')
    expect(starterPackStep?.starterPackId).toBe('board-blueprint-client-onboarding')

    const captureStep = scenario.steps.find((step) => step.type === 'createCapture')
    expect(captureStep?.text).toContain('New client onboarding - ACME Ltd')
    expect(captureStep?.text).toContain('Request director ID documents')
    expect(captureStep?.text).toContain('Prepare internal review once documents arrive')

    const stepTypes = scenario.steps.map((step) => step.type)
    expect(stepTypes).toEqual([
      'createBoard',
      'applyStarterPack',
      'createCapture',
      'triageCapture',
      'waitForCaptureProposal',
      'executeProposal',
    ])
  })
})
