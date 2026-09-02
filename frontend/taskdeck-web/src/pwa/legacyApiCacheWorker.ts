import { logWarn } from '../utils/errorReporting'

/**
 * Handshake shared with `public/api-cache-cleanup.js`. The literals are duplicated
 * because the worker script is plain JavaScript served from `public/` and cannot
 * import from `src/`; `src/tests/pwa/apiCacheWorkerContract.spec.ts` pins them.
 */
export const API_CACHE_POLICY_QUERY = 'taskdeck:api-cache-policy'
export const API_CACHE_POLICY_RETIRED = 'legacy-api-cache-retired'
export const API_CACHE_SKIP_WAITING = 'taskdeck:skip-waiting'

const HANDSHAKE_TIMEOUT_MS = 2_000
const ACTIVATION_TIMEOUT_MS = 10_000

let activeRetirement: Promise<boolean> | null = null

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

function waitForControllerChange(timeoutMs: number): Promise<boolean> {
  return new Promise((resolve) => {
    let settled = false
    const finish = (changed: boolean) => {
      if (settled) return
      settled = true
      navigator.serviceWorker.removeEventListener('controllerchange', onChange)
      resolve(changed)
    }
    const onChange = () => {
      clearTimeout(timer)
      finish(true)
    }
    const timer = setTimeout(() => finish(false), timeoutMs)
    navigator.serviceWorker.addEventListener('controllerchange', onChange)
  })
}

async function retire(): Promise<boolean> {
  const controller = navigator.serviceWorker.controller
  // Nothing is intercepting this page's requests, so no worker can serve a
  // cached API response to it.
  if (!controller) return true
  if (await asksRetirementPolicy(controller, HANDSHAKE_TIMEOUT_MS)) return true

  // The controlling worker predates the API-cache retirement, so it repopulates
  // the authenticated namespace on every request and the page-side purge alone is
  // not an upgrade. `registerType: 'prompt'` lets a replacement wait indefinitely -
  // the update banner is never shown on the public login route, and a user can
  // dismiss it - so the migration is forced here instead of being left to the UI.
  const registration = await navigator.serviceWorker.getRegistration()
  if (!registration) return true

  const controllerChanged = waitForControllerChange(ACTIVATION_TIMEOUT_MS)
  try {
    await registration.update()
  } catch {
    // An update fetch can fail offline; the waiting worker below may still exist.
  }
  registration.waiting?.postMessage({ type: API_CACHE_SKIP_WAITING })

  if (await controllerChanged) {
    const replacement = navigator.serviceWorker.controller
    if (replacement && (await asksRetirementPolicy(replacement, HANDSHAKE_TIMEOUT_MS))) return true
  }

  // No replacement took over. Unregistering stops the legacy worker from ever
  // controlling this origin again, but it keeps controlling the *current* page
  // until a reload, so this attempt still fails closed.
  try {
    await registration.unregister()
  } catch {
    // Reported below with the same operator-facing warning as any other failure.
  }
  logWarn('A retired service worker still controls this page; reload before signing in.')
  return false
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

  activeRetirement = retire()
    .catch(() => false)
    .finally(() => {
      activeRetirement = null
    })
  return activeRetirement
}
