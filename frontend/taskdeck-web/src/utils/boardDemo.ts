export const CLIENT_ONBOARDING_DEMO_BOARD_NAME = 'DEMO: Client Onboarding Demo'

export function isClientOnboardingDemoBoardName(boardName?: string | null): boolean {
  return boardName?.trim().toLowerCase() === CLIENT_ONBOARDING_DEMO_BOARD_NAME.toLowerCase()
}
