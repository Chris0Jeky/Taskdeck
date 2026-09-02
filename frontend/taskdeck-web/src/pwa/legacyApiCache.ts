import { logWarn } from '../utils/errorReporting'
import { retireLegacyApiCacheWorker } from './legacyApiCacheWorker'

export const LEGACY_API_CACHE_PREFIX = 'taskdeck-api-cache'

let activePurge: Promise<boolean> | null = null

function isLegacyApiCache(cacheName: string): boolean {
  return cacheName.startsWith(LEGACY_API_CACHE_PREFIX)
}

async function purge(): Promise<boolean> {
  // Order matters: a worker that still holds the old NetworkFirst API route would
  // repopulate the namespace immediately after it is emptied.
  if (!(await retireLegacyApiCacheWorker())) return false

  const cacheNames = await caches.keys()
  await Promise.all(
    cacheNames.filter(isLegacyApiCache).map((cacheName) => caches.delete(cacheName).catch(() => false)),
  )
  // The verdict is absence, not the delete() return value: `delete()` reports false
  // for a cache another tab or the activating worker removed first, and the
  // namespace is equally safe in that case.
  const remaining = await caches.keys()
  return !remaining.some(isLegacyApiCache)
}

/** Removes every version of the pre-#2350 authenticated API cache. */
export function purgeLegacyApiCaches(): Promise<boolean> {
  if (typeof caches === 'undefined') return Promise.resolve(true)
  if (activePurge) return activePurge

  activePurge = purge()
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
