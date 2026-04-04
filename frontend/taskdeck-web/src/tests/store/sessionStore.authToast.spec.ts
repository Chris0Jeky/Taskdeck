/**
 * Regression tests for transient toast state during auth flows — issue #685.
 *
 * Guards the following behaviours observed in live QA (2026-04-02):
 *  - Login error toast/state must not bleed into register or later login flows.
 *  - Duplicate-registration feedback must clear cleanly when navigating back to sign-in.
 *  - Registration-success state must not survive logout and later login attempts.
 *  - Successful login must render login-specific success messaging, not stale registration messaging.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { authApi } from '../../api/authApi'
import { useSessionStore } from '../../store/sessionStore'
import { useToastStore } from '../../store/toastStore'
import type { AuthResponse } from '../../types/auth'

vi.mock('../../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    changePassword: vi.fn(),
    getProviders: vi.fn(),
    exchangeOAuthCode: vi.fn(),
  },
}))

vi.mock('../../api/usersApi', () => ({
  usersApi: {
    getUser: vi.fn(),
  },
}))

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function toBase64Url(value: string): string {
  return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function fakeJwt(exp?: number): string {
  const header = toBase64Url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const payload = toBase64Url(JSON.stringify(exp != null ? { exp } : {}))
  return `${header}.${payload}.sig`
}

function makeAuthResponse(expOffsetSeconds = 3600): AuthResponse {
  return {
    token: fakeJwt(Math.floor(Date.now() / 1000) + expOffsetSeconds),
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

function makeLoginError(message = 'Invalid credentials') {
  return { response: { data: { message } } }
}

function makeRegistrationDuplicateError() {
  return {
    response: {
      data: {
        message:
          'An account with that username or email already exists. Sign in with your existing credentials.',
      },
    },
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('auth-flow toast regression (#685)', () => {
  let session: ReturnType<typeof useSessionStore>
  let toast: ReturnType<typeof useToastStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    session = useSessionStore()
    toast = useToastStore()
    vi.clearAllMocks()
    localStorage.clear()
  })

  // -------------------------------------------------------------------------
  // Login failure — toast appears
  // -------------------------------------------------------------------------

  describe('login failure toast', () => {
    it('shows an error toast when login credentials are invalid', async () => {
      vi.mocked(authApi.login).mockRejectedValue(makeLoginError())

      await expect(
        session.login({ usernameOrEmail: 'bad', password: 'bad' }),
      ).rejects.toBeDefined()

      const errorToasts = toast.toasts.filter((t) => t.type === 'error')
      expect(errorToasts).toHaveLength(1)
      expect(errorToasts[0].message).toBe('Invalid credentials')
    })

    it('error toast message matches the server-supplied reason, not a generic fallback', async () => {
      vi.mocked(authApi.login).mockRejectedValue(
        makeLoginError('Your account has been locked. Contact support.'),
      )

      await expect(
        session.login({ usernameOrEmail: 'locked', password: 'pass' }),
      ).rejects.toBeDefined()

      expect(toast.toasts[0].message).toBe('Your account has been locked. Contact support.')
    })

    it('stores the error in session.error alongside the toast', async () => {
      vi.mocked(authApi.login).mockRejectedValue(makeLoginError('Invalid credentials'))

      await expect(
        session.login({ usernameOrEmail: 'bad', password: 'bad' }),
      ).rejects.toBeDefined()

      expect(session.error).toBe('Invalid credentials')
      expect(toast.toasts.some((t) => t.type === 'error')).toBe(true)
    })
  })

  // -------------------------------------------------------------------------
  // Registration failure — toast appears
  // -------------------------------------------------------------------------

  describe('registration failure toast', () => {
    it('shows an error toast on generic registration failure', async () => {
      vi.mocked(authApi.register).mockRejectedValue(makeLoginError('Registration failed'))

      await expect(
        session.register({ username: 'u', email: 'u@example.com', password: 'pass' }),
      ).rejects.toBeDefined()

      const errorToasts = toast.toasts.filter((t) => t.type === 'error')
      expect(errorToasts).toHaveLength(1)
      expect(errorToasts[0].message).toBe('Registration failed')
    })

    it('shows the duplicate-account guidance as an error toast', async () => {
      vi.mocked(authApi.register).mockRejectedValue(makeRegistrationDuplicateError())

      await expect(
        session.register({ username: 'existing', email: 'existing@example.com', password: 'pass' }),
      ).rejects.toBeDefined()

      const errorToasts = toast.toasts.filter((t) => t.type === 'error')
      expect(errorToasts).toHaveLength(1)
      expect(errorToasts[0].message).toContain('already exists')
    })
  })

  // -------------------------------------------------------------------------
  // Successful auth — toast type and message are correct
  // -------------------------------------------------------------------------

  describe('successful auth toast', () => {
    it('shows a success toast on login with login-specific messaging', async () => {
      vi.mocked(authApi.login).mockResolvedValue(makeAuthResponse())

      await session.login({ usernameOrEmail: 'testuser', password: 'pass' })

      const successToasts = toast.toasts.filter((t) => t.type === 'success')
      expect(successToasts).toHaveLength(1)
      expect(successToasts[0].message).toBe('Logged in successfully')
    })

    it('shows a success toast on registration with registration-specific messaging', async () => {
      vi.mocked(authApi.register).mockResolvedValue(makeAuthResponse())

      await session.register({ username: 'newuser', email: 'new@example.com', password: 'pass' })

      const successToasts = toast.toasts.filter((t) => t.type === 'success')
      expect(successToasts).toHaveLength(1)
      expect(successToasts[0].message).toBe('Registration successful')
    })

    it('login success does NOT emit a registration-success message', async () => {
      vi.mocked(authApi.login).mockResolvedValue(makeAuthResponse())

      await session.login({ usernameOrEmail: 'testuser', password: 'pass' })

      const registrationToasts = toast.toasts.filter((t) =>
        t.message.toLowerCase().includes('registration'),
      )
      expect(registrationToasts).toHaveLength(0)
    })

    it('registration success does NOT emit a login-success message', async () => {
      vi.mocked(authApi.register).mockResolvedValue(makeAuthResponse())

      await session.register({ username: 'newuser', email: 'new@example.com', password: 'pass' })

      const loginToasts = toast.toasts.filter((t) =>
        t.message.toLowerCase().includes('logged in'),
      )
      expect(loginToasts).toHaveLength(0)
    })
  })

  // -------------------------------------------------------------------------
  // Toast does not bleed across login -> register -> login flows
  // -------------------------------------------------------------------------

  describe('toast isolation across login/register transitions', () => {
    it('login error toast does not persist after a subsequent successful login', async () => {
      // First attempt fails
      vi.mocked(authApi.login).mockRejectedValueOnce(makeLoginError('Invalid credentials'))
      await expect(
        session.login({ usernameOrEmail: 'bad', password: 'bad' }),
      ).rejects.toBeDefined()

      // Simulate navigation: toast store is cleared (as a page/route-level component would do)
      toast.clear()

      // Second attempt succeeds
      vi.mocked(authApi.login).mockResolvedValueOnce(makeAuthResponse())
      await session.login({ usernameOrEmail: 'testuser', password: 'pass' })

      // Only the success toast from the second attempt should be present
      expect(toast.toasts.every((t) => t.type !== 'error')).toBe(true)
      expect(toast.toasts.filter((t) => t.type === 'success')).toHaveLength(1)
      expect(toast.toasts[0].message).toBe('Logged in successfully')
    })

    it('registration error does not bleed into a subsequent login success toast', async () => {
      // Registration fails (e.g. duplicate)
      vi.mocked(authApi.register).mockRejectedValueOnce(makeRegistrationDuplicateError())
      await expect(
        session.register({ username: 'existing', email: 'e@example.com', password: 'pass' }),
      ).rejects.toBeDefined()

      expect(toast.toasts.filter((t) => t.type === 'error')).toHaveLength(1)

      // Simulate navigating from /register -> /login (route component teardown clears toasts)
      toast.clear()
      expect(toast.toasts).toHaveLength(0)

      // Login succeeds
      vi.mocked(authApi.login).mockResolvedValueOnce(makeAuthResponse())
      await session.login({ usernameOrEmail: 'existing', password: 'password123' })

      // Must have exactly one toast — a login-specific success, no leftover registration error
      expect(toast.toasts).toHaveLength(1)
      expect(toast.toasts[0].type).toBe('success')
      expect(toast.toasts[0].message).toBe('Logged in successfully')
      expect(toast.toasts[0].message).not.toContain('Registration')
    })

    it('duplicate-registration toast is cleared after toast.clear() simulating navigation', async () => {
      vi.mocked(authApi.register).mockRejectedValueOnce(makeRegistrationDuplicateError())
      await expect(
        session.register({ username: 'existing', email: 'e@example.com', password: 'pass' }),
      ).rejects.toBeDefined()

      const beforeClear = toast.toasts.filter((t) => t.type === 'error')
      expect(beforeClear).toHaveLength(1)
      expect(beforeClear[0].message).toContain('already exists')

      // Simulate route navigation to /login
      toast.clear()

      expect(toast.toasts).toHaveLength(0)
    })

    it('login error state is reset on a fresh login attempt (session.error clears on retry)', async () => {
      vi.mocked(authApi.login).mockRejectedValueOnce(makeLoginError('Invalid credentials'))
      await expect(
        session.login({ usernameOrEmail: 'bad', password: 'bad' }),
      ).rejects.toBeDefined()

      expect(session.error).toBe('Invalid credentials')

      // Second attempt: login resolves — error should be cleared before the API call
      vi.mocked(authApi.login).mockResolvedValueOnce(makeAuthResponse())
      await session.login({ usernameOrEmail: 'testuser', password: 'pass' })

      expect(session.error).toBeNull()
    })
  })

  // -------------------------------------------------------------------------
  // Post-registration success state does not survive logout into subsequent login
  // -------------------------------------------------------------------------

  describe('registration success state does not survive logout', () => {
    it('after register then logout, a subsequent login shows login-specific toast, not registration toast', async () => {
      // Step 1: Register successfully
      vi.mocked(authApi.register).mockResolvedValueOnce(makeAuthResponse())
      await session.register({ username: 'newuser', email: 'new@example.com', password: 'pass' })

      const regToasts = toast.toasts.filter((t) => t.type === 'success')
      expect(regToasts).toHaveLength(1)
      expect(regToasts[0].message).toBe('Registration successful')

      // Step 2: Logout (route navigation would clear toasts; we mimic that)
      session.logout()
      toast.clear()

      // After logout, no success toasts remain from registration
      expect(toast.toasts.filter((t) => t.type === 'success')).toHaveLength(0)

      // Step 3: Log back in
      vi.mocked(authApi.login).mockResolvedValueOnce(makeAuthResponse())
      await session.login({ usernameOrEmail: 'newuser', password: 'pass' })

      const loginToasts = toast.toasts.filter((t) => t.type === 'success')
      expect(loginToasts).toHaveLength(1)
      // Must be login-specific — "Logged in successfully", not "Registration successful"
      expect(loginToasts[0].message).toBe('Logged in successfully')
      expect(loginToasts[0].message).not.toContain('Registration')
    })

    it('logout produces an info toast, not a success toast, keeping feedback distinct', async () => {
      vi.mocked(authApi.login).mockResolvedValueOnce(makeAuthResponse())
      await session.login({ usernameOrEmail: 'testuser', password: 'pass' })

      // Clear login success so we start clean
      toast.clear()

      session.logout()

      expect(toast.toasts).toHaveLength(1)
      expect(toast.toasts[0].type).toBe('info')
      expect(toast.toasts[0].message).toBe('Logged out')
      // Explicitly no success type on logout
      expect(toast.toasts[0].type).not.toBe('success')
    })
  })

  // -------------------------------------------------------------------------
  // OAuth flow — toast lifecycle
  // -------------------------------------------------------------------------

  describe('OAuth (GitHub) auth toast', () => {
    it('shows a GitHub-specific success toast on OAuth code exchange', async () => {
      vi.mocked(authApi.exchangeOAuthCode).mockResolvedValue(makeAuthResponse())

      await session.exchangeOAuthCode('valid-oauth-code')

      const successToasts = toast.toasts.filter((t) => t.type === 'success')
      expect(successToasts).toHaveLength(1)
      expect(successToasts[0].message).toBe('Signed in with GitHub')
      // Must NOT say "Logged in successfully" (which is the credential-login message)
      expect(successToasts[0].message).not.toBe('Logged in successfully')
    })

    it('shows an error toast on OAuth code exchange failure', async () => {
      vi.mocked(authApi.exchangeOAuthCode).mockRejectedValue(
        makeLoginError('GitHub sign-in failed'),
      )

      await expect(session.exchangeOAuthCode('expired-code')).rejects.toBeDefined()

      const errorToasts = toast.toasts.filter((t) => t.type === 'error')
      expect(errorToasts).toHaveLength(1)
      expect(errorToasts[0].message).toBe('GitHub sign-in failed')
    })

    it('OAuth error toast does not bleed into a subsequent credential-login success', async () => {
      vi.mocked(authApi.exchangeOAuthCode).mockRejectedValueOnce(
        makeLoginError('Invalid or expired code'),
      )
      await expect(session.exchangeOAuthCode('bad-code')).rejects.toBeDefined()

      // Simulate navigation (toast cleared on route transition)
      toast.clear()

      vi.mocked(authApi.login).mockResolvedValueOnce(makeAuthResponse())
      await session.login({ usernameOrEmail: 'testuser', password: 'pass' })

      expect(toast.toasts.filter((t) => t.type === 'error')).toHaveLength(0)
      expect(toast.toasts.filter((t) => t.type === 'success')).toHaveLength(1)
      expect(toast.toasts[0].message).toBe('Logged in successfully')
    })
  })

  // -------------------------------------------------------------------------
  // Toast auto-removal does not affect session state
  // -------------------------------------------------------------------------

  describe('toast auto-removal is independent of session state', () => {
    it('auto-removing a success toast does not clear session.error', async () => {
      vi.useFakeTimers()

      vi.mocked(authApi.login).mockRejectedValueOnce(makeLoginError('Bad creds'))
      await expect(
        session.login({ usernameOrEmail: 'bad', password: 'bad' }),
      ).rejects.toBeDefined()

      expect(session.error).toBe('Bad creds')

      // Let the toast auto-expire
      vi.advanceTimersByTime(6000)

      // Session error must still be set — toast expiry must not reset session state
      expect(session.error).toBe('Bad creds')

      vi.useRealTimers()
    })

    it('auto-removing a registration success toast does not clear session token', async () => {
      vi.useFakeTimers()

      vi.mocked(authApi.register).mockResolvedValueOnce(makeAuthResponse())
      await session.register({ username: 'newuser', email: 'new@example.com', password: 'pass' })

      const tokenBefore = session.token

      vi.advanceTimersByTime(5000)

      expect(toast.toasts.filter((t) => t.type === 'success')).toHaveLength(0)
      // Session must still be active
      expect(session.token).toBe(tokenBefore)
      expect(session.isAuthenticated).toBe(true)

      vi.useRealTimers()
    })
  })
})
