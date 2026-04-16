/**
 * HTTP retry helpers for the shared Axios client (issue #854 / FE-15).
 *
 * Retries transient failures (5xx, 429, network errors) on idempotent
 * methods with exponential backoff and jitter. 429 responses honour the
 * `Retry-After` header (numeric seconds or RFC 1123 HTTP-date) up to a
 * safe upper bound.
 *
 * Kept framework-free so it is trivially unit-testable.
 */
import axios, { AxiosError, type AxiosRequestConfig } from 'axios'

/** Maximum number of retry attempts per request (not counting the original). */
export const MAX_RETRIES = 3

/** Base delay in ms before the first retry; doubled each subsequent attempt. */
export const BASE_DELAY_MS = 1000

/**
 * Upper bound on any single wait, including a server-supplied `Retry-After`.
 * Prevents a hostile or malformed header from hanging the UI for minutes.
 */
export const MAX_DELAY_MS = 60_000

/** Idempotent HTTP methods safe to retry without risking duplicate side effects. */
const IDEMPOTENT_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'PUT', 'DELETE'])

/** Extended config type that tracks retry attempt count on the request. */
export interface RetryableRequestConfig extends AxiosRequestConfig {
  __retryCount?: number
}

/** Decide whether a method may be safely retried. Case-insensitive. */
export function isIdempotent(method: string | undefined): boolean {
  if (!method) return false
  return IDEMPOTENT_METHODS.has(method.toUpperCase())
}

/**
 * Parse a Retry-After header value. Accepts either a non-negative integer
 * seconds value or an RFC 1123 HTTP-date. Returns the delay in milliseconds,
 * clamped to [0, MAX_DELAY_MS]. Returns null on any malformed input so the
 * caller can fall back to default backoff.
 */
export function parseRetryAfter(
  value: string | null | undefined,
  now: number = Date.now(),
): number | null {
  if (value == null) return null
  const trimmed = String(value).trim()
  if (trimmed === '') return null

  // Numeric seconds form.
  if (/^\d+(?:\.\d+)?$/.test(trimmed)) {
    const seconds = Number(trimmed)
    if (!Number.isFinite(seconds) || seconds < 0) return null
    return Math.min(seconds * 1000, MAX_DELAY_MS)
  }

  // HTTP-date form.
  const parsed = Date.parse(trimmed)
  if (Number.isNaN(parsed)) return null
  const delta = parsed - now
  if (delta <= 0) return 0
  return Math.min(delta, MAX_DELAY_MS)
}

/**
 * Exponential backoff with +/-25% jitter to avoid thundering-herd
 * synchronisation when many clients recover from the same outage.
 * `attempt` is 1-indexed: first retry uses BASE_DELAY_MS, second uses 2x, etc.
 */
export function computeBackoff(
  attempt: number,
  random: () => number = Math.random,
): number {
  const exponential = BASE_DELAY_MS * Math.pow(2, Math.max(0, attempt - 1))
  const capped = Math.min(exponential, MAX_DELAY_MS)
  // Jitter in [-0.25, +0.25]; avoid going below 0.
  const jitter = (random() - 0.5) * 0.5
  const jittered = Math.max(0, Math.round(capped * (1 + jitter)))
  return Math.min(jittered, MAX_DELAY_MS)
}

/**
 * Whether an Axios error is a transient failure eligible for retry, given
 * the request method and current attempt count. Does NOT treat 401/403/4xx
 * (except 429) as retryable — those should flow through the existing
 * session/error handling.
 */
export function isRetryableError(error: AxiosError): boolean {
  // Cancellation (user navigation, AbortController) must never retry.
  if (axios.isCancel(error)) return false

  const config = error.config as RetryableRequestConfig | undefined
  if (!config || !isIdempotent(config.method)) return false

  // Network / timeout errors: no response object.
  if (!error.response) {
    // Axios sets code === 'ECONNABORTED' for timeouts; both should retry.
    return true
  }

  const status = error.response.status
  if (status === 429) return true
  if (status >= 500 && status < 600) return true
  return false
}

/** Compute delay for a given error/attempt, honouring Retry-After on 429. */
export function computeRetryDelay(
  error: AxiosError,
  attempt: number,
  options: { now?: number; random?: () => number } = {},
): number {
  const now = options.now ?? Date.now()
  const random = options.random ?? Math.random
  const status = error.response?.status
  if (status === 429) {
    const header =
      (error.response?.headers?.['retry-after'] as string | undefined) ??
      (error.response?.headers?.['Retry-After'] as string | undefined)
    const parsed = parseRetryAfter(header, now)
    if (parsed !== null) return parsed
  }
  return computeBackoff(attempt, random)
}
