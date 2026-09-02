// Runtime caches are not covered by Workbox's cleanupOutdatedCaches. This
// activation hook retires the entire legacy API namespace without touching the
// explicit share-target offline queue.
const TASKDECK_LEGACY_API_CACHE_PREFIX = 'taskdeck-api-cache'

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
  })())
})
