import { describe, expect, it } from 'vitest'
import {
  CLIENT_ONBOARDING_DEMO_BOARD_NAME,
  isClientOnboardingDemoBoardName,
} from '../../utils/boardDemo'

describe('boardDemo', () => {
  it('exposes the canonical demo-board name', () => {
    expect(CLIENT_ONBOARDING_DEMO_BOARD_NAME).toBe('DEMO: Client Onboarding Demo')
  })

  it('matches the canonical demo board name with different casing and whitespace', () => {
    expect(isClientOnboardingDemoBoardName('DEMO: Client Onboarding Demo')).toBe(true)
    expect(isClientOnboardingDemoBoardName('  DEMO: Client Onboarding Demo  ')).toBe(true)
    expect(isClientOnboardingDemoBoardName('demo: client onboarding demo')).toBe(true)
  })

  it('does not match partial or unrelated board names', () => {
    expect(isClientOnboardingDemoBoardName(undefined)).toBe(false)
    expect(isClientOnboardingDemoBoardName(null)).toBe(false)
    expect(isClientOnboardingDemoBoardName('Engineering Sprint')).toBe(false)
    expect(isClientOnboardingDemoBoardName('Notes for my client onboarding demo')).toBe(false)
    expect(isClientOnboardingDemoBoardName('Client onboarding DEMO board')).toBe(false)
  })
})
