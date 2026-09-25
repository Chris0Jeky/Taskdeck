import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { authApi } from '../../api/authApi'
import { usersApi } from '../../api/usersApi'
import { useSessionStore } from '../../store/sessionStore'
import * as tokenStorage from '../../utils/tokenStorage'
import type { AuthResponse } from '../../types/auth'

const effects = vi.hoisted(() => ({
  purge: vi.fn(), reset: vi.fn(), success: vi.fn(), error: vi.fn(), info: vi.fn(), remove: vi.fn(),
}))
vi.mock('../../api/authApi', () => ({ authApi: {
  login: vi.fn(), register: vi.fn(), exchangeOAuthCode: vi.fn(), exchangeOidcCode: vi.fn(),
  refreshToken: vi.fn(), changePassword: vi.fn(),
} }))
vi.mock('../../api/usersApi', () => ({ usersApi: { getUser: vi.fn() } }))
vi.mock('../../pwa/legacyApiCache', () => ({ purgeLegacyApiCaches: effects.purge }))
vi.mock('../../composables/useProposalDisplayNames', () => ({ proposalDisplayNames: { reset: effects.reset } }))
vi.mock('../../store/toastStore', () => ({ useToastStore: () => effects }))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

function response(id = 'alice'): AuthResponse {
  const encode = (value: unknown) => btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return {
    token: `${encode({ alg: 'HS256' })}.${encode({ sub: id, exp: 4102444800 })}.sig`,
    user: { id, username: id, email: `${id}@example.test`, defaultRole: 2, isActive: true,
      createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' },
  }
}

function save(data: AuthResponse) {
  tokenStorage.setToken(data.token)
  tokenStorage.setSession({ userId: data.user.id, username: data.user.username, email: data.user.email, defaultRole: 2 })
}

describe('session identity operation ownership (#3324)', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    setActivePinia(createPinia())
    localStorage.clear()
    sessionStorage.clear()
    effects.purge.mockResolvedValue(true)
    vi.mocked(usersApi.getUser).mockResolvedValue(response().user)
  })

  it('does not restore a logged-out identity when an old refresh responds', async () => {
    const store = useSessionStore()
    const request = deferred<AuthResponse>()
    vi.mocked(authApi.refreshToken).mockReturnValue(request.promise)
    const refresh = store.refreshSession().catch(() => undefined)
    store.logout()
    effects.reset.mockClear()
    request.resolve(response())
    await refresh
    expect(store.isAuthenticated).toBe(false)
    expect(tokenStorage.getToken()).toBeNull()
    expect(effects.reset).not.toHaveBeenCalled()
  })

  it('rechecks refresh ownership after its cache purge', async () => {
    const store = useSessionStore()
    const purge = deferred<boolean>()
    vi.mocked(authApi.refreshToken).mockResolvedValue(response())
    effects.purge.mockReturnValueOnce(purge.promise)
    const refresh = store.refreshSession().catch(() => undefined)
    await vi.waitFor(() => expect(effects.purge).toHaveBeenCalledTimes(1))
    store.logout()
    purge.resolve(true)
    await refresh
    expect(store.userId).toBeNull()
    expect(tokenStorage.getToken()).toBeNull()
  })

  it('does not restore saved credentials after logout during cache purge', async () => {
    save(response())
    const store = useSessionStore()
    const purge = deferred<boolean>()
    effects.purge.mockReturnValueOnce(purge.promise)
    const restore = store.restoreSession()
    store.logout()
    purge.resolve(true)
    await restore
    expect(store.isAuthenticated).toBe(false)
    expect(store.userId).toBeNull()
    expect(usersApi.getUser).not.toHaveBeenCalled()
  })

  it('still establishes current refresh and restore operations', async () => {
    const store = useSessionStore()
    vi.mocked(authApi.refreshToken).mockResolvedValue(response())
    await store.refreshSession()
    expect(store.userId).toBe('alice')
    expect(tokenStorage.getToken()).toBe(response().token)
    save(response('bob'))
    vi.mocked(usersApi.getUser).mockResolvedValue(response('bob').user)
    await store.restoreSession()
    expect(store.userId).toBe('bob')
  })

  it('publishes an account switch before the new token exposes torn session metadata', async () => {
    save(response('alice'))
    const snapshot = tokenStorage.captureSessionContinuity()
    const bob = response('bob')
    vi.mocked(authApi.login).mockResolvedValue(bob)
    const originalSetItem = localStorage.setItem.bind(localStorage)
    const writes: string[] = []
    const setItem = vi.spyOn(localStorage, 'setItem').mockImplementation((key, value) => {
      originalSetItem(key, value)
      writes.push(key)
      if (key === 'taskdeck_token' && value === bob.token) {
        expect(tokenStorage.getSession()?.userId).toBe('alice')
        expect(tokenStorage.isSameSessionContinuity(snapshot)).toBe(false)
      }
    })

    try {
      await useSessionStore().login({ usernameOrEmail: 'bob', password: 'test' })
    } finally {
      setItem.mockRestore()
    }

    expect(writes.indexOf('taskdeck_session_break')).toBeGreaterThanOrEqual(0)
    expect(writes.indexOf('taskdeck_session_break')).toBeLessThan(writes.indexOf('taskdeck_token'))
    expect(tokenStorage.getSession()?.userId).toBe('bob')
  })

  const flows = ['login', 'register', 'exchangeOAuthCode', 'exchangeOidcCode'] as const
  function start(store: ReturnType<typeof useSessionStore>, flow: typeof flows[number]) {
    if (flow === 'login') return store.login({ usernameOrEmail: 'alice', password: 'test' })
    if (flow === 'register') return store.register({ username: 'alice', email: 'alice@example.test', password: 'test' })
    return store[flow]('one-time-code')
  }

  it.each(flows)('does not send %s credentials after logout during pre-send purge', async (flow) => {
    const store = useSessionStore()
    const purge = deferred<boolean>()
    effects.purge.mockReturnValueOnce(purge.promise)
    const result = start(store, flow).catch((error: unknown) => error)
    store.logout()
    purge.resolve(true)
    expect(await result).toBeInstanceOf(Error)
    expect(authApi[flow]).not.toHaveBeenCalled()
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
    expect(effects.error).not.toHaveBeenCalled()
    expect(effects.success).not.toHaveBeenCalled()
  })

  it.each(flows)('does not establish %s after logout during post-response purge', async (flow) => {
    const store = useSessionStore()
    const purge = deferred<boolean>()
    effects.purge.mockResolvedValueOnce(true).mockReturnValueOnce(purge.promise)
    vi.mocked(authApi[flow]).mockResolvedValue(response())
    const result = start(store, flow).catch((error: unknown) => error)
    await vi.waitFor(() => expect(effects.purge).toHaveBeenCalledTimes(2))
    store.logout()
    effects.reset.mockClear()
    purge.resolve(true)
    const outcome = await result
    expect(outcome).toBeInstanceOf(Error)
    expect((outcome as Error).message).toContain('server request completed')
    expect(store.userId).toBeNull()
    expect(tokenStorage.getToken()).toBeNull()
    expect(effects.reset).not.toHaveBeenCalled()
    expect(effects.success).not.toHaveBeenCalled()
    expect(effects.error).not.toHaveBeenCalled()
  })

  it('keeps the newer login when responses arrive in reverse order', async () => {
    const store = useSessionStore()
    const old = deferred<AuthResponse>()
    vi.mocked(authApi.login).mockReturnValueOnce(old.promise).mockResolvedValueOnce(response('bob'))
    const previous = start(store, 'login').catch((error: unknown) => error)
    await vi.waitFor(() => expect(authApi.login).toHaveBeenCalledTimes(1))
    await start(store, 'login')
    effects.reset.mockClear()
    effects.success.mockClear()
    old.resolve(response())
    expect(await previous).toBeInstanceOf(Error)
    expect(store.userId).toBe('bob')
    expect(tokenStorage.getSession()?.userId).toBe('bob')
    expect(tokenStorage.getToken()).toBe(response('bob').token)
    expect(effects.reset).not.toHaveBeenCalled()
    expect(effects.success).not.toHaveBeenCalled()
  })

  it('does not revive the previous intent when the newer login fails', async () => {
    const store = useSessionStore()
    const old = deferred<AuthResponse>()
    vi.mocked(authApi.login).mockReturnValueOnce(old.promise).mockRejectedValueOnce(new Error('New credentials rejected'))
    const previous = start(store, 'login').catch((error: unknown) => error)
    await vi.waitFor(() => expect(authApi.login).toHaveBeenCalledTimes(1))
    await expect(start(store, 'login')).rejects.toThrow('New credentials rejected')
    effects.error.mockClear()
    old.resolve(response())
    await previous
    expect(store.userId).toBeNull()
    expect(store.error).toBe('New credentials rejected')
    expect(effects.error).not.toHaveBeenCalled()
    expect(effects.success).not.toHaveBeenCalled()
  })

  it('does not clear newer loading or publish an old rejection', async () => {
    const store = useSessionStore()
    const old = deferred<AuthResponse>()
    const current = deferred<AuthResponse>()
    vi.mocked(authApi.login).mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise)
    const previous = start(store, 'login').catch((error: unknown) => error)
    await vi.waitFor(() => expect(authApi.login).toHaveBeenCalledTimes(1))
    const next = start(store, 'login')
    await vi.waitFor(() => expect(authApi.login).toHaveBeenCalledTimes(2))
    old.reject(new Error('Old request rejected'))
    await previous
    expect(store.loading).toBe(true)
    expect(store.error).toBeNull()
    expect(effects.error).not.toHaveBeenCalled()
    current.resolve(response('bob'))
    await next
    expect(store.loading).toBe(false)
  })

  it('does not let an old restore purge failure clear a newer login', async () => {
    save(response())
    const store = useSessionStore()
    const purge = deferred<boolean>()
    effects.purge.mockReturnValueOnce(purge.promise)
    const previous = store.restoreSession()
    vi.mocked(authApi.login).mockResolvedValue(response('bob'))
    await start(store, 'login')
    purge.resolve(false)
    await previous
    expect(store.userId).toBe('bob')
    expect(tokenStorage.getToken()).toBe(response('bob').token)
  })

  it('guards restore profile hydration even after a same-token login', async () => {
    save(response())
    const store = useSessionStore()
    const profile = deferred<AuthResponse['user']>()
    vi.mocked(usersApi.getUser).mockReturnValue(profile.promise)
    await store.restoreSession()
    vi.mocked(authApi.login).mockResolvedValue(response())
    await start(store, 'login')
    profile.resolve({ ...response().user, defaultRole: 0 })
    await profile.promise
    expect(store.defaultRole).toBe(2)
    expect(tokenStorage.getSession()?.defaultRole).toBe(2)
  })

  it('keeps demo entry when a previous refresh settles and permits a later real login', async () => {
    const store = useSessionStore()
    const request = deferred<AuthResponse>()
    vi.mocked(authApi.refreshToken).mockReturnValue(request.promise)
    const refresh = store.refreshSession().catch((error: unknown) => error)
    store.loginAsDemo()
    const demoUser = store.userId
    request.resolve(response())
    await refresh
    expect(store.isDemo).toBe(true)
    expect(store.userId).toBe(demoUser)
    expect(tokenStorage.getToken()).toBeNull()
    vi.mocked(authApi.login).mockResolvedValue(response())
    await start(store, 'login')
    expect(store.isDemo).toBe(false)
    expect(store.userId).toBe('alice')
  })

  it('retires password-change UI settlement when identity changes', async () => {
    const store = useSessionStore()
    const request = deferred<void>()
    vi.mocked(authApi.changePassword).mockReturnValue(request.promise)
    const passwordChange = store.changePassword({ currentPassword: 'old', newPassword: 'new' })
    store.clearSession()
    request.resolve()
    await passwordChange
    expect(effects.success).not.toHaveBeenCalled()
    expect(store.loading).toBe(false)
  })
})
