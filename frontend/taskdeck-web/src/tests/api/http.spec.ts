/**
 * Tests for the HTTP interceptor in api/http.ts (issue #725).
 *
 * The Axios instance has two interceptors:
 * 1. Request interceptor: injects Authorization header (if token valid),
 *    clears storage if token expired, adds X-Request-Id.
 * 2. Response interceptor: on 401, clears storage and redirects to /login
 *    (unless on auth path or in demo mode). Propagates other errors.
 *
 * We use axios-mock-adapter to intercept HTTP calls at the adapter level
 * so that interceptors run normally over each request/response cycle.
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import MockAdapter from 'axios-mock-adapter'
import axios from 'axios'

// ─── JWT helpers ────────────────────────────────────────────────────────────

function toBase64Url(value: string): string {
  return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function fakeJwt(expOffsetSeconds = 3600): string {
  const header = toBase64Url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const exp = Math.floor(Date.now() / 1000) + expOffsetSeconds
  const payload = toBase64Url(JSON.stringify({ exp }))
  return `${header}.${payload}.fakesig`
}

function expiredJwt(): string {
  return fakeJwt(-60)
}

// ─── Mocks ──────────────────────────────────────────────────────────────────

// Mock demoMode before importing http (module-level side effects).
// Use a hoisted getter so per-test overrides work without Object.defineProperty
// (which triggers ESLint no-import-assign).
const demoModeFlag = vi.hoisted(() => ({ value: false }))
vi.mock('../../utils/demoMode', () => ({
  get isDemoMode() {
    return demoModeFlag.value
  },
}))

// We'll control this per-test
const navigationMock = vi.hoisted(() => ({
  isAuthRoutePath: vi.fn().mockReturnValue(false),
}))
vi.mock('../../utils/navigation', () => navigationMock)

// ─── Module imports (after mocks) ───────────────────────────────────────────

import http from '../../api/http'
import * as tokenStorage from '../../utils/tokenStorage'

// ─── Test suite ─────────────────────────────────────────────────────────────

describe('http interceptors (#725)', () => {
  let mock: MockAdapter
  const originalLocation = window.location

  beforeEach(() => {
    mock = new MockAdapter(http)
    localStorage.clear()
    vi.restoreAllMocks()
    navigationMock.isAuthRoutePath.mockReturnValue(false)
    demoModeFlag.value = false
    // Reset window.location to a test-friendly object
    Object.defineProperty(window, 'location', {
      value: { ...originalLocation, href: 'http://localhost/', pathname: '/workspace/home', search: '' },
      writable: true,
      configurable: true,
    })
  })

  afterEach(() => {
    mock.restore()
    demoModeFlag.value = false
    Object.defineProperty(window, 'location', {
      value: originalLocation,
      writable: true,
      configurable: true,
    })
  })

  // ── Request interceptor: Authorization header ───────────────────────────

  describe('request interceptor — Authorization header', () => {
    it('attaches Bearer token when token is valid', async () => {
      const token = fakeJwt()
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(token)
      mock.onGet('/test').reply(200, { ok: true })

      const response = await http.get('/test')

      expect(response.config.headers.Authorization).toBe(`Bearer ${token}`)
    })

    it('does not attach Authorization header when no token exists', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      mock.onGet('/test').reply(200, { ok: true })

      const response = await http.get('/test')

      expect(response.config.headers.Authorization).toBeUndefined()
    })

    it('does not attach Authorization header when token is expired', async () => {
      const expired = expiredJwt()
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(expired)
      mock.onGet('/test').reply(200, { ok: true })

      const response = await http.get('/test')

      expect(response.config.headers.Authorization).toBeUndefined()
    })

    it('clears storage when token is expired', async () => {
      const expired = expiredJwt()
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(expired)
      const clearSpy = vi.spyOn(tokenStorage, 'clearAll')
      mock.onGet('/test').reply(200, { ok: true })

      await http.get('/test')

      expect(clearSpy).toHaveBeenCalledOnce()
    })
  })

  // ── Request interceptor: X-Request-Id header ──────────────────────────

  describe('request interceptor — X-Request-Id', () => {
    it('injects X-Request-Id header on every request', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      mock.onGet('/test').reply(200, { ok: true })

      const response = await http.get('/test')

      const requestId = response.config.headers['X-Request-Id']
      expect(requestId).toBeDefined()
      expect(typeof requestId).toBe('string')
      expect((requestId as string).length).toBeGreaterThan(0)
    })

    it('does not overwrite a pre-existing X-Request-Id', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      mock.onGet('/test').reply(200, { ok: true })

      const response = await http.get('/test', {
        headers: { 'X-Request-Id': 'custom-id-123' },
      })

      expect(response.config.headers['X-Request-Id']).toBe('custom-id-123')
    })

    it('generates unique X-Request-Id values per request', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      mock.onGet('/test').reply(200, { ok: true })

      const r1 = await http.get('/test')
      const r2 = await http.get('/test')

      const id1 = r1.config.headers['X-Request-Id']
      const id2 = r2.config.headers['X-Request-Id']
      expect(id1).not.toBe(id2)
    })
  })

  // ── Response interceptor: 401 handling ────────────────────────────────

  describe('response interceptor — 401 handling', () => {
    it('clears storage on 401 response', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      const clearSpy = vi.spyOn(tokenStorage, 'clearAll')
      mock.onGet('/test').reply(401, { message: 'Unauthorized' })

      await expect(http.get('/test')).rejects.toThrow()

      expect(clearSpy).toHaveBeenCalled()
    })

    it('redirects to /login with return URL on 401', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      vi.spyOn(tokenStorage, 'clearAll')
      Object.defineProperty(window, 'location', {
        value: { pathname: '/workspace/boards/abc', search: '?filter=active', href: '' },
        writable: true,
        configurable: true,
      })
      mock.onGet('/test').reply(401, { message: 'Unauthorized' })

      await expect(http.get('/test')).rejects.toThrow()

      expect(window.location.href).toBe(
        '/login?redirect=' + encodeURIComponent('/workspace/boards/abc?filter=active'),
      )
    })

    it('does not redirect when already on an auth path (avoids redirect loop)', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      vi.spyOn(tokenStorage, 'clearAll')
      navigationMock.isAuthRoutePath.mockReturnValue(true)
      Object.defineProperty(window, 'location', {
        value: { pathname: '/login', search: '', href: '' },
        writable: true,
        configurable: true,
      })
      mock.onGet('/test').reply(401, { message: 'Unauthorized' })

      await expect(http.get('/test')).rejects.toThrow()

      // href should not have been changed to a redirect
      expect(window.location.href).toBe('')
    })

    it('does not redirect or clear storage on 401 when in demo mode', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      const clearSpy = vi.spyOn(tokenStorage, 'clearAll')
      demoModeFlag.value = true
      Object.defineProperty(window, 'location', {
        value: { pathname: '/workspace/home', search: '', href: '' },
        writable: true,
        configurable: true,
      })
      mock.onGet('/test').reply(401, { message: 'Unauthorized' })

      await expect(http.get('/test')).rejects.toThrow()

      // In demo mode, neither redirect nor clearAll should fire
      expect(window.location.href).toBe('')
      expect(clearSpy).not.toHaveBeenCalled()
    })
  })

  // ── Response interceptor: non-401 errors ──────────────────────────────

  describe('response interceptor — non-401 errors', () => {
    it('propagates 500 error without redirecting', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      const clearSpy = vi.spyOn(tokenStorage, 'clearAll')
      Object.defineProperty(window, 'location', {
        value: { pathname: '/workspace/home', search: '', href: '' },
        writable: true,
        configurable: true,
      })
      mock.onGet('/test').reply(500, { message: 'Internal Server Error' })

      // `skipRetry: true` opts out of the retry interceptor (#854). Without
      // it a 500 would trigger 3 retries with 1s/2s/4s backoffs and blow past
      // the default 5s vitest timeout. Retry behaviour is covered by the
      // dedicated `retry interceptor (#854)` suite below.
      await expect(http.get('/test', { skipRetry: true })).rejects.toThrow()

      expect(clearSpy).not.toHaveBeenCalled()
      expect(window.location.href).toBe('')
    })

    it('propagates 403 error without redirecting', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      const clearSpy = vi.spyOn(tokenStorage, 'clearAll')
      mock.onGet('/test').reply(403, { message: 'Forbidden' })

      await expect(http.get('/test')).rejects.toThrow()

      expect(clearSpy).not.toHaveBeenCalled()
    })

    it('propagates 404 error without redirecting', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      const clearSpy = vi.spyOn(tokenStorage, 'clearAll')
      mock.onGet('/test').reply(404, { message: 'Not Found' })

      await expect(http.get('/test')).rejects.toThrow()

      expect(clearSpy).not.toHaveBeenCalled()
    })
  })

  // ── Response interceptor: network errors ──────────────────────────────

  describe('response interceptor — network errors', () => {
    it('propagates network error to caller', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      mock.onGet('/test').networkError()

      // skipRetry avoids the 1s+2s+4s retry path (see 500 test above).
      await expect(http.get('/test', { skipRetry: true })).rejects.toThrow('Network Error')
    })

    it('does not clear storage or redirect on network error', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      const clearSpy = vi.spyOn(tokenStorage, 'clearAll')
      Object.defineProperty(window, 'location', {
        value: { pathname: '/workspace/home', search: '', href: '' },
        writable: true,
        configurable: true,
      })
      mock.onGet('/test').networkError()

      await expect(http.get('/test', { skipRetry: true })).rejects.toThrow()

      expect(clearSpy).not.toHaveBeenCalled()
      expect(window.location.href).toBe('')
    })
  })

  // ── Response interceptor: timeout errors ──────────────────────────────

  describe('response interceptor — timeout errors', () => {
    it('propagates timeout error to caller', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      mock.onGet('/test').timeout()

      await expect(http.get('/test', { skipRetry: true })).rejects.toThrow()
    })
  })

  // ── Successful responses pass through ─────────────────────────────────

  describe('successful responses', () => {
    it('returns response data for 200 responses', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(fakeJwt())
      mock.onGet('/test').reply(200, { boards: [] })

      const response = await http.get('/test')

      expect(response.status).toBe(200)
      expect(response.data).toEqual({ boards: [] })
    })

    it('returns response data for 201 responses', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(fakeJwt())
      mock.onPost('/test').reply(201, { id: 'new-1' })

      const response = await http.post('/test', { name: 'item' })

      expect(response.status).toBe(201)
      expect(response.data).toEqual({ id: 'new-1' })
    })
  })

  // ── Retry interceptor (#854) ───────────────────────────────────────────

  describe('retry interceptor (#854)', () => {
    // Use fake timers so the 1s/2s/4s backoffs don't actually wait.
    beforeEach(() => {
      vi.useFakeTimers()
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
    })
    afterEach(() => {
      vi.useRealTimers()
    })

    // Drive the retry loop: advance fake timers until axios-mock-adapter has
    // seen `expectedRequests` of the given method (default GET), or throw an
    // explicit error if not reached within the budget. Throwing prevents
    // silent hangs (Copilot feedback on PR #890) — a stalled retry loop now
    // fails fast at drainRetries rather than waiting for the outer test
    // timeout.
    async function drainRetries(
      expectedRequests: number,
      options: { maxTicks?: number; tickMs?: number; method?: keyof typeof mock.history } = {},
    ) {
      const maxTicks = options.maxTicks ?? 200
      const tickMs = options.tickMs ?? 100
      const method = options.method ?? 'get'
      const seen = () => mock.history[method].length
      for (let i = 0; i < maxTicks; i++) {
        if (seen() >= expectedRequests) return
        await vi.advanceTimersByTimeAsync(tickMs)
      }
      throw new Error(
        `drainRetries: expected ${expectedRequests} ${method.toUpperCase()} request(s) within ` +
          `${maxTicks * tickMs}ms, observed ${seen()}`,
      )
    }

    it('retries GET 500 three times then fails (4 total requests)', async () => {
      mock.onGet('/flaky').reply(500, { error: 'boom' })

      const pending = http.get('/flaky')
      const expectation = expect(pending).rejects.toMatchObject({
        response: { status: 500 },
      })
      await drainRetries(4, { tickMs: 1000, maxTicks: 30 })
      await expectation

      expect(mock.history.get.length).toBe(4)
    })

    it('succeeds on retry when GET 500 then 200', async () => {
      let call = 0
      mock.onGet('/eventually').reply(() => {
        call++
        return call === 1 ? [500, { error: 'temp' }] : [200, { ok: true }]
      })

      const pending = http.get('/eventually')
      await drainRetries(2, { tickMs: 1000, maxTicks: 10 })
      const response = await pending

      expect(response.status).toBe(200)
      expect(response.data).toEqual({ ok: true })
      expect(mock.history.get.length).toBe(2)
    })

    it('does not retry POST on 500 (single request)', async () => {
      mock.onPost('/write').reply(500, { error: 'boom' })

      await expect(http.post('/write', { k: 'v' })).rejects.toMatchObject({
        response: { status: 500 },
      })
      expect(mock.history.post.length).toBe(1)
    })

    it('does not retry PATCH on 500 (single request)', async () => {
      mock.onPatch('/update').reply(500, { error: 'boom' })

      await expect(http.patch('/update', { k: 'v' })).rejects.toMatchObject({
        response: { status: 500 },
      })
      expect(mock.history.patch.length).toBe(1)
    })

    it('retries PUT on 500', async () => {
      mock.onPut('/put').reply(500, { error: 'boom' })

      const pending = http.put('/put', { k: 'v' })
      const expectation = expect(pending).rejects.toMatchObject({
        response: { status: 500 },
      })
      await drainRetries(4, { tickMs: 1000, maxTicks: 30, method: 'put' })
      await expectation

      expect(mock.history.put.length).toBe(4)
    })

    it('does not retry GET 404 (4xx client error)', async () => {
      mock.onGet('/missing').reply(404, { error: 'nope' })

      await expect(http.get('/missing')).rejects.toMatchObject({
        response: { status: 404 },
      })
      expect(mock.history.get.length).toBe(1)
    })

    it('does not retry GET 401 (preserves session redirect flow)', async () => {
      Object.defineProperty(window, 'location', {
        value: { pathname: '/workspace/home', search: '', href: '' },
        writable: true,
        configurable: true,
      })
      const clearSpy = vi.spyOn(tokenStorage, 'clearAll')
      mock.onGet('/auth').reply(401, { error: 'nope' })

      await expect(http.get('/auth')).rejects.toMatchObject({
        response: { status: 401 },
      })
      expect(mock.history.get.length).toBe(1)
      // Existing 401 handler must still fire.
      expect(clearSpy).toHaveBeenCalled()
    })

    it('retries GET on network error', async () => {
      mock.onGet('/offline').networkError()

      const pending = http.get('/offline')
      const expectation = expect(pending).rejects.toThrow()
      await drainRetries(4, { tickMs: 1000, maxTicks: 30 })
      await expectation

      expect(mock.history.get.length).toBe(4)
    })

    it('honours numeric Retry-After on 429', async () => {
      // First call: 429 with Retry-After: 2 (seconds). Second call: 200.
      let call = 0
      mock.onGet('/throttled').reply(() => {
        call++
        if (call === 1) return [429, { error: 'slow down' }, { 'retry-after': '2' }]
        return [200, { ok: true }]
      })

      const pending = http.get('/throttled')
      // Advance 1.9s — retry should NOT have fired yet.
      await vi.advanceTimersByTimeAsync(1900)
      expect(mock.history.get.length).toBe(1)
      // Advance past the 2s mark.
      await vi.advanceTimersByTimeAsync(200)
      const response = await pending

      expect(response.status).toBe(200)
      expect(mock.history.get.length).toBe(2)
    })

    it('honours HTTP-date Retry-After on 429', async () => {
      const fixedNow = Date.parse('2026-04-16T12:00:00Z')
      vi.setSystemTime(fixedNow)
      const target = new Date(fixedNow + 3000).toUTCString() // 3s in the future

      let call = 0
      mock.onGet('/throttled-date').reply(() => {
        call++
        if (call === 1) return [429, { error: 'slow down' }, { 'retry-after': target }]
        return [200, { ok: true }]
      })

      const pending = http.get('/throttled-date')
      await drainRetries(2, { tickMs: 500, maxTicks: 20 })
      const response = await pending

      expect(response.status).toBe(200)
      expect(mock.history.get.length).toBe(2)
    })

    it('skipRetry opt-out disables the retry loop for a single request', async () => {
      // Verifies the opt-out path used by the baseline tests above — a 500
      // with skipRetry must reject immediately after one request, NOT after
      // the 1s/2s/4s retry schedule.
      mock.onGet('/once').reply(500, { error: 'boom' })
      await expect(http.get('/once', { skipRetry: true })).rejects.toMatchObject({
        response: { status: 500 },
      })
      expect(mock.history.get.length).toBe(1)
    })

    it('does not retry non-transient 5xx (501 Not Implemented)', async () => {
      mock.onGet('/not-implemented').reply(501, { error: 'nope' })
      await expect(http.get('/not-implemented')).rejects.toMatchObject({
        response: { status: 501 },
      })
      expect(mock.history.get.length).toBe(1)
    })

    it('retries GET 408 Request Timeout', async () => {
      mock.onGet('/timeout-status').reply(408, { error: 'timeout' })
      const pending = http.get('/timeout-status')
      const expectation = expect(pending).rejects.toMatchObject({
        response: { status: 408 },
      })
      await drainRetries(4, { tickMs: 1000, maxTicks: 30 })
      await expectation
      expect(mock.history.get.length).toBe(4)
    })

    it('aborts retry loop when request is cancelled mid-wait', async () => {
      mock.onGet('/slow').reply(500, { error: 'boom' })
      const controller = new AbortController()
      const pending = http.get('/slow', { signal: controller.signal })
      // Attach the rejection expectation BEFORE we drive timers forward so
      // the promise is already being awaited when it settles. Previously the
      // raw `pending` sat unobserved and Vitest flagged the rejection as
      // unhandled (PR #890 self-review finding).
      const expectation = expect(pending).rejects.toSatisfy((err: unknown) => {
        // Cancellation should surface via axios.isCancel, NOT the original 500.
        return axios.isCancel(err)
      })

      // Let first attempt fail and enter the retry wait.
      await vi.advanceTimersByTimeAsync(1)
      expect(mock.history.get.length).toBe(1)

      controller.abort()
      // Advance past the backoff; should NOT re-issue.
      await vi.advanceTimersByTimeAsync(10_000)

      await expectation
      expect(mock.history.get.length).toBe(1)
    })
  })
})
