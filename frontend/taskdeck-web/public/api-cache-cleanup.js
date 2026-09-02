// Runtime caches are not covered by Workbox's cleanupOutdatedCaches. This
// activation hook retires the entire legacy API namespace without touching the
// explicit share-target offline queue.
const TASKDECK_LEGACY_API_CACHE_PREFIX = 'taskdeck-api-cache'

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
      // A page loaded under the vulnerable worker keeps getting API replay until
      // something takes over its fetches; waiting for a reload is not a guarantee.
      await self.clients.claim()
    } catch {
      console.warn('Unable to claim open clients after retiring legacy API caches.')
    }
  })())
})
