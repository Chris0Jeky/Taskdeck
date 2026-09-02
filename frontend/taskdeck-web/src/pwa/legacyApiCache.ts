import { logWarn } from '../utils/errorReporting'

export const LEGACY_API_CACHE_PREFIX = 'taskdeck-api-cache'

let activePurge: Promise<boolean> | null = null

/** Removes every version of the pre-#2350 authenticated API cache. */
export function purgeLegacyApiCaches(): Promise<boolean> {
  if (typeof caches === 'undefined') return Promise.resolve(true)
  if (activePurge) return activePurge

  activePurge = caches.keys()
    .then((cacheNames) => Promise.all(
      cacheNames
        .filter((cacheName) => cacheName.startsWith(LEGACY_API_CACHE_PREFIX))
        .map((cacheName) => caches.delete(cacheName)),
    ))
    .then((results) => results.every(Boolean))
    .catch(() => {
      // Do not include the exception: a browser implementation could include
      // request metadata. This is observable without exposing a token.
      logWarn('Legacy API cache purge failed; authenticated session establishment is blocked.')
      return false
    })
    .finally(() => {
      activePurge = null
    })

  return activePurge
}
