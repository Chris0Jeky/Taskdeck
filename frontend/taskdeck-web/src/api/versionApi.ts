import http from './http'
import type { RetryableRequestConfig } from './httpRetry'

/**
 * Product-version lookup (#1948).
 *
 * The single source of truth for the version the UI displays is the version the
 * **running backend** reports from `GET /health/live` — the value #1804 stamps
 * from the release tag (`/p:Version` -> `AssemblyInformationalVersion` ->
 * `ProductVersion.Value`, covered by `HealthApiTests` and `ProductVersionTests`).
 *
 * The frontend deliberately keeps **no version literal of its own**. The retired
 * `v0.7.2` default on `PaperSidebar`'s `version` prop (copied from the Paper
 * design mock) drifted silently from the shipped tag and made every dogfooding
 * report unattributable to a build; a constant here would do it again.
 *
 * `/health/live` is anonymous and lives at the server root, *outside* the `/api`
 * prefix carried by `http`'s baseURL, so the request overrides baseURL with the
 * API root — the same derivation `useBoardRealtime.resolveHubUrl()` uses to
 * reach `/hubs/boards`.
 */

/** Shape of the anonymous `GET /health/live` payload this module consumes. */
export interface LiveHealthResponse {
  status?: string
  version?: string
  timestamp?: string
}

/**
 * Server root for endpoints that sit outside the `/api` prefix. Mirrors
 * `useBoardRealtime.resolveHubUrl()`; returns `''` for the packaged deployment
 * (`VITE_API_BASE_URL=/api`), which keeps the request same-origin and relative.
 */
export function resolveApiRoot(): string {
  const apiBase = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api'
  return apiBase.replace(/\/api\/?$/i, '')
}

export const versionApi = {
  /**
   * Reads the stamped product version from the running backend.
   * Returns `null` when the payload carries no usable version, so callers can
   * render nothing rather than inventing a value. Transport failures reject —
   * the caller decides what an unreachable backend should look like.
   */
  async getProductVersion(): Promise<string | null> {
    const config: RetryableRequestConfig = {
      baseURL: resolveApiRoot(),
      // Fail fast: a cosmetic footer stamp must not hold a retry/backoff chain
      // open for seconds against a backend that is down.
      skipRetry: true,
    }
    const { data } = await http.get<LiveHealthResponse>('/health/live', config)
    const version = typeof data?.version === 'string' ? data.version.trim() : ''
    return version.length > 0 ? version : null
  },
}
