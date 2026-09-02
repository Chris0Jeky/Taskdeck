export type PresentationProfile = 'flow' | 'guided' | 'control'

export interface CapabilityAvailability {
  available: boolean
  reason?: string
}

export interface PresentationDecision {
  showAdvancedDiagnostics: boolean
  explainAutomation: boolean
  collapseEvidenceByDefault: boolean
  capability: CapabilityAvailability
}

export function resolvePresentation(
  profile: PresentationProfile,
  capability: CapabilityAvailability,
): PresentationDecision {
  return {
    showAdvancedDiagnostics: profile === 'control',
    explainAutomation: profile !== 'flow',
    collapseEvidenceByDefault: profile === 'flow',
    capability,
  }
}

// Profiles change disclosure and defaults only. Domain commands and permissions
// must remain the same shared actions regardless of the selected profile.
