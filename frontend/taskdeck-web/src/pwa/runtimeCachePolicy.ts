// Keep this aligned with the navigation-fallback denylist. URLs retain encoded
// path segments in `pathname`, while the API proxy decodes and merges them.
const API_PATH = /^(?:\/|%2[fF])+(?:a|%61|%41)(?:p|%70|%50)(?:i|%69|%49)(?:[/?]|%2[fF]|$)/i
const LOCALE_CATALOG_PATH = /^\/assets\/(?:it|es)-[\w-]+\.js$/
const STATIC_ASSET_PATH = /\.(?:png|jpg|jpeg|svg|gif|webp|ico|woff|woff2)$/i

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
