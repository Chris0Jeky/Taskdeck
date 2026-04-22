import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { nextTick } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { useSessionStore } from '../../store/sessionStore'
import { useSessionTimeout, WARNING_BEFORE_EXPIRY_MS } from '../../composables/useSessionTimeout'
import type { SessionTimeoutState } from '../../composables/useSessionTimeout'

// Mock authApi to prevent real HTTP calls
vi.mock('../../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    changePassword: vi.fn(),
    getProviders: vi.fn(),
    exchangeOAuthCode: vi.fn(),
    exchangeOidcCode: vi.fn(),
    refreshToken: vi.fn(),
    getLinkedAccounts: vi.fn(),
    linkGitHub: vi.fn(),
    unlinkGitHub: vi.fn(),
    getMfaStatus: vi.fn(),
    setupMfa: vi.fn(),
    confirmMfa: vi.fn(),
    verifyMfa: vi.fn(),
    disableMfa: vi.fn(),
  },
}))

vi.mock('../../api/usersApi', () => ({
  usersApi: {
    getUser: vi.fn(),
  },
}))

function toBase64Url(value: string): string {
  return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function fakeJwt(exp: number): string {
  const header = toBase64Url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const payload = toBase64Url(JSON.stringify({ exp }))
  return `${header}.${payload}.sig`
}

describe('useSessionTimeout', () => {
  let session: ReturnType<typeof useSessionStore>
  let state: SessionTimeoutState
  let currentTime: number

  function nowFn() {
    return currentTime
  }

  function setTokenWithExpiry(secondsFromNow: number) {
    const exp = Math.floor(currentTime / 1000) + secondsFromNow
    session.token = fakeJwt(exp)
    session.expiresAt = new Date(exp * 1000).toISOString()
  }

  beforeEach(() => {
    vi.useFakeTimers()
    setActivePinia(createPinia())
    session = useSessionStore()
    currentTime = Date.now()
    localStorage.clear()
  })

  afterEach(() => {
    state?.teardown()
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('exports WARNING_BEFORE_EXPIRY_MS as 5 minutes', () => {
    expect(WARNING_BEFORE_EXPIRY_MS).toBe(5 * 60 * 1000)
  })

  it('does not show warning when unauthenticated', () => {
    state = useSessionTimeout({ nowFn })
    expect(state.showWarning.value).toBe(false)
    expect(state.secondsRemaining.value).toBeNull()
  })

  it('does not show warning when in demo mode', () => {
    session.isDemo = true
    state = useSessionTimeout({ nowFn })
    expect(state.showWarning.value).toBe(false)
  })

  it('does not show warning when token expires far in the future', () => {
    setTokenWithExpiry(3600) // 1 hour from now
    state = useSessionTimeout({ nowFn })
    expect(state.showWarning.value).toBe(false)
    expect(state.secondsRemaining.value).toBeNull()
  })

  it('shows warning when timer fires at 5 minutes before expiry', () => {
    setTokenWithExpiry(600) // 10 minutes from now
    state = useSessionTimeout({ nowFn })

    expect(state.showWarning.value).toBe(false)

    // Advance to 5 minutes before expiry (5 minutes forward)
    currentTime += 5 * 60 * 1000
    vi.advanceTimersByTime(5 * 60 * 1000)

    expect(state.showWarning.value).toBe(true)
    expect(state.secondsRemaining.value).toBe(300) // 5 minutes
  })

  it('shows warning immediately when token is already within warning window', () => {
    setTokenWithExpiry(180) // 3 minutes from now (< 5 min warning window)
    state = useSessionTimeout({ nowFn })

    expect(state.showWarning.value).toBe(true)
    expect(state.secondsRemaining.value).toBe(180)
  })

  it('counts down seconds remaining', () => {
    setTokenWithExpiry(180) // 3 minutes from now
    state = useSessionTimeout({ nowFn })

    expect(state.secondsRemaining.value).toBe(180)

    // Advance 10 seconds
    currentTime += 10_000
    vi.advanceTimersByTime(1000)

    expect(state.secondsRemaining.value).toBe(170)
  })

  it('resets state when countdown reaches zero', () => {
    setTokenWithExpiry(5) // 5 seconds from now
    state = useSessionTimeout({ nowFn })

    expect(state.showWarning.value).toBe(true)
    expect(state.secondsRemaining.value).toBe(5)

    // Advance past expiry
    currentTime += 6000
    vi.advanceTimersByTime(1000)

    expect(state.showWarning.value).toBe(false)
    expect(state.secondsRemaining.value).toBeNull()
  })

  it('dismiss hides the warning', () => {
    setTokenWithExpiry(120) // 2 minutes
    state = useSessionTimeout({ nowFn })

    expect(state.showWarning.value).toBe(true)

    state.dismiss()

    expect(state.showWarning.value).toBe(false)
    expect(state.secondsRemaining.value).toBeNull()
  })

  it('does not show warning again for the same token after dismiss', async () => {
    setTokenWithExpiry(120) // 2 minutes
    state = useSessionTimeout({ nowFn })

    expect(state.showWarning.value).toBe(true)
    state.dismiss()
    expect(state.showWarning.value).toBe(false)

    // Re-setting the same token should not re-trigger
    const currentToken = session.token
    session.token = null
    await nextTick()
    session.token = currentToken
    await nextTick()

    expect(state.showWarning.value).toBe(false)
  })

  it('shows warning for a new token after dismiss of old one', async () => {
    setTokenWithExpiry(120) // 2 minutes
    state = useSessionTimeout({ nowFn })

    expect(state.showWarning.value).toBe(true)
    state.dismiss()

    // Set a different token that's also in warning window
    const exp2 = Math.floor(currentTime / 1000) + 150
    session.token = fakeJwt(exp2)
    session.expiresAt = new Date(exp2 * 1000).toISOString()

    await nextTick()

    expect(state.showWarning.value).toBe(true)
  })

  it('resets warning when token is cleared (logout)', async () => {
    setTokenWithExpiry(120)
    state = useSessionTimeout({ nowFn })

    expect(state.showWarning.value).toBe(true)

    session.token = null
    await nextTick()

    expect(state.showWarning.value).toBe(false)
    expect(state.secondsRemaining.value).toBeNull()
  })

  it('resets when switching to demo mode', async () => {
    setTokenWithExpiry(120)
    state = useSessionTimeout({ nowFn })

    expect(state.showWarning.value).toBe(true)

    session.isDemo = true
    session.token = null
    await nextTick()

    expect(state.showWarning.value).toBe(false)
  })

  it('extend calls refreshToken and dismisses on success', async () => {
    const newExp = Math.floor(currentTime / 1000) + 3600
    const newToken = fakeJwt(newExp)
    const mockRefresh = vi.fn().mockResolvedValue({
      token: newToken,
      user: {
        id: 'user-1',
        username: 'test',
        email: 'test@example.com',
        defaultRole: 0,
        isActive: true,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
      },
    })

    setTokenWithExpiry(120)
    state = useSessionTimeout({ nowFn, refreshToken: mockRefresh })

    expect(state.showWarning.value).toBe(true)

    await state.extend()

    expect(mockRefresh).toHaveBeenCalledOnce()
    expect(state.showWarning.value).toBe(false)
    expect(session.token).toBe(newToken)
  })

  it('extend shows toast on failure and keeps warning visible', async () => {
    const mockRefresh = vi.fn().mockRejectedValue(new Error('Network error'))

    setTokenWithExpiry(120)
    state = useSessionTimeout({ nowFn, refreshToken: mockRefresh })

    expect(state.showWarning.value).toBe(true)

    await state.extend()

    expect(mockRefresh).toHaveBeenCalledOnce()
    // Warning should remain visible
    expect(state.showWarning.value).toBe(true)
  })

  it('extend is idempotent (no double calls while in progress)', async () => {
    let resolveFn: (value: unknown) => void
    const mockRefresh = vi.fn().mockReturnValue(
      new Promise((resolve) => {
        resolveFn = resolve
      }),
    )

    setTokenWithExpiry(120)
    state = useSessionTimeout({ nowFn, refreshToken: mockRefresh })

    const p1 = state.extend()
    const p2 = state.extend() // should be a no-op

    expect(state.extending.value).toBe(true)
    expect(mockRefresh).toHaveBeenCalledTimes(1)

    const newExp = Math.floor(currentTime / 1000) + 3600
    resolveFn!({
      token: fakeJwt(newExp),
      user: {
        id: 'u1',
        username: 'test',
        email: 'test@example.com',
        defaultRole: 0,
        isActive: true,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
      },
    })

    await p1
    await p2

    expect(state.extending.value).toBe(false)
    expect(mockRefresh).toHaveBeenCalledTimes(1)
  })

  it('does not show warning for expired tokens', () => {
    // Token already expired
    setTokenWithExpiry(-10)
    state = useSessionTimeout({ nowFn })

    expect(state.showWarning.value).toBe(false)
  })

  it('teardown clears all state and timers', () => {
    setTokenWithExpiry(600) // 10 minutes
    state = useSessionTimeout({ nowFn })

    // Not in warning window yet
    expect(state.showWarning.value).toBe(false)

    state.teardown()

    // Advance to where warning would have fired
    currentTime += 5 * 60 * 1000
    vi.advanceTimersByTime(5 * 60 * 1000)

    // Should not show since teardown was called
    expect(state.showWarning.value).toBe(false)
  })

  it('reschedules warning when token changes to a new one', async () => {
    // First token: 10 min from now
    setTokenWithExpiry(600)
    state = useSessionTimeout({ nowFn })
    expect(state.showWarning.value).toBe(false)

    // Change to a token that's 2 min from now
    const exp2 = Math.floor(currentTime / 1000) + 120
    session.token = fakeJwt(exp2)
    session.expiresAt = new Date(exp2 * 1000).toISOString()

    await nextTick()

    expect(state.showWarning.value).toBe(true)
    expect(state.secondsRemaining.value).toBe(120)
  })
})
