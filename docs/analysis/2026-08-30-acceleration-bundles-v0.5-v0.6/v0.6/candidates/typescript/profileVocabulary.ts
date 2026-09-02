
export type ProcessingProfile = 'private' | 'balanced' | 'strict' | 'expert'
export type PresentationProfile = 'flow' | 'guided' | 'control'
export type AuthorityProfile =
  | 'observe'
  | 'suggest'
  | 'assist'
  | 'operate'
  | 'autonomous'
  | 'custom'

export const processingLabels: Record<ProcessingProfile, string> = {
  private: 'Private',
  balanced: 'Balanced',
  strict: 'Strict',
  expert: 'Expert',
}

export function isProcessingProfile(value: unknown): value is ProcessingProfile {
  return typeof value === 'string' && value in processingLabels
}

export function migratePresentationProfile(value: string | null | undefined): PresentationProfile {
  switch (value) {
    case 'guided':
      return 'guided'
    case 'workbench':
    case 'agent':
    case 'control':
      return 'control'
    case 'flow':
      return 'flow'
    default:
      return 'guided'
  }
}
