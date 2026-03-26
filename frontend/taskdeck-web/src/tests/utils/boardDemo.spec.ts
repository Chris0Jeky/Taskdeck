import { describe, expect, it } from 'vitest'
import {
  CLIENT_ONBOARDING_DEMO_BOARD_NAME_FRAGMENT,
  isClientOnboardingDemoBoardName,
} from '../../utils/boardDemo'

describe('boardDemo', () => {
  it('exposes the canonical demo-board fragment', () => {
    expect(CLIENT_ONBOARDING_DEMO_BOARD_NAME_FRAGMENT).toBe('client onboarding demo')
  })

  it('matches client onboarding demo board names regardless of whitespace or casing', () => {
    expect(isClientOnboardingDemoBoardName('DEMO: Client Onboarding Demo')).toBe(true)
    expect(isClientOnboardingDemoBoardName('  client onboarding demo  ')).toBe(true)
    expect(isClientOnboardingDemoBoardName('Client onboarding DEMO board')).toBe(true)
  })

  it('returns false for missing or non-demo board names', () => {
    expect(isClientOnboardingDemoBoardName(undefined)).toBe(false)
    expect(isClientOnboardingDemoBoardName(null)).toBe(false)
    expect(isClientOnboardingDemoBoardName('Engineering Sprint')).toBe(false)
  })
})
