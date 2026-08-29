/**
 * Auth-expiry notice (GH-2142).
 *
 * The response interceptor's 401 handling ends in a full document navigation
 * to `/login`, which tears down every component before any of them can react.
 * This is the one synchronous beat before that: `notifyAuthExpired()` is
 * dispatched immediately BEFORE `window.location.href` is assigned, so a
 * surface holding unsaved user input (the Paper capture composer) can persist
 * it — `window.dispatchEvent` runs its listeners synchronously, so the work is
 * done by the time the navigation is queued.
 *
 * Listeners must therefore stay cheap and synchronous. Anything awaited here
 * is racing a page teardown it will lose.
 */

export const AUTH_EXPIRED_EVENT = 'taskdeck:auth-expired'

/** Fire the notice. No-op outside a browser context. */
export function notifyAuthExpired(): void {
  if (typeof window === 'undefined' || typeof window.dispatchEvent !== 'function') return
  try {
    window.dispatchEvent(new CustomEvent(AUTH_EXPIRED_EVENT))
  } catch {
    // A listener that throws must never stop the redirect: losing the session
    // is the primary event, stashing the draft is the courtesy.
  }
}

/**
 * Subscribe to the notice. Returns an unsubscribe function — call it from
 * `onUnmounted` so a torn-down surface cannot stash on a later expiry.
 */
export function onAuthExpired(handler: () => void): () => void {
  if (typeof window === 'undefined' || typeof window.addEventListener !== 'function') {
    return () => undefined
  }
  const listener = () => handler()
  window.addEventListener(AUTH_EXPIRED_EVENT, listener)
  return () => window.removeEventListener(AUTH_EXPIRED_EVENT, listener)
}
