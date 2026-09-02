// Keep this aligned with the navigation-fallback denylist. URLs retain encoded
// path segments in `pathname`, while the API proxy decodes and merges them.
const API_PATH = /^(?:\/|%2[fF])+(?:a|%61|%41)(?:p|%70|%50)(?:i|%69|%49)(?:[/?]|%2[fF]|$)/i

// Denying `/api` is necessary but not sufficient, because the API base is a
// deployment choice: `VITE_API_BASE_URL` may be prefixed (`/taskdeck/api`, handled
// in api/versionApi.ts), and an authenticated response whose path merely ends in an
// image extension - `/taskdeck/api/users/by-username/alice.png` - would otherwise be
// stored in the shared, cross-identity `taskdeck-static-assets` cache. So the
// runtime caches admit only the directories the Vite build itself emits. That is
// fail-closed for every API base shape: an unrecognised layout loses runtime
// caching for a static asset, it never admits an API response.
const LOCALE_CATALOG_PATH = /^\/assets\/(?:it|es)-[\w-]+\.js$/
const STATIC_ASSET_PATH = /^\/(?:assets|icons)\/[^?#]*\.(?:png|jpg|jpeg|svg|gif|webp|ico|woff|woff2)$/i

/** Runtime cache predicates must never admit an API path, whatever its origin. */
export function isApiPath(pathname: string): boolean {
  return API_PATH.test(pathname)
}

export function isLocaleCatalogRequest({ url }: { url: URL }): boolean {
  return !isApiPath(url.pathname) && LOCALE_CATALOG_PATH.test(url.pathname)
}

export function isStaticAssetRequest({ url }: { url: URL }): boolean {
  return !isApiPath(url.pathname) && STATIC_ASSET_PATH.test(url.pathname)
}
