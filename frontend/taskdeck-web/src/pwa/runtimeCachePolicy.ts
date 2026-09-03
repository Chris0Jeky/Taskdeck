// Keep this aligned with the navigation-fallback denylist. URLs retain encoded
// path segments in `pathname`, while the API proxy decodes and merges them.
const API_PATH = /^\/api(?:\/|$)/i

// Denying `/api` is necessary but not sufficient, because the API base is a
// deployment choice: `VITE_API_BASE_URL` may be prefixed (`/taskdeck/api`, handled
// in api/versionApi.ts), and an authenticated response whose path merely ends in an
// image extension - `/taskdeck/api/users/by-username/alice.png` - would otherwise be
// stored in the shared, cross-identity `taskdeck-static-assets` cache. Runtime
// matchers therefore anchor on build-owned directories and separately exclude
// the normalized configured API base. An ambiguous base disables runtime
// matching instead of risking an identity-bound cache entry.
const LOCALE_CATALOG_PATH = /^\/assets\/(?:it|es)-[\w-]+\.js$/
const STATIC_ASSET_PATH = /^\/(?:assets|icons)\/[^?#]*\.(?:png|jpg|jpeg|svg|gif|webp|ico|woff|woff2)$/i

/**
 * Returns the normalized path portion of a configured API base.
 *
 * A missing or empty base keeps the legacy `/api` boundary. Any other
 * malformed or ambiguous value returns `null`; callers treat that as a
 * fail-closed configuration and admit nothing to the runtime caches.
 */
export function normalizeApiBasePath(apiBaseUrl?: string): string | null {
  if (apiBaseUrl === undefined || apiBaseUrl.trim() === '') return ''

  const value = apiBaseUrl.trim()
  const isAbsolute = /^https?:\/\//i.test(value)
  if (!isAbsolute && (!value.startsWith('/') || value.startsWith('//'))) return null

  try {
    const parsed = isAbsolute ? new URL(value) : new URL(value, 'https://taskdeck.invalid')
    if (parsed.search || parsed.hash || parsed.username || parsed.password) return null

    const path = decodeURIComponent(parsed.pathname).replace(/\/+/g, '/').replace(/\/$/, '')
    return path &&
      path !== '/' &&
      !path.includes('\\') &&
      [...path].every((character) => character.charCodeAt(0) <= 0x7f)
      ? path
      : null
  } catch {
    return null
  }
}

/** Runtime cache predicates must never admit an API path, whatever its origin. */
export function isApiPath(pathname: string, apiBaseUrl?: string): boolean {
  const configuredPath = normalizeApiBasePath(apiBaseUrl)
  if (configuredPath === null) return true

  try {
    const normalizedPath = decodeURIComponent(pathname).replace(/\/+/g, '/')
    return (
      API_PATH.test(normalizedPath) ||
      (configuredPath !== '' &&
        (normalizedPath === configuredPath || normalizedPath.startsWith(`${configuredPath}/`)))
    )
  } catch {
    return true
  }
}

export function isLocaleCatalogRequest({ url }: { url: URL }, apiBaseUrl?: string): boolean {
  return !isApiPath(url.pathname, apiBaseUrl) && LOCALE_CATALOG_PATH.test(url.pathname)
}

export function isStaticAssetRequest({ url }: { url: URL }, apiBaseUrl?: string): boolean {
  return !isApiPath(url.pathname, apiBaseUrl) && STATIC_ASSET_PATH.test(url.pathname)
}
