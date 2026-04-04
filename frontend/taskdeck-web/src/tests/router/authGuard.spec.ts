/**
 * Tests for authentication route guard logic (issue #687).
 *
 * The guard in router/index.ts checks whether a session token is present and
 * non-expired before allowing navigation to workspace routes. It also handles
 * the reverse: redirecting an already-authenticated user away from login/register.
 *
 * These tests exercise the guard decision table directly without spinning up a
 * full vue-router instance, following the same pattern as featureFlagGuard.spec.ts.
 */
import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'

// ─── helpers ──────────────────────────────────────────────────────────────────

function toBase64Url(value: string): string {
  return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

/** Build a minimal but structurally-valid JWT with the given exp (Unix seconds). */
function fakeJwt(expOffsetSeconds = 3600): string {
  const header = toBase64Url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const exp = Math.floor(Date.now() / 1000) + expOffsetSeconds
  const payload = toBase64Url(JSON.stringify({ exp }))
  return `${header}.${payload}.fakesig`
}

/** Build a JWT that has already expired. */
function expiredJwt(): string {
  const header = toBase64Url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const exp = Math.floor(Date.now() / 1000) - 60 // 60 s in the past
  const payload = toBase64Url(JSON.stringify({ exp }))
  return `${header}.${payload}.fakesig`
}

const TOKEN_KEY = 'taskdeck_token'

// ─── mirror of the guard logic from router/index.ts ──────────────────────────
//
// Rather than importing the live router (which brings in heavy module-level
// side effects), we inline the guard decision function so tests are fast and
// isolated.  Any future change to the guard must be reflected here.

import { isTokenExpired } from '../../utils/jwt'
import * as tokenStorage from '../../utils/tokenStorage'

function guardDecision(
  to: { path: string; fullPath: string; meta: { public?: boolean } },
  opts: { token: string | null; demoActive?: boolean },
): { path: string; query?: Record<string, string> } | undefined {
  const isPublic = to.meta.public === true
  const demoActive = opts.demoActive ?? false
  const token = opts.token
  const tokenValid = !!token && !isTokenExpired(token)
  const hasValidSession = tokenValid || demoActive

  if (token && !tokenValid) {
    // Expired token is cleared by the guard
    tokenStorage.clearAll()
  }

  if (!isPublic && !hasValidSession && to.path.startsWith('/workspace')) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }

  if (isPublic && hasValidSession && (to.path === '/login' || to.path === '/register')) {
    return { path: '/workspace/home' }
  }

  return undefined // allow navigation
}

// ─── tests ────────────────────────────────────────────────────────────────────

describe('auth route guard (#687)', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // ── unauthenticated user ───────────────────────────────────────────────────

  describe('unauthenticated user (no token)', () => {
    it('redirects to /login when visiting a workspace route', () => {
      const result = guardDecision(
        { path: '/workspace/home', fullPath: '/workspace/home', meta: {} },
        { token: null },
      )
      expect(result).toEqual({ path: '/login', query: { redirect: '/workspace/home' } })
    })

    it('preserves the intended full path in the redirect query', () => {
      const result = guardDecision(
        { path: '/workspace/boards/abc-123', fullPath: '/workspace/boards/abc-123', meta: {} },
        { token: null },
      )
      expect(result).toEqual({ path: '/login', query: { redirect: '/workspace/boards/abc-123' } })
    })

    it('preserves query parameters in the redirect target', () => {
      const result = guardDecision(
        { path: '/workspace/metrics', fullPath: '/workspace/metrics?boardId=xyz', meta: {} },
        { token: null },
      )
      expect(result).toEqual({ path: '/login', query: { redirect: '/workspace/metrics?boardId=xyz' } })
    })

    it('allows access to /login without a token', () => {
      const result = guardDecision(
        { path: '/login', fullPath: '/login', meta: { public: true } },
        { token: null },
      )
      expect(result).toBeUndefined()
    })

    it('allows access to /register without a token', () => {
      const result = guardDecision(
        { path: '/register', fullPath: '/register', meta: { public: true } },
        { token: null },
      )
      expect(result).toBeUndefined()
    })
  })

  // ── expired token ─────────────────────────────────────────────────────────

  describe('expired token', () => {
    it('redirects to /login when token is expired', () => {
      const expired = expiredJwt()
      const result = guardDecision(
        { path: '/workspace/today', fullPath: '/workspace/today', meta: {} },
        { token: expired },
      )
      expect(result).toEqual({ path: '/login', query: { redirect: '/workspace/today' } })
    })

    it('clears token storage when an expired token is encountered', () => {
      const expired = expiredJwt()
      tokenStorage.setToken(expired)
      expect(tokenStorage.getToken()).not.toBeNull()

      guardDecision(
        { path: '/workspace/home', fullPath: '/workspace/home', meta: {} },
        { token: expired },
      )

      // clearAll() was called, so storage should be empty
      expect(localStorage.getItem(TOKEN_KEY)).toBeNull()
    })
  })

  // ── authenticated user ─────────────────────────────────────────────────────

  describe('authenticated user (valid token)', () => {
    it('allows navigation to workspace routes', () => {
      const token = fakeJwt()
      const result = guardDecision(
        { path: '/workspace/home', fullPath: '/workspace/home', meta: {} },
        { token },
      )
      expect(result).toBeUndefined()
    })

    it('allows navigation to /workspace/metrics', () => {
      const token = fakeJwt()
      const result = guardDecision(
        { path: '/workspace/metrics', fullPath: '/workspace/metrics', meta: {} },
        { token },
      )
      expect(result).toBeUndefined()
    })

    it('allows navigation to /workspace/boards/:id', () => {
      const token = fakeJwt()
      const result = guardDecision(
        { path: '/workspace/boards/board-1', fullPath: '/workspace/boards/board-1', meta: {} },
        { token },
      )
      expect(result).toBeUndefined()
    })

    it('redirects away from /login when already authenticated', () => {
      const result = guardDecision(
        { path: '/login', fullPath: '/login', meta: { public: true } },
        { token: fakeJwt() },
      )
      expect(result).toEqual({ path: '/workspace/home' })
    })

    it('redirects away from /register when already authenticated', () => {
      const result = guardDecision(
        { path: '/register', fullPath: '/register', meta: { public: true } },
        { token: fakeJwt() },
      )
      expect(result).toEqual({ path: '/workspace/home' })
    })
  })

  // ── demo mode ─────────────────────────────────────────────────────────────

  describe('demo mode (active demo session)', () => {
    it('allows workspace navigation without a JWT when demo is active', () => {
      const result = guardDecision(
        { path: '/workspace/home', fullPath: '/workspace/home', meta: {} },
        { token: null, demoActive: true },
      )
      expect(result).toBeUndefined()
    })

    it('redirects /login to /workspace/home while demo is active', () => {
      const result = guardDecision(
        { path: '/login', fullPath: '/login', meta: { public: true } },
        { token: null, demoActive: true },
      )
      expect(result).toEqual({ path: '/workspace/home' })
    })
  })

  // ── workspace sub-path boundary ───────────────────────────────────────────

  describe('path-prefix boundary', () => {
    it('does not redirect non-workspace public paths when unauthenticated', () => {
      // A hypothetical future public page at /about should not get caught.
      const result = guardDecision(
        { path: '/about', fullPath: '/about', meta: { public: true } },
        { token: null },
      )
      expect(result).toBeUndefined()
    })

    it('only guards paths that start with /workspace', () => {
      // A non-workspace non-public path (e.g. a future API status page) is
      // outside the guard's scope — it returns undefined (no redirect).
      const result = guardDecision(
        { path: '/status', fullPath: '/status', meta: {} },
        { token: null },
      )
      expect(result).toBeUndefined()
    })
  })
})
