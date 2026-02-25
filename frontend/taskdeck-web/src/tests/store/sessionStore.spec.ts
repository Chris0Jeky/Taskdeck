import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { authApi } from '../../api/authApi'
import { useSessionStore } from '../../store/sessionStore'
import type { AuthResponse } from '../../types/auth'

vi.mock('../../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    changePassword: vi.fn(),
  },
}))

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

describe('sessionStore', () => {
  let store: ReturnType<typeof useSessionStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    store = useSessionStore()
    vi.clearAllMocks()
    localStorage.clear()
  })

  it('sets session on successful login', async () => {
    const response = makeAuthResponse()
    vi.mocked(authApi.login).mockResolvedValue(response)

    await store.login({ usernameOrEmail: 'testuser', password: 'pass' })

    expect(store.token).toBe(response.token)
    expect(store.userId).toBe('user-1')
    expect(store.username).toBe('testuser')
    expect(store.email).toBe('test@example.com')
    expect(store.defaultRole).toBe(2)
    expect(store.isAuthenticated).toBe(true)
    expect(store.error).toBeNull()
  })

  it('sets error on login failure', async () => {
    const err = { response: { data: { message: 'Invalid credentials' } } }
    vi.mocked(authApi.login).mockRejectedValue(err)

    await expect(store.login({ usernameOrEmail: 'bad', password: 'bad' })).rejects.toEqual(err)

    expect(store.error).toBe('Invalid credentials')
    expect(store.isAuthenticated).toBe(false)
  })

  it('sets session on successful registration', async () => {
    const response = makeAuthResponse()
    vi.mocked(authApi.register).mockResolvedValue(response)

    await store.register({ username: 'newuser', email: 'new@example.com', password: 'pass' })

    expect(store.userId).toBe('user-1')
    expect(store.isAuthenticated).toBe(true)
  })

  it('allows successful login after duplicate registration failure', async () => {
    const duplicateRegistrationError = {
      response: {
        data: {
          message: 'An account with that username or email already exists. Sign in with your existing credentials.',
        },
      },
    }
    const loginResponse = makeAuthResponse()

    vi.mocked(authApi.register).mockRejectedValueOnce(duplicateRegistrationError)
    vi.mocked(authApi.login).mockResolvedValueOnce(loginResponse)

    await expect(
      store.register({ username: 'existing-user', email: 'existing@example.com', password: 'new-pass' }),
    ).rejects.toEqual(duplicateRegistrationError)

    expect(store.isAuthenticated).toBe(false)
    expect(store.error).toContain('already exists')

    await store.login({ usernameOrEmail: 'existing-user', password: 'password123' })

    expect(store.error).toBeNull()
    expect(store.isAuthenticated).toBe(true)
    expect(store.userId).toBe('user-1')
  })

  it('restoreSession restores valid base64url jwt', () => {
    const token = makeAuthResponse().token
    localStorage.setItem('taskdeck_token', token)
    localStorage.setItem('taskdeck_session', JSON.stringify({
      userId: 'user-1',
      username: 'restored',
      email: 'restored@example.com',
      defaultRole: 1,
    }))

    store.restoreSession()

    expect(store.token).toBe(token)
    expect(store.userId).toBe('user-1')
    expect(store.username).toBe('restored')
    expect(store.email).toBe('restored@example.com')
    expect(store.defaultRole).toBe(1)
    expect(store.isAuthenticated).toBe(true)
  })

  it('restoreSession clears expired jwt', () => {
    const token = fakeJwt(Math.floor(Date.now() / 1000) - 60)
    localStorage.setItem('taskdeck_token', token)
    localStorage.setItem('taskdeck_session', JSON.stringify({
      userId: 'user-1',
      username: 'expired',
      email: 'expired@example.com',
      defaultRole: 2,
    }))

    store.restoreSession()

    expect(store.token).toBeNull()
    expect(store.userId).toBeNull()
    expect(store.defaultRole).toBeNull()
    expect(store.isAuthenticated).toBe(false)
  })

  it('logout clears state and storage', async () => {
    const response = makeAuthResponse()
    vi.mocked(authApi.login).mockResolvedValue(response)
    await store.login({ usernameOrEmail: 'testuser', password: 'pass' })

    store.logout()

    expect(store.token).toBeNull()
    expect(store.userId).toBeNull()
    expect(store.defaultRole).toBeNull()
    expect(store.isAuthenticated).toBe(false)
    expect(localStorage.getItem('taskdeck_token')).toBeNull()
    expect(localStorage.getItem('taskdeck_session')).toBeNull()
  })

  it('requireUserId throws when session is missing', () => {
    expect(() => store.requireUserId('queue operations')).toThrow('You must be logged in to use queue operations.')
  })
})
