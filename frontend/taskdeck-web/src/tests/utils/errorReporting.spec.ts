import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h } from 'vue'
import {
  installVueErrorHandler,
  installWindowErrorListeners,
  reportToSentry,
} from '../../utils/errorReporting'

describe('errorReporting utilities', () => {
  let consoleErrorSpy: ReturnType<typeof vi.spyOn>

  beforeEach(() => {
    consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    delete (globalThis as { Sentry?: unknown }).Sentry
  })

  afterEach(() => {
    consoleErrorSpy.mockRestore()
    delete (globalThis as { Sentry?: unknown }).Sentry
  })

  describe('reportToSentry', () => {
    it('returns false when window.Sentry is not present', () => {
      expect(reportToSentry(new Error('nope'))).toBe(false)
    })

    it('calls Sentry.captureException and returns true when present', () => {
      const captureException = vi.fn()
      ;(globalThis as { Sentry?: unknown }).Sentry = { captureException }
      const err = new Error('boom')

      expect(reportToSentry(err)).toBe(true)
      expect(captureException).toHaveBeenCalledWith(err, undefined)
    })

    it('swallows exceptions thrown by Sentry.captureException', () => {
      ;(globalThis as { Sentry?: unknown }).Sentry = {
        captureException: () => {
          throw new Error('sentry exploded')
        },
      }
      expect(() => reportToSentry(new Error('x'))).not.toThrow()
      expect(reportToSentry(new Error('x'))).toBe(false)
    })

    it('returns false when Sentry exists but captureException is not a function', () => {
      ;(globalThis as { Sentry?: unknown }).Sentry = { captureException: 'not-a-fn' }
      expect(reportToSentry(new Error('x'))).toBe(false)
    })
  })

  describe('installVueErrorHandler', () => {
    it('installs a handler on app.config.errorHandler that logs to console', () => {
      const app = createApp(defineComponent({ render: () => h('div') }))
      installVueErrorHandler(app)

      expect(typeof app.config.errorHandler).toBe('function')

      const err = new Error('vue-boom')
      app.config.errorHandler!(err, null, 'render')

      expect(consoleErrorSpy).toHaveBeenCalled()
      const logged = consoleErrorSpy.mock.calls.flat().map(String).join(' ')
      expect(logged).toContain('vue:errorHandler')
    })

    it('forwards the error to Sentry when Sentry is present', () => {
      const captureException = vi.fn()
      ;(globalThis as { Sentry?: unknown }).Sentry = { captureException }

      const app = createApp(defineComponent({ render: () => h('div') }))
      installVueErrorHandler(app)

      const err = new Error('forward-me')
      app.config.errorHandler!(err, null, 'render')

      expect(captureException).toHaveBeenCalledWith(err, { info: 'render' })
    })
  })

  describe('installWindowErrorListeners', () => {
    it('logs and reports unhandledrejection events', () => {
      const captureException = vi.fn()
      ;(globalThis as { Sentry?: unknown }).Sentry = { captureException }

      const dispose = installWindowErrorListeners(window)

      const reason = new Error('rejected')
      // happy-dom supports PromiseRejectionEvent dispatch via a generic Event.
      const event = new Event('unhandledrejection') as PromiseRejectionEvent
      Object.defineProperty(event, 'reason', { value: reason })
      window.dispatchEvent(event)

      expect(consoleErrorSpy).toHaveBeenCalled()
      const logged = consoleErrorSpy.mock.calls.flat().map(String).join(' ')
      expect(logged).toContain('unhandledrejection')
      expect(captureException).toHaveBeenCalledWith(reason, { source: 'unhandledrejection' })

      dispose()
    })

    it('logs and reports window error events', () => {
      const captureException = vi.fn()
      ;(globalThis as { Sentry?: unknown }).Sentry = { captureException }

      const dispose = installWindowErrorListeners(window)

      const err = new Error('uncaught')
      const event = new Event('error') as ErrorEvent
      Object.defineProperty(event, 'error', { value: err })
      Object.defineProperty(event, 'message', { value: 'uncaught' })
      window.dispatchEvent(event)

      expect(consoleErrorSpy).toHaveBeenCalled()
      const logged = consoleErrorSpy.mock.calls.flat().map(String).join(' ')
      expect(logged).toContain('window:error')
      expect(captureException).toHaveBeenCalledWith(err, { source: 'window.error' })

      dispose()
    })

    it('dispose() removes the listeners so later events are not handled', () => {
      const captureException = vi.fn()
      ;(globalThis as { Sentry?: unknown }).Sentry = { captureException }

      const dispose = installWindowErrorListeners(window)
      dispose()

      const event = new Event('unhandledrejection') as PromiseRejectionEvent
      Object.defineProperty(event, 'reason', { value: new Error('late') })
      window.dispatchEvent(event)

      expect(captureException).not.toHaveBeenCalled()
    })

    it('does not throw when there is no Sentry global', () => {
      const dispose = installWindowErrorListeners(window)
      const event = new Event('unhandledrejection') as PromiseRejectionEvent
      Object.defineProperty(event, 'reason', { value: new Error('x') })
      expect(() => window.dispatchEvent(event)).not.toThrow()
      dispose()
    })
  })
})
