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

      await expect(http.get('/test')).rejects.toThrow()

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

      await expect(http.get('/test')).rejects.toThrow('Network Error')
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

      await expect(http.get('/test')).rejects.toThrow()

      expect(clearSpy).not.toHaveBeenCalled()
      expect(window.location.href).toBe('')
    })
  })

  // ── Response interceptor: timeout errors ──────────────────────────────

  describe('response interceptor — timeout errors', () => {
    it('propagates timeout error to caller', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      mock.onGet('/test').timeout()

      await expect(http.get('/test')).rejects.toThrow()
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
})
