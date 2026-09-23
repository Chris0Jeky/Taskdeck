/**
 * Global error reporting plumbing for the Vue app.
 *
 * Responsibilities:
 *   - Install `app.config.errorHandler` as the top-level Vue backstop.
 *   - Install `window.addEventListener('unhandledrejection', ...)` to catch
 *     async rejections that Vue's errorCaptured hook cannot see.
 *   - Install `window.addEventListener('error', ...)` for uncaught runtime
 *     errors thrown outside Vue's render pipeline.
 *   - Provide `logError` / `logWarn` helpers that sanitize console output
 *     in production builds (no response bodies, URLs, or headers leaked).
 *
 * These hooks log to the console and, if the host page has pre-installed
 * Sentry on `window.Sentry`, forward the exception. No new npm dependency
 * is added — the integration is intentionally opt-in and runtime-detected.
 */
import type { App } from 'vue'

type SentryLike = {
  captureException?: (err: unknown, hint?: unknown) => void
}

/** Read a Sentry-like global, if present, without declaring a hard dependency. */
function getSentry(): SentryLike | null {
  const sentry = (globalThis as unknown as { Sentry?: SentryLike }).Sentry
  if (sentry && typeof sentry.captureException === 'function') {
    return sentry
  }
  return null
}

/** Safely forward an error to Sentry. Never throws. */
export function reportToSentry(err: unknown, hint?: unknown): boolean {
  const sentry = getSentry()
  if (!sentry || !sentry.captureException) return false
  try {
    sentry.captureException(sanitizeForSentry(err), hint)
    return true
  } catch {
    // Reporting must never itself propagate.
    return false
  }
}

/**
 * Minimal structural shape of an axios rejection reason.
 *
 * Axios errors carry the full request config (notably
 * config.headers.Authorization) plus request/response bodies. Taskdeck ships
 * no Sentry.init/beforeSend of its own, so anything forwarded here reaches
 * host-configured Sentry unscrubbed. Detect the shape structurally (no axios
 * import) and normalise it before forwarding.
 */
type AxiosLike = {
  isAxiosError?: unknown
  message?: unknown
  config?: { method?: unknown; url?: unknown } | null
  response?: { status?: unknown } | null
}

function isAxiosLike(err: unknown): err is AxiosLike {
  if (!err || typeof err !== 'object') return false
  const candidate = err as AxiosLike
  if (candidate.isAxiosError === true) return true
  return (
    typeof candidate.config === 'object' &&
    candidate.config !== null &&
    typeof candidate.response === 'object' &&
    candidate.response !== null
  )
}

/** Keep only the origin + path of a request URL; drop query and fragment. */
function safeRequestPath(rawUrl: unknown): string | undefined {
  if (typeof rawUrl !== 'string' || rawUrl.length === 0) return undefined
  try {
    const parsed = new URL(rawUrl, 'http://localhost')
    // Opaque schemes carry payloads in pathname; file URLs expose local paths.
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return undefined
    const origin = parsed.origin === 'http://localhost' ? '' : parsed.origin
    return origin + parsed.pathname
  } catch {
    return undefined
  }
}

/**
 * Normalise an axios-shaped rejection into a header/body-free Error.
 *
 * Everything else passes through untouched so Sentry keeps full stack
 * traces for genuine application errors. Never throws.
 */
function sanitizeForSentry(err: unknown): unknown {
  try {
    if (!isAxiosLike(err)) return err
    const status = typeof err.response?.status === 'number' ? err.response.status : undefined
    const method = typeof err.config?.method === 'string' ? err.config.method.toUpperCase() : undefined
    const path = safeRequestPath(err.config?.url)
    const parts: string[] = []
    if (status !== undefined) parts.push('status ' + String(status))
    const target = [method, path].filter(Boolean).join(' ')
    if (target.length > 0) parts.push(target)
    const safe = parts.length > 0 ? new Error('Request failed (' + parts.join(' ') + ')') : new Error('Request failed')
    ;(safe as Error & { sentryContext?: Record<string, unknown> }).sentryContext = { status, method, path }
    return safe
  } catch {
    return new Error('Request failed')
  }
}

// ---------------------------------------------------------------------------
// Sanitised console logging helpers
// ---------------------------------------------------------------------------

/**
 * Extract a safe, short message from an unknown error value.
 *
 * In development the full error object is returned so developers get maximum
 * context.  In production only a one-line message string is returned — no
 * response body, URL, headers, or stack trace is leaked to the browser console.
 */
function safeErrorDetail(error: unknown): unknown {
  if (import.meta.env.DEV) return error

  if (error instanceof Error) return error.message
  if (typeof error === 'string') return error
  try {
    if (error && typeof error === 'object' && 'message' in error) {
      return String((error as { message: unknown }).message)
    }
  } catch {
    // Defensive: some objects (e.g., Proxy) throw on property access
  }
  return 'An error occurred'
}

/**
 * Log an error with a human-readable context string.
 *
 * - **Development**: logs full error details (existing behaviour).
 * - **Production**: logs only the context string and a sanitised message
 *   (no response body, no URL, no headers).
 */
export function logError(context: string, ...args: unknown[]): void {
  if (import.meta.env.DEV) {
    console.error(context, ...args)
  } else {
    console.error(context, ...args.map(safeErrorDetail))
  }
}

/**
 * Log a warning with a human-readable context string.
 *
 * Follows the same sanitisation rules as {@link logError}.
 */
export function logWarn(context: string, ...args: unknown[]): void {
  if (import.meta.env.DEV) {
    console.warn(context, ...args)
  } else {
    console.warn(context, ...args.map(safeErrorDetail))
  }
}

/** Install `app.config.errorHandler` as a last-resort logger/reporter. */
export function installVueErrorHandler(app: App): void {
  app.config.errorHandler = (err, _instance, info) => {
    logError('[vue:errorHandler]', err, info)
    reportToSentry(err, { info })
  }
}

type DisposeFn = () => void

/**
 * Install window-level listeners for unhandled promise rejections and
 * uncaught errors. Returns a dispose function (primarily for tests /
 * hot-reload cleanup).
 */
export function installWindowErrorListeners(target: Window = window): DisposeFn {
  const onRejection = (event: PromiseRejectionEvent) => {
    const reason = event?.reason
    logError('[window:unhandledrejection]', reason)
    reportToSentry(reason, { source: 'unhandledrejection' })
  }

  const onError = (event: ErrorEvent) => {
    // Prefer event.error (the real Error instance) over event.message.
    const err = event?.error ?? event?.message ?? event
    logError('[window:error]', err)
    reportToSentry(err, { source: 'window.error' })
  }

  target.addEventListener('unhandledrejection', onRejection)
  target.addEventListener('error', onError)

  return () => {
    target.removeEventListener('unhandledrejection', onRejection)
    target.removeEventListener('error', onError)
  }
}
