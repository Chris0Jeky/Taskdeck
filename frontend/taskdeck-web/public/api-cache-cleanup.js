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

let evaluationSweep = null

/**
 * @param {{ force?: boolean }} [options] `force` re-sweeps even when the marker cache
 *   says a previous sweep already completed.
 */
async function retireCaches({ force = false } = {}) {
  if (!force && (await caches.has(TASKDECK_MIGRATION_MARKER_CACHE))) return

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

function reportFailure(error) {
  console.warn('Unable to retire legacy Taskdeck runtime caches.', error)
  throw error
}

/**
 * The evaluation-time sweep, memoised so concurrent callers share one pass. The memo
 * is cleared on failure so a later activation or worker restart retries rather than
 * remembering a failure that left the vulnerable namespace in place.
 */
function retireCachesOnce() {
  if (!evaluationSweep) {
    evaluationSweep = retireCaches().catch((error) => {
      evaluationSweep = null
      reportFailure(error)
    })
  }
  return evaluationSweep
}

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    // Deliberately NOT the memoised evaluation-time sweep, and deliberately ignoring
    // the marker. That sweep runs during INSTALL, while the old vulnerable worker is
    // still the controller and can still store an identity-bound response in
    // `taskdeck-static-assets`. Reusing the memo (or short-circuiting on the marker)
    // would let anything cached in that window survive the migration, which is the
    // whole threat model. Activation therefore always re-sweeps, and still fails
    // activation on error so a worker cannot control the page from a partially
    // cleaned state.
    await retireCaches({ force: true }).catch(reportFailure)
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
// this same file's `message` listener answered normally. This evaluation-time sweep
// is what actually retires the caches on such a build; the marker cache keeps it a
// one-time migration rather than a purge on every worker restart.
void retireCachesOnce().catch(() => {
  // Already reported; the forced sweep at activation retries.
})
