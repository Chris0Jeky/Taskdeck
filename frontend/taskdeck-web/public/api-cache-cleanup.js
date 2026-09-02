// Runtime caches are not covered by Workbox's cleanupOutdatedCaches. This
// activation hook retires the entire legacy API namespace without touching the
// explicit share-target offline queue.
const TASKDECK_LEGACY_API_CACHE_PREFIX = 'taskdeck-api-cache'

// The pre-#2350 static-asset route matched on file extension alone, so a
// deployment with a prefixed API base could have stored an authenticated response
// such as `/taskdeck/api/users/by-username/alice.png` in this cache, where it
// survives an account switch for 30 days. The current route can no longer serve
// it, but the stored response is still user A's data sitting in user B's browser,
// so every entry the current policy would refuse is evicted on activation.
const TASKDECK_STATIC_ASSET_CACHE = 'taskdeck-static-assets'
const TASKDECK_STATIC_ASSET_PATH =
  /^\/(?:assets|icons)\/[^?#]*\.(?:png|jpg|jpeg|svg|gif|webp|ico|woff|woff2)$/i

// Handshake shared with src/pwa/legacyApiCacheWorker.ts. A pre-#2350 worker has
// no listener for the query, so the page can tell a retired worker from one that
// still replays authenticated API responses, and can force this one to activate.
const TASKDECK_API_CACHE_POLICY_QUERY = 'taskdeck:api-cache-policy'
const TASKDECK_API_CACHE_POLICY_RETIRED = 'legacy-api-cache-retired'
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
      // Do not leave a newly deployed worker stuck activating because browser
      // cache storage is temporarily unavailable. Identity transitions still
      // fail closed in the page before accepting a replacement session.
      console.warn('Unable to remove legacy API caches during activation.')
    }

    try {
      if (await caches.has(TASKDECK_STATIC_ASSET_CACHE)) {
        const cache = await caches.open(TASKDECK_STATIC_ASSET_CACHE)
        const requests = await cache.keys()
        await Promise.all(
          requests
            .filter((request) => !TASKDECK_STATIC_ASSET_PATH.test(new URL(request.url).pathname))
            .map((request) => cache.delete(request)),
        )
      }
    } catch {
      console.warn('Unable to evict non-asset entries from the static asset cache during activation.')
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
