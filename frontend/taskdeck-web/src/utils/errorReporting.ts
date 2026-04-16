/**
 * Global error reporting plumbing for the Vue app.
 *
 * Responsibilities:
 *   - Install `app.config.errorHandler` as the top-level Vue backstop.
 *   - Install `window.addEventListener('unhandledrejection', ...)` to catch
 *     async rejections that Vue's errorCaptured hook cannot see.
 *   - Install `window.addEventListener('error', ...)` for uncaught runtime
 *     errors thrown outside Vue's render pipeline.
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
    sentry.captureException(err, hint)
    return true
  } catch {
    // Reporting must never itself propagate.
    return false
  }
}

/** Install `app.config.errorHandler` as a last-resort logger/reporter. */
export function installVueErrorHandler(app: App): void {
  app.config.errorHandler = (err, _instance, info) => {
    console.error('[vue:errorHandler]', err, info)
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
    console.error('[window:unhandledrejection]', reason)
    reportToSentry(reason, { source: 'unhandledrejection' })
  }

  const onError = (event: ErrorEvent) => {
    // Prefer event.error (the real Error instance) over event.message.
    const err = event?.error ?? event?.message ?? event
    console.error('[window:error]', err)
    reportToSentry(err, { source: 'window.error' })
  }

  target.addEventListener('unhandledrejection', onRejection)
  target.addEventListener('error', onError)

  return () => {
    target.removeEventListener('unhandledrejection', onRejection)
    target.removeEventListener('error', onError)
  }
}
