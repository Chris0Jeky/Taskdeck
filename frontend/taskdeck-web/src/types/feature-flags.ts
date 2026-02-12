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
  newAccess: true,
  newActivity: true,
  newOps: true,
  newAutomation: true,
  newArchive: true,
}
