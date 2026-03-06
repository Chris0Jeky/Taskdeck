type ReuseExistingServerOptions = {
  requiresFreshServer?: boolean
}

export function resolveReuseExistingServer(
  env: NodeJS.ProcessEnv,
  { requiresFreshServer = false }: ReuseExistingServerOptions = {},
): boolean {
  const override = env.TASKDECK_E2E_REUSE_EXISTING_SERVER?.trim().toLowerCase()
  if (override === '1' || override === 'true') {
    return true
  }

  if (override === '0' || override === 'false') {
    return false
  }

  if (requiresFreshServer) {
    return false
  }

  return !env.CI
}
