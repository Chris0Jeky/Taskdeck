import { logWarn } from '../utils/errorReporting'

/**
 * Handshake shared with `public/api-cache-cleanup.js`. The literals are duplicated
 * because the worker script is plain JavaScript served from `public/` and cannot
 * import from `src/`; `src/tests/pwa/legacyApiCacheWorker.spec.ts` pins them against
 * the real worker source.
 */
export const API_CACHE_POLICY_QUERY = 'taskdeck:api-cache-policy'
export const API_CACHE_POLICY_RETIRED = 'legacy-api-cache-retired'
export const API_CACHE_SKIP_WAITING = 'taskdeck:skip-waiting'

const HANDSHAKE_TIMEOUT_MS = 1_500
const UPDATE_TIMEOUT_MS = 5_000
const ACTIVATION_TIMEOUT_MS = 6_000
/**
 * Hard ceiling on the whole migration. Session restore and the router guard both
 * await it, so an unbounded step here would pin the app on its loading state with a
 * reload that re-enters the same wait.
 *
 * Every individual budget above is also clamped to what remains of this deadline. A
 * ceiling that only bounded the *promise* would let `retire()` keep running after its
 * caller was told the attempt failed - and then reach `unregister()` while a retry
 * started by the next navigation was mid-handshake, tearing the registration out from
 * under it and hard-blocking the user.
 */
const RETIREMENT_DEADLINE_MS = 12_000

/** Time budget shared by every step of one migration attempt. */
function deadlineFrom(totalMs: number): { remaining: (budgetMs: number) => number; expired: () => boolean } {
  const expiresAt = Date.now() + totalMs
  const left = () => Math.max(0, expiresAt - Date.now())
  return {
    remaining: (budgetMs: number) => Math.min(budgetMs, left()),
    expired: () => left() <= 0,
  }
}

let activeRetirement: Promise<boolean> | null = null

function withTimeout<T>(work: Promise<T>, timeoutMs: number, fallback: T): Promise<T> {
  return new Promise((resolve) => {
    let settled = false
    const finish = (value: T) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      resolve(value)
    }
    const timer = setTimeout(() => finish(fallback), timeoutMs)
    work.then((value) => finish(value), () => finish(fallback))
  })
}

function asksRetirementPolicy(worker: ServiceWorker, timeoutMs: number): Promise<boolean> {
  return new Promise((resolve) => {
    let settled = false
    const channel = new MessageChannel()
    const finish = (retired: boolean) => {
      if (settled) return
      settled = true
      try {
        channel.port1.close()
      } catch {
        // A closed port is already in the state this cleanup wants.
      }
      resolve(retired)
    }
    // A pre-#2350 worker has no handler for this message, so silence is the
    // answer that matters: absence of the policy means the worker can still
    // replay authenticated API responses.
    const timer = setTimeout(() => finish(false), timeoutMs)
    channel.port1.onmessage = (event: MessageEvent) => {
      clearTimeout(timer)
      finish((event.data as { policy?: string } | null)?.policy === API_CACHE_POLICY_RETIRED)
    }
    try {
      worker.postMessage({ type: API_CACHE_POLICY_QUERY }, [channel.port2])
    } catch {
      clearTimeout(timer)
      finish(false)
    }
  })
}

/**
 * Latches `controllerchange` from the moment it is created rather than from the
 * moment it is awaited. Another tab's migration claims every client, so the event
 * can land while this tab is still mid-handshake; a listener attached later would
 * miss it and then tear down a perfectly good registration.
 */
function controllerChangeLatch(): { wait: (timeoutMs: number) => Promise<boolean>; dispose: () => void } {
  let fired = false
  let notify: (() => void) | null = null
  const onChange = () => {
    fired = true
    notify?.()
  }
  navigator.serviceWorker.addEventListener('controllerchange', onChange)
  return {
    wait(timeoutMs: number) {
      if (fired) return Promise.resolve(true)
      return new Promise((resolve) => {
        const timer = setTimeout(() => {
          notify = null
          resolve(false)
        }, timeoutMs)
        notify = () => {
          clearTimeout(timer)
          resolve(true)
        }
      })
    },
    dispose() {
      navigator.serviceWorker.removeEventListener('controllerchange', onChange)
    },
  }
}

function sendSkipWaiting(worker: ServiceWorker | null): void {
  try {
    worker?.postMessage({ type: API_CACHE_SKIP_WAITING })
  } catch {
    // The worker went redundant between the state check and the post.
  }
}

/**
 * Resolves true once a replacement worker exists and has been told to skip waiting
 * (or is already past installing and may take over on its own), false when the wait
 * expired without one appearing.
 *
 * `registration.update()` resolves as soon as the update job is *started* - the
 * spec resolves the job promise inside Install, before the install event's
 * lifetime promises settle - so `registration.waiting` is normally still null when
 * it returns and a one-shot read there would never deliver the message. The
 * replacement has to be followed through `updatefound` and `statechange` instead.
 *
 * The verdict matters as well as the wait: when no replacement is coming there is
 * nothing for a `controllerchange` to announce, so the caller can stop waiting and
 * spend its remaining budget on the cleanup instead.
 */
function forceReplacementToActivate(
  registration: ServiceWorkerRegistration,
  timeoutMs: number,
): Promise<boolean> {
  return new Promise((resolve) => {
    let settled = false
    // Held so the worker's own listener is removed too: on a timeout it would otherwise
    // outlive this promise and send skip-waiting to a migration already abandoned.
    let followed: { worker: ServiceWorker; onStateChange: () => void } | null = null
    const finish = (found: boolean) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      registration.removeEventListener('updatefound', onUpdateFound)
      followed?.worker.removeEventListener('statechange', followed.onStateChange)
      followed = null
      resolve(found)
    }
    const timer = setTimeout(() => finish(false), timeoutMs)

    const follow = (worker: ServiceWorker | null) => {
      if (!worker) return
      if (worker.state === 'installed') {
        sendSkipWaiting(worker)
        finish(true)
        return
      }
      if (worker.state !== 'installing') {
        // Already 'activating', 'activated' or 'redundant': no further statechange is
        // coming, so waiting out the budget would only eat the shared deadline.
        finish(worker.state !== 'redundant')
        return
      }
      const onStateChange = () => {
        if (worker.state === 'installing') return
        // 'activated' means it took over on its own; 'redundant' means it never will.
        if (worker.state === 'installed') sendSkipWaiting(worker)
        finish(worker.state !== 'redundant')
      }
      followed = { worker, onStateChange }
      worker.addEventListener('statechange', onStateChange)
    }

    const onUpdateFound = () => follow(registration.installing)
    registration.addEventListener('updatefound', onUpdateFound)

    // A replacement may already be waiting (the user dismissed the update banner)
    // or installing (another tab started the same migration).
    if (registration.waiting) {
      sendSkipWaiting(registration.waiting)
      finish(true)
      return
    }
    follow(registration.installing)
  })
}

async function retire(deadline: ReturnType<typeof deadlineFrom>): Promise<boolean> {
  const controller = navigator.serviceWorker.controller
  // Nothing is intercepting this page's requests, so no worker can serve a
  // cached API response to it.
  if (!controller) return true

  const latch = controllerChangeLatch()
  try {
    if (await asksRetirementPolicy(controller, deadline.remaining(HANDSHAKE_TIMEOUT_MS))) return true

    // Another tab may have completed the migration while that handshake was
    // pending; the new worker claims every client, including this one.
    const claimed = navigator.serviceWorker.controller
    if (
      claimed
      && claimed !== controller
      && (await asksRetirementPolicy(claimed, deadline.remaining(HANDSHAKE_TIMEOUT_MS)))
    ) {
      return true
    }

    // The controlling worker predates the API-cache retirement, so it repopulates
    // the authenticated namespace on every request and the page-side purge alone is
    // not an upgrade. `registerType: 'prompt'` lets a replacement wait indefinitely -
    // the update banner is never shown on the public login route, and a user can
    // dismiss it - so the migration is forced here instead of being left to the UI.
    const registration = await navigator.serviceWorker.getRegistration()
    if (!registration) {
      // Unregistering never releases a page the worker already controls, so a
      // missing registration is only safe when nothing controls this page. A
      // controller that did not answer the handshake still intercepts every fetch.
      return navigator.serviceWorker.controller === null
    }

    const forced = forceReplacementToActivate(registration, deadline.remaining(ACTIVATION_TIMEOUT_MS))
    // Bounded: a stalled-but-open update fetch would otherwise never settle, and
    // every caller of the purge shares this one promise.
    await withTimeout(registration.update(), deadline.remaining(UPDATE_TIMEOUT_MS), undefined)
    const foundReplacement = await forced

    // No replacement appeared, so there is nothing for a `controllerchange` to
    // announce. Skipping that wait is what keeps the cleanup below inside the shared
    // deadline instead of being starved by a wait that cannot succeed.
    if (foundReplacement && (await latch.wait(deadline.remaining(ACTIVATION_TIMEOUT_MS)))) {
      const replacement = navigator.serviceWorker.controller
      if (!replacement) return true
      if (await asksRetirementPolicy(replacement, deadline.remaining(HANDSHAKE_TIMEOUT_MS))) return true
    }

    if (registration.installing || registration.waiting) {
      // A replacement is on its way in. Unregistering would destroy it, and a
      // reload lets it take over, so fail closed without touching it.
      logWarn('A replacement service worker is installing or waiting; reload before signing in.')
      return false
    }

    if (deadline.expired()) {
      // Out of budget. Unregistering now would land after this attempt's caller has
      // already been told it failed, and could pull the registration out from under a
      // retry that the next navigation has already started.
      logWarn('The service worker migration ran out of time; reload before signing in.')
      return false
    }

    // Nothing is coming. Removing the registration stops the legacy worker from
    // ever controlling this origin again, but it keeps controlling the *current*
    // page until a reload, so this attempt still fails closed.
    try {
      await registration.unregister()
    } catch {
      // Reported below with the same operator-facing warning as any other failure.
    }
    logWarn('A retired service worker still controls this page; reload before signing in.')
    return false
  } finally {
    latch.dispose()
  }
}

/**
 * Resolves true once no service worker can replay authenticated API responses to
 * this page, forcing a waiting replacement to activate when one is available.
 *
 * Deduplicated like the cache purge: concurrent callers share one migration, and a
 * failed attempt is not memoised so a later navigation can retry.
 */
export function retireLegacyApiCacheWorker(): Promise<boolean> {
  if (typeof navigator === 'undefined' || !('serviceWorker' in navigator)) return Promise.resolve(true)
  if (activeRetirement) return activeRetirement

  const deadline = deadlineFrom(RETIREMENT_DEADLINE_MS)
  activeRetirement = withTimeout(
    retire(deadline).catch(() => {
      logWarn('The service worker migration failed unexpectedly; reload before signing in.')
      return false
    }),
    RETIREMENT_DEADLINE_MS,
    false,
  ).finally(() => {
    activeRetirement = null
  })
  return activeRetirement
}
