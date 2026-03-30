export function parseTrueishEnv(value) {
  if (typeof value !== 'string') return false
  const normalized = value.trim().toLowerCase()
  return normalized === '1' || normalized === 'true' || normalized === 'yes' || normalized === 'on'
}

export function normalizeBaseUrl(value, fallback) {
  const normalized = value ?? fallback
  return normalized.endsWith('/') ? normalized.slice(0, -1) : normalized
}

export function getHostname(url) {
  return new URL(url).hostname.toLowerCase()
}

export function isLocalHostname(hostname) {
  return hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1' || hostname === '[::1]'
}

export function assertSafeLocalApiTarget(
  apiBaseUrl,
  {
    allowNonLocal = false,
    overrideEnvVar = 'TASKDECK_DEMO_ALLOW_NON_LOCAL_API',
    contextLabel = 'run demo harness',
  } = {},
) {
  let hostname
  try {
    hostname = getHostname(apiBaseUrl)
  } catch (err) {
    throw new Error(`Invalid API base URL "${apiBaseUrl}". ${err?.message || err}`, { cause: err })
  }

  if (isLocalHostname(hostname) || allowNonLocal) {
    return
  }

  throw new Error(
    `Refusing to ${contextLabel} against non-local API target "${apiBaseUrl}". ` +
      `Set ${overrideEnvVar}=true to override intentionally.`,
  )
}

export function isoDaysFromNow(days) {
  const value = new Date()
  value.setDate(value.getDate() + Number(days || 0))
  return value.toISOString()
}
