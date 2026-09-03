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

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    try {
      const cacheNames = await caches.keys()
      await Promise.all(
        cacheNames
          .filter((cacheName) => cacheName.startsWith(TASKDECK_LEGACY_API_CACHE_PREFIX))
          .map((cacheName) => caches.delete(cacheName)),
      )
    } catch {
      console.warn('Unable to remove legacy API caches during activation.')
      throw new Error('Legacy API cache cleanup failed.')
    }

    try {
      await caches.delete(TASKDECK_STATIC_ASSET_CACHE)
    } catch {
      console.warn('Unable to invalidate the static asset cache during activation.')
      throw new Error('Static asset cache cleanup failed.')
    }

    try {
      // A page loaded under the vulnerable worker keeps getting API replay until
      // something takes over its fetches; waiting for a reload is not a guarantee.
      await self.clients.claim()
    } catch {
      console.warn('Unable to claim open clients after retiring legacy API caches.')
    }
  })())
})
