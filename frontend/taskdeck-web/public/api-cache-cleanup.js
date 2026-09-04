// Runtime caches are not covered by Workbox's cleanupOutdatedCaches. This
// activation hook retires the entire legacy API namespace without touching the
// explicit share-target offline queue.
const TASKDECK_LEGACY_API_CACHE_PREFIX = 'taskdeck-api-cache'

// The pre-#2350 static-asset route matched on file extension alone, so a
// deployment with a prefixed API base could have stored an authenticated response
// such as `/taskdeck/api/users/by-username/alice.png` in this cache, where it
// survives an account switch for 30 days. Invalidate the whole runtime cache on
// activation instead of trying to reconstruct the build-time API base here.
const TASKDECK_STATIC_ASSET_CACHE = 'taskdeck-static-assets'

// Handshake shared with src/pwa/legacyApiCacheWorker.ts. A pre-#2350 worker has
// no listener for the query, and the #2350 worker's old acknowledgement is not
// accepted, so the page can force this one to activate before sign-in.
const TASKDECK_API_CACHE_POLICY_QUERY = 'taskdeck:api-cache-policy'
// The old acknowledgement 'legacy-api-cache-retired' is deliberately not reused.
const TASKDECK_API_CACHE_POLICY_RETIRED = 'taskdeck-api-cache-policy-v2'
const TASKDECK_API_CACHE_SKIP_WAITING = 'taskdeck:skip-waiting'

self.addEventListener('message', (event) => {
  const message = event.data
  if (!message || typeof message !== 'object') return

  if (message.type === TASKDECK_API_CACHE_SKIP_WAITING) {
    // Only the page's security migration sends this. Normal updates still wait
    // for the SwUpdatePrompt banner, so registerType 'prompt' keeps its meaning.
    self.skipWaiting()
    return
  }

  if (message.type !== TASKDECK_API_CACHE_POLICY_QUERY) return
  const port = event.ports && event.ports[0]
  if (port) port.postMessage({ policy: TASKDECK_API_CACHE_POLICY_RETIRED })
})

// Marker cache proving this migration already ran for this origin. Deliberately
// NOT under the `taskdeck-api-cache` prefix, which the sweep below deletes.
const TASKDECK_MIGRATION_MARKER_CACHE = 'taskdeck-pwa-cache-policy-v2'

let retirement = null

async function retireCaches() {
  if (await caches.has(TASKDECK_MIGRATION_MARKER_CACHE)) return

  const cacheNames = await caches.keys()
  await Promise.all(
    cacheNames
      .filter((cacheName) => cacheName.startsWith(TASKDECK_LEGACY_API_CACHE_PREFIX))
      .map((cacheName) => caches.delete(cacheName)),
  )
  await caches.delete(TASKDECK_STATIC_ASSET_CACHE)

  // Written last: a partial sweep must not be recorded as a completed migration,
  // or a retry would be suppressed while an identity-bound entry still survived.
  await caches.open(TASKDECK_MIGRATION_MARKER_CACHE)
}

/**
 * Deduplicated so the activation hook and the evaluation-time call below share one
 * sweep instead of racing two of them over the same cache names.
 */
function retireCachesOnce() {
  if (!retirement) {
    retirement = retireCaches().catch((error) => {
      // Cleared so a later activation or worker restart retries rather than
      // memoising a failure that left the vulnerable namespace in place.
      retirement = null
      console.warn('Unable to retire legacy Taskdeck runtime caches.', error)
      throw error
    })
  }
  return retirement
}

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    await retireCachesOnce()
    try {
      // A page loaded under the vulnerable worker keeps getting API replay until
      // something takes over its fetches; waiting for a reload is not a guarantee.
      await self.clients.claim()
    } catch {
      console.warn('Unable to claim open clients after retiring legacy API caches.')
    }
  })())
})

// The generated worker loads this file with `importScripts()` from inside
// vite-plugin-pwa's asynchronous AMD `define()` factory, which runs in a promise
// continuation rather than during the worker's synchronous initial evaluation. By
// the time the listener above is attached the `activate` event has already been
// dispatched, so it never fires and the sweep never runs - measured in Chromium,
// where a seeded `taskdeck-static-assets` entry survived the whole migration while
// this same file's `message` listener answered normally. Running the sweep at
// evaluation time is what actually retires the caches; the listener above stays so
// the cleanup is still bound to activation on any build whose worker imports this
// file synchronously. The marker cache keeps it a one-time migration rather than a
// purge on every worker restart.
void retireCachesOnce().catch(() => {
  // Already reported; activation retries.
})
