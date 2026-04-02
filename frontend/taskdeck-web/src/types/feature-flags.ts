export interface FeatureFlags {
  newShell: boolean
  newAuth: boolean
  newAccess: boolean
  newActivity: boolean
  newOps: boolean
  newAutomation: boolean
  newArchive: boolean
  devTools: boolean
}

export const defaultFeatureFlags: FeatureFlags = {
  newShell: true,
  newAuth: true,
  newAutomation: true,
  // These surfaces are shipped and reachable from the sidebar; default them on
  // so that direct-URL navigation (e.g. /workspace/archive) no longer silently
  // redirects to Home.  See #681.
  newAccess: true,
  newActivity: true,
  newOps: true,
  newArchive: true,
  // Internal dev tooling stays behind a flag — not user-facing.
  devTools: false,
}
