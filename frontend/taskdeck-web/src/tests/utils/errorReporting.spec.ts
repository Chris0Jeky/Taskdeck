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

    it('normalizes axios-shaped errors before forwarding (no headers, config, or bodies)', () => {
      const captureException = vi.fn()
      ;(globalThis as { Sentry?: unknown }).Sentry = { captureException }
      const axiosErr = {
        isAxiosError: true,
        message: 'Request failed with status code 500',
        config: {
          method: 'post',
          url: '/api/boards?token=secret',
          headers: { Authorization: 'Bearer super-secret', 'X-Other': 'x' },
          data: { password: 'hunter2' },
        },
        response: { status: 500, statusText: 'Server Error', data: { secret: true } },
        request: {},
      }

      expect(reportToSentry(axiosErr)).toBe(true)
      expect(captureException).toHaveBeenCalledTimes(1)
      const forwarded = captureException.mock.calls[0][0] as Error & { sentryContext?: Record<string, unknown> }
      expect(forwarded).toBeInstanceOf(Error)
      expect(forwarded.message).toBe('Request failed (status 500 POST /api/boards)')
      expect(forwarded.sentryContext).toEqual({ status: 500, method: 'POST', path: '/api/boards' })
      expect(JSON.stringify(forwarded)).not.toContain('super-secret')
      expect(JSON.stringify(forwarded)).not.toContain('hunter2')
      expect(JSON.stringify(forwarded)).not.toContain('token=secret')
      expect('config' in forwarded).toBe(false)
      expect('response' in forwarded).toBe(false)
    })

    it('normalizes config/response-shaped rejections even without the axios flag', () => {
      const captureException = vi.fn()
      ;(globalThis as { Sentry?: unknown }).Sentry = { captureException }
      const reason = {
        message: 'Network Error',
        config: {
          method: 'get',
          url: 'https://api.example.com/api/cards#frag',
          headers: { Authorization: 'Bearer abc' },
        },
        response: { status: 401 },
      }

      expect(reportToSentry(reason)).toBe(true)
      const forwarded = captureException.mock.calls[0][0] as Error
      expect(forwarded).toBeInstanceOf(Error)
      expect(forwarded.message).toBe('Request failed (status 401 GET https://api.example.com/api/cards)')
      expect('headers' in forwarded).toBe(false)
    })

    it('never throws on malformed axios-shaped values', () => {
      const captureException = vi.fn()
      ;(globalThis as { Sentry?: unknown }).Sentry = { captureException }
      const weird = { isAxiosError: true, config: null, response: null }

      expect(() => reportToSentry(weird)).not.toThrow()
      expect(reportToSentry(weird)).toBe(true)
      const forwarded = captureException.mock.calls[0][0] as Error
      expect(forwarded).toBeInstanceOf(Error)
      expect(forwarded.message).toBe('Request failed')
    })

    it('passes non-object rejection reasons through untouched', () => {
      const captureException = vi.fn()
      ;(globalThis as { Sentry?: unknown }).Sentry = { captureException }

      expect(reportToSentry('boom')).toBe(true)
      expect(captureException).toHaveBeenCalledWith('boom', undefined)
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
