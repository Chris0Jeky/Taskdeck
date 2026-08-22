import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { versionApi } from '../api/versionApi'
import { isDemoMode } from '../utils/demoMode'
import { logWarn } from '../utils/errorReporting'

/**
 * Shared product-version state for every surface that displays "what am I
 * running?" (#1948).
 *
 * Source of truth: the running backend's stamped version, read once per app
 * session from `GET /health/live` (see `api/versionApi.ts` for the full chain
 * back to the release tag). There is intentionally **no literal fallback** —
 * when the version cannot be established the surface renders nothing, because
 * a wrong version string is worse than no version string: it silently
 * misattributes every bug report filed against that build.
 */

const version = ref<string | null>(null)
let loadPromise: Promise<void> | null = null

/** `0.1.1` -> `v0.1.1`; an already-prefixed value is left alone. */
function toDisplayVersion(raw: string | null): string | null {
  if (raw === null) return null
  return /^v/i.test(raw) ? raw : `v${raw}`
}

async function loadProductVersion(): Promise<void> {
  if (isDemoMode) {
    // Demo builds ship without a backend (VITE_API_BASE_URL is empty), so no
    // version can be established. Render nothing rather than guess.
    version.value = null
    return
  }

  try {
    version.value = await versionApi.getProductVersion()
    if (version.value === null) {
      // Reached the backend, but the payload carried no usable version — a
      // reverse proxy answering `/health/live` with the SPA's index.html looks
      // exactly like this. The memo deliberately stands (the answer was a real
      // one, not a transport failure), so without this line the footer is
      // silently empty for the whole session with nothing to diagnose it by.
      logWarn('[version] /health/live returned no usable version')
    }
  } catch (error) {
    version.value = null
    // Clear the memo so a later mount can retry a backend that was merely
    // starting up; the stamp is cosmetic, so failure is a warning, not an error.
    loadPromise = null
    logWarn('[version] product version unavailable', error)
  }
}

/** Starts the one-shot load; concurrent callers share the same request. */
export function ensureProductVersionLoaded(): Promise<void> {
  loadPromise ??= loadProductVersion()
  return loadPromise
}

/**
 * Test-only: drops the cached version and in-flight memo so each spec starts
 * from a clean module state.
 */
export function resetProductVersionForTests(): void {
  version.value = null
  loadPromise = null
}

export interface ProductVersionState {
  /** Raw version reported by the backend, or `null` when it is unknown. */
  version: Ref<string | null>
  /** Display form (`v`-prefixed), or `null` when nothing should be rendered. */
  displayVersion: ComputedRef<string | null>
  /** Re-exposed loader, for callers that want to await the first read. */
  ensureLoaded: () => Promise<void>
}

/**
 * Reading the version is enough to request it: the load is memoized, so any
 * number of consumers cost at most one `/health/live` request per session.
 */
export function useProductVersion(): ProductVersionState {
  void ensureProductVersionLoaded()

  return {
    version,
    displayVersion: computed(() => toDisplayVersion(version.value)),
    ensureLoaded: ensureProductVersionLoaded,
  }
}
