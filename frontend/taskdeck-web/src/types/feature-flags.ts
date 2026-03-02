export interface FeatureFlags {
  newShell: boolean
  newAuth: boolean
  newAccess: boolean
  newActivity: boolean
  newOps: boolean
  newAutomation: boolean
  newArchive: boolean
}

export const defaultFeatureFlags: FeatureFlags = {
  newShell: true,
  newAuth: true,
  newAutomation: true,
  // Advanced/diagnostic surfaces are useful, but noisy on first run.
  // Users can still enable them from Settings -> Feature Flags.
  newAccess: false,
  newActivity: false,
  newOps: false,
  newArchive: false,
}
