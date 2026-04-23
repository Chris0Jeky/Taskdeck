import axios, { AxiosHeaders, type AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { isTokenExpired } from '../utils/jwt'
import { createRequestId } from '../utils/requestId'
import { isAuthRoutePath } from '../utils/navigation'
import { isDemoMode } from '../utils/demoMode'
import * as tokenStorage from '../utils/tokenStorage'
import { logError, logWarn } from '../utils/errorReporting'
import {
  MAX_RETRIES,
  computeRetryDelay,
  isRetryableError,
  type RetryableRequestConfig,
} from './httpRetry'

const REQUEST_ID_HEADER = 'X-Request-Id'

function ensureRequestIdHeader(config: InternalAxiosRequestConfig): void {
  const headers = AxiosHeaders.from(config.headers)
  if (!headers.get(REQUEST_ID_HEADER)) {
    headers.set(REQUEST_ID_HEADER, createRequestId())
  }
  config.headers = headers
}

const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api',
  headers: {
    'Content-Type': 'application/json',
  },
})

// Request interceptor for auth token
http.interceptors.request.use(
  (config) => {
    ensureRequestIdHeader(config)

    const token = tokenStorage.getToken()
    if (token) {
      if (isTokenExpired(token)) {
        tokenStorage.clearAll()
      } else {
        config.headers.Authorization = `Bearer ${token}`
      }
    }
    return config
  },
  (error) => Promise.reject(error)
)

// Response interceptor for error handling
http.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response) {
      logError('API Error:', error)

      // Handle 401 - clear session and redirect to login (skip in demo mode).
      // Callers can set `skipAuth401` on the request config to suppress this
      // behaviour (e.g. token refresh attempts that want to handle 401 locally).
      const skipAuth401 = (error.config as Record<string, unknown> | undefined)?.skipAuth401 === true
      if (error.response.status === 401 && !isDemoMode && !skipAuth401) {
        tokenStorage.clearAll()
        const pathname = window.location.pathname
        const currentPath = `${pathname}${window.location.search}`
        if (!isAuthRoutePath(pathname)) {
          window.location.href = `/login?redirect=${encodeURIComponent(currentPath)}`
        }
      }
    } else if (error.request) {
      logError('Network Error:', error)
    } else {
      logError('Error:', error)
    }
    return Promise.reject(error)
  }
)

/**
 * Retry interceptor (issue #854 / FE-15).
 *
 * Response rejection handlers fire in registration order, so this handler
 * runs AFTER the 401/error-logging handler above — the 401 handler sees the
 * original transient failure first (harmless logging, no-op since it is not
 * a 401), then we decide whether to retry. On a retryable transient failure
 * (5xx, 429, network/timeout) against an idempotent method, we wait with
 * exponential backoff (honouring Retry-After for 429) and re-issue the
 * request via `http.request(config)`. Non-retryable errors (4xx other than
 * 429, non-idempotent methods, exhausted retries, cancellations) reject,
 * so the caller sees the terminal error. 401 handling is preserved because
 * 401 is never retryable, so the first handler's redirect logic runs
 * normally before we pass the rejection through.
 */
http.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const config = error.config as (RetryableRequestConfig & InternalAxiosRequestConfig) | undefined
    if (!config) return Promise.reject(error)

    // Short-circuit: caller opted out explicitly. Keeps the interceptor
    // transparent for tests and for fail-fast background polls.
    if (config.skipRetry) return Promise.reject(error)

    if (!isRetryableError(error)) return Promise.reject(error)

    const attempt = (config.__retryCount ?? 0) + 1
    if (attempt > MAX_RETRIES) return Promise.reject(error)
    config.__retryCount = attempt

    const delay = computeRetryDelay(error, attempt)
    if (import.meta.env.DEV) {
      const status = error.response?.status ?? 'network'
      logWarn(
        `[http] retry ${attempt}/${MAX_RETRIES} for ${config.method?.toUpperCase()} ${config.url} ` +
          `after ${delay}ms (status=${status})`,
      )
    }

    // Race the backoff wait against the request's AbortSignal so we don't
    // hold the promise open for up to 60s on a cancelled request. Reject
    // with axios.CanceledError so callers can discriminate via
    // axios.isCancel(err) instead of treating a stale 5xx as user error.
    const signal = config.signal as AbortSignal | undefined
    if (signal?.aborted) {
      return Promise.reject(new axios.CanceledError('Request aborted before retry'))
    }
    try {
      await new Promise<void>((resolve, reject) => {
        const timer = setTimeout(resolve, delay)
        if (signal) {
          const onAbort = () => {
            clearTimeout(timer)
            signal.removeEventListener('abort', onAbort)
            reject(new axios.CanceledError('Request aborted while waiting to retry'))
          }
          signal.addEventListener('abort', onAbort, { once: true })
        }
      })
    } catch (abortErr) {
      return Promise.reject(abortErr)
    }
    // Double-check (defensive): some adapters may fire abort synchronously
    // right at the edge of the timer resolving.
    if (signal?.aborted) {
      return Promise.reject(new axios.CanceledError('Request aborted while waiting to retry'))
    }
    return http.request(config)
  },
)

export default http
