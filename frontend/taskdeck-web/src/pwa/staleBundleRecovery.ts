import { logWarn } from '../utils/errorReporting'

const RELOADED_MARKER = 'taskdeck:stale-bundle-reloaded'

/**
 * Recovers a page whose lazy route chunks no longer exist.
 *
 * The API-cache migration forces a replacement service worker to activate while the
 * page is still running the *old* bundle. Workbox's `cleanupOutdatedCaches` then drops
 * the old precache and the new worker claims this client, so an
 * `import('./views/Something.vue')` for a chunk named with the previous build hash
 * resolves to a URL neither the precache nor the deployed server still has. Without a
 * handler the navigation just fails and the user is stuck until they reload by hand.
 *
 * Reloading is safe here because the new bundle is already installed; it is guarded by
 * a session-scoped marker so a genuinely broken chunk cannot produce a reload loop.
 */
export function installStaleBundleRecovery(reload: () => void = () => window.location.reload()): () => void {
  const onPreloadError = (event: Event) => {
    event.preventDefault()
    let alreadyReloaded: boolean
    try {
      alreadyReloaded = sessionStorage.getItem(RELOADED_MARKER) === '1'
      sessionStorage.setItem(RELOADED_MARKER, '1')
    } catch {
      // Private-mode storage refusals must not turn a recoverable state into a loop:
      // without a marker we cannot prove this is the first attempt, so do not reload.
      logWarn('A route chunk failed to load and recovery state is unavailable; reload the page.')
      return
    }
    if (alreadyReloaded) {
      logWarn('A route chunk failed to load again after reloading; the deployment may be incomplete.')
      return
    }
    reload()
  }

  window.addEventListener('vite:preloadError', onPreloadError)
  return () => window.removeEventListener('vite:preloadError', onPreloadError)
}

/** Clears the guard once the app has rendered, so a later deploy can recover again. */
export function clearStaleBundleRecoveryMarker(): void {
  try {
    sessionStorage.removeItem(RELOADED_MARKER)
  } catch {
    // Nothing to clear when storage is unavailable.
  }
}
