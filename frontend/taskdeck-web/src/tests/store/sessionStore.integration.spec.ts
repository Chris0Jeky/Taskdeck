/**
 * sessionStore integration tests — store + real authApi module, HTTP layer mocked.
 *
 * These tests verify the full store → authApi → http chain for authentication.
 * Mocking http (not authApi) catches any mismatch between API response shapes
 * and what the store expects, including token parsing and claim extraction.
 *
 * Scenarios covered:
 * - Login / register success and failure
 * - OAuth code exchange
 * - Session restore from localStorage
 * - Logout clears state and storage
 * - Token expiry detection
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import http from '../../api/http'
import { useSessionStore } from '../../store/sessionStore'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

// Helper: build a compact base64url-encoded JWT with the given exp claim.
function toBase64Url(value: string): string {
  return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function fakeJwt(expOffsetSeconds = 3600): string {
  const header = toBase64Url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const exp = Math.floor(Date.now() / 1000) + expOffsetSeconds
  const payload = toBase64Url(JSON.stringify({ exp }))
  return `${header}.${payload}.fakesig`
}

function makeAuthResponse(expOffsetSeconds = 3600) {
  return {
    token: fakeJwt(expOffsetSeconds),
    user: {
      id: 'user-1',
      username: 'testuser',
      email: 'test@example.com',
      defaultRole: 2,
      isActive: true,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    },
  }
}

describe('sessionStore — integration (real authApi, mocked HTTP)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
  })

  // ── login ─────────────────────────────────────────────────────────────────

  describe('login', () => {
    it('posts to /auth/login and stores the token and user claims in state', async () => {
      const authResponse = makeAuthResponse()
      vi.mocked(http.post).mockResolvedValue({ data: authResponse })

      const store = useSessionStore()
      await store.login({ usernameOrEmail: 'testuser', password: 'pass123' })

      expect(store.token).toBe(authResponse.token)
      expect(store.userId).toBe('user-1')
      expect(store.username).toBe('testuser')
      expect(store.email).toBe('test@example.com')
      expect(store.defaultRole).toBe(2)
      expect(store.isAuthenticated).toBe(true)
      expect(store.error).toBeNull()
      expect(http.post).toHaveBeenCalledWith('/auth/login', { usernameOrEmail: 'testuser', password: 'pass123' })
    })

    it('persists the token and session data to localStorage after login', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: makeAuthResponse() })

      const store = useSessionStore()
      await store.login({ usernameOrEmail: 'testuser', password: 'pass' })

      expect(localStorage.getItem('taskdeck_token')).toBe(store.token)
      const session = JSON.parse(localStorage.getItem('taskdeck_session') ?? '{}')
      expect(session.userId).toBe('user-1')
      expect(session.username).toBe('testuser')
    })

    it('sets error and clears isAuthenticated when POST /auth/login returns an error response', async () => {
      vi.mocked(http.post).mockRejectedValue({
        response: { data: { message: 'Invalid credentials' } },
      })

      const store = useSessionStore()
      await expect(store.login({ usernameOrEmail: 'wrong', password: 'wrong' })).rejects.toBeDefined()

      expect(store.isAuthenticated).toBe(false)
      expect(store.error).toBe('Invalid credentials')
      expect(store.token).toBeNull()
    })
  })

  // ── register ──────────────────────────────────────────────────────────────

  describe('register', () => {
    it('posts to /auth/register and establishes a session on success', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: makeAuthResponse() })

      const store = useSessionStore()
      await store.register({ username: 'newuser', email: 'new@example.com', password: 'pass123' })

      expect(store.isAuthenticated).toBe(true)
      expect(store.userId).toBe('user-1')
      expect(http.post).toHaveBeenCalledWith('/auth/register', expect.objectContaining({ username: 'newuser' }))
    })

    it('sets error when POST /auth/register returns a conflict', async () => {
      vi.mocked(http.post).mockRejectedValue({
        response: { data: { message: 'An account with that username or email already exists. Sign in with your existing credentials.' } },
      })

      const store = useSessionStore()
      await expect(store.register({ username: 'existing', email: 'existing@example.com', password: 'pass' })).rejects.toBeDefined()

      expect(store.isAuthenticated).toBe(false)
      expect(store.error).toContain('already exists')
    })
  })

  // ── exchangeOAuthCode ─────────────────────────────────────────────────────

  describe('exchangeOAuthCode', () => {
    it('posts to /auth/github/exchange and establishes a session on success', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: makeAuthResponse() })

      const store = useSessionStore()
      await store.exchangeOAuthCode('github-code-xyz')

      expect(store.isAuthenticated).toBe(true)
      expect(store.token).not.toBeNull()
      expect(http.post).toHaveBeenCalledWith('/auth/github/exchange', { code: 'github-code-xyz' })
    })

    it('sets error when the OAuth exchange POST fails', async () => {
      vi.mocked(http.post).mockRejectedValue({
        response: { data: { message: 'Invalid or expired code' } },
      })

      const store = useSessionStore()
      await expect(store.exchangeOAuthCode('bad-code')).rejects.toBeDefined()

      expect(store.isAuthenticated).toBe(false)
      expect(store.error).toBe('Invalid or expired code')
    })
  })

  // ── logout ────────────────────────────────────────────────────────────────

  describe('logout', () => {
    it('clears all session state and removes localStorage entries', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: makeAuthResponse() })

      const store = useSessionStore()
      await store.login({ usernameOrEmail: 'testuser', password: 'pass' })
      expect(store.isAuthenticated).toBe(true)

      store.logout()

      expect(store.token).toBeNull()
      expect(store.userId).toBeNull()
      expect(store.username).toBeNull()
      expect(store.email).toBeNull()
      expect(store.defaultRole).toBeNull()
      expect(store.isAuthenticated).toBe(false)
      expect(localStorage.getItem('taskdeck_token')).toBeNull()
      expect(localStorage.getItem('taskdeck_session')).toBeNull()
    })
  })

  // ── restoreSession ────────────────────────────────────────────────────────

  describe('restoreSession', () => {
    it('restores an active session from localStorage without calling the API', () => {
      const token = fakeJwt(3600)
      localStorage.setItem('taskdeck_token', token)
      localStorage.setItem('taskdeck_session', JSON.stringify({
        userId: 'user-1',
        username: 'restored',
        email: 'restored@example.com',
        defaultRole: 1,
      }))

      // No GET expected when usersApi is not mocked here — restoreSession may fire
      // a background getUser call but the synchronous state must already be set
      vi.mocked(http.get).mockResolvedValue({ data: {
        id: 'user-1', username: 'restored', email: 'restored@example.com',
        defaultRole: 1, isActive: true, createdAt: '', updatedAt: '',
      } })

      const store = useSessionStore()
      store.restoreSession()

      expect(store.token).toBe(token)
      expect(store.userId).toBe('user-1')
      expect(store.isAuthenticated).toBe(true)
    })

    it('clears state and storage when the persisted token is expired', () => {
      const expiredToken = fakeJwt(-60) // expired 60s ago
      localStorage.setItem('taskdeck_token', expiredToken)
      localStorage.setItem('taskdeck_session', JSON.stringify({
        userId: 'user-1',
        username: 'expired-user',
        email: 'expired@example.com',
        defaultRole: 2,
      }))

      const store = useSessionStore()
      store.restoreSession()

      expect(store.token).toBeNull()
      expect(store.userId).toBeNull()
      expect(store.isAuthenticated).toBe(false)
      expect(localStorage.getItem('taskdeck_token')).toBeNull()
    })

    it('does not restore session when localStorage contains no token', () => {
      const store = useSessionStore()
      store.restoreSession()

      expect(store.isAuthenticated).toBe(false)
      expect(store.token).toBeNull()
    })
  })

  // ── requireUserId ─────────────────────────────────────────────────────────

  describe('requireUserId', () => {
    it('throws a descriptive error when the user is not logged in', () => {
      const store = useSessionStore()

      expect(() => store.requireUserId('queue operations')).toThrow(
        'You must be logged in to use queue operations.',
      )
    })

    it('returns the userId when the session is active', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: makeAuthResponse() })
      vi.mocked(http.get).mockResolvedValue({ data: {
        id: 'user-1', username: 'testuser', email: 'test@example.com',
        defaultRole: 2, isActive: true, createdAt: '', updatedAt: '',
      } })

      const store = useSessionStore()
      await store.login({ usernameOrEmail: 'testuser', password: 'pass' })

      const userId = store.requireUserId('test action')
      expect(userId).toBe('user-1')
    })
  })
})
