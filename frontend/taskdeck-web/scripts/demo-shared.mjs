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

export function extractListItems(response, contextLabel = 'response') {
  if (Array.isArray(response)) {
    return response
  }

  if (response && typeof response === 'object') {
    if (Array.isArray(response.items)) {
      return response.items
    }

    if (Array.isArray(response.Items)) {
      return response.Items
    }
  }

  throw new TypeError(`${contextLabel} did not return a list or paginated items object`)
}

export function hasMoreListItems(response) {
  if (!response || typeof response !== 'object' || Array.isArray(response)) {
    return false
  }

  return Boolean(response.hasMore ?? response.HasMore)
}

export async function collectAllListItems(
  fetchPage,
  { contextLabel = 'response', limit = 50 } = {},
) {
  const items = []
  let offset = 0

  while (true) {
    const response = (await fetchPage({ offset, limit })) ?? []
    const pageItems = extractListItems(response, contextLabel)
    items.push(...pageItems)

    if (!hasMoreListItems(response)) {
      return items
    }

    if (pageItems.length === 0) {
      throw new Error(`${contextLabel} pagination reported more items without returning a page`)
    }

    offset += pageItems.length
  }
}

export function isoDaysFromNow(days) {
  const value = new Date()
  value.setDate(value.getDate() + Number(days || 0))
  return value.toISOString()
}
