export const CLIENT_ONBOARDING_DEMO_BOARD_NAME_FRAGMENT = 'client onboarding demo'

export function isClientOnboardingDemoBoardName(boardName?: string | null): boolean {
  return Boolean(boardName?.trim().toLowerCase().includes(CLIENT_ONBOARDING_DEMO_BOARD_NAME_FRAGMENT))
}
