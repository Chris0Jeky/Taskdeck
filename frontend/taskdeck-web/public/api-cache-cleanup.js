// Runtime caches are not covered by Workbox's cleanupOutdatedCaches. This
// activation hook retires the entire legacy API namespace without touching the
// explicit share-target offline queue.
//
// The generated worker must load this file with a TOP-LEVEL `importScripts` so the `activate`
// listener below is attached during the worker's initial synchronous evaluation and cannot miss
// its event. vite-plugin-pwa emits that call inside its asynchronous AMD factory, i.e. from a
// promise continuation, which makes the attachment depend on the browser draining microtasks
// before it dispatches the lifecycle event - something no specification promises. The build hoists
// the call out of the factory and fails if it cannot; see src/pwa/hoistWorkerImportScripts.ts and
// the structural case in tests/pwa-generated-worker.spec.ts (#2639).
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
    // whole threat model. Activation therefore always re-sweeps.
    //
    // What the `waitUntil` buys, precisely: Handle Fetch holds every request until the
    // worker leaves the `activating` state, so no response can be served out of a
    // half-swept cache. It does NOT make activation fail - the spec only aborts on a
    // rejected INSTALL, never on a rejected activate - so a rejection here is surfaced
    // (console warning plus an unhandled rejection) and the worker still activates. A
    // sweep that cannot complete therefore leaves the old entries in place; documented
    // as a residual in docs/platform/PWA_OFFLINE_BEHAVIOR.md.
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

// Evaluation-time sweep. It runs while the replacement is INSTALLING, so it retires the
// vulnerable namespaces as early as possible - but the old worker still controls every open
// page at that moment and can re-admit an entry afterwards, which is why the `activate`
// listener above re-sweeps with `force: true` inside `event.waitUntil`. The marker cache keeps
// THIS pass a one-time migration rather than a purge on every worker restart; the forced pass
// deliberately ignores it.
//
// #2639 reported that the `activate` listener never received its event, because
// vite-plugin-pwa emitted the `importScripts` for this file inside its asynchronous AMD factory
// (measured on PR #2416 as `__proofActivateFired: false`). Re-measured 2026-09-05 with breadcrumb
// caches in Chromium 151.0.7922.34 (Playwright 1.62.1): the listener DID receive its event on every run -
// first install, waiting-then-skip-waiting, and a worker killed over CDP and restarted straight
// into activation, 3 of 3 each. The factory's microtask drains before the lifecycle event is
// dispatched in that engine. It is not guaranteed to, in any engine, which is why the build now
// hoists the call to the top of the generated worker (src/pwa/hoistWorkerImportScripts.ts): the
// listener is attached during initial evaluation and the ordering stops being a race.
void retireCachesOnce().catch(() => {
  // Already reported; the forced sweep at activation retries.
})
