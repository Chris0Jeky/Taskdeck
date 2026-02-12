import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSessionStore } from '../../store/sessionStore'
import { authApi } from '../../api/authApi'
import type { AuthResponse } from '../../types/auth'

vi.mock('../../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    changePassword: vi.fn(),
  },
}))

// Helper to build a fake JWT with an exp claim
function fakeJwt(exp?: number): string {
  const header = btoa(JSON.stringify({ alg: 'HS256' }))
  const payload = btoa(JSON.stringify(exp != null ? { exp } : {}))
  return `${header}.${payload}.sig`
}

describe('sessionStore', () => {
  let store: ReturnType<typeof useSessionStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    store = useSessionStore()
    vi.clearAllMocks()
    localStorage.clear()
  })

  describe('login', () => {
    it('should set session on successful login', async () => {
      const response: AuthResponse = {
        token: fakeJwt(Math.floor(Date.now() / 1000) + 3600),
        userId: 'user-1',
        username: 'testuser',
        email: 'test@example.com',
      }
      vi.mocked(authApi.login).mockResolvedValue(response)

      await store.login({ username: 'testuser', password: 'pass' })

      expect(store.token).toBe(response.token)
      expect(store.userId).toBe('user-1')
      expect(store.username).toBe('testuser')
      expect(store.email).toBe('test@example.com')
      expect(store.isAuthenticated).toBe(true)
      expect(store.loading).toBe(false)
      expect(store.error).toBeNull()
    })

    it('should set error on login failure', async () => {
      const err = { response: { data: { message: 'Invalid credentials' } } }
      vi.mocked(authApi.login).mockRejectedValue(err)

      await expect(store.login({ username: 'bad', password: 'bad' })).rejects.toEqual(err)

      expect(store.error).toBe('Invalid credentials')
      expect(store.isAuthenticated).toBe(false)
      expect(store.loading).toBe(false)
    })
  })

  describe('register', () => {
    it('should set session on successful registration', async () => {
      const response: AuthResponse = {
        token: fakeJwt(Math.floor(Date.now() / 1000) + 3600),
        userId: 'user-2',
        username: 'newuser',
        email: 'new@example.com',
      }
      vi.mocked(authApi.register).mockResolvedValue(response)

      await store.register({ username: 'newuser', email: 'new@example.com', password: 'pass' })

      expect(store.token).toBe(response.token)
      expect(store.userId).toBe('user-2')
      expect(store.username).toBe('newuser')
      expect(store.email).toBe('new@example.com')
      expect(store.isAuthenticated).toBe(true)
    })
  })

  describe('logout', () => {
    it('should clear session and isAuthenticated becomes false', async () => {
      const response: AuthResponse = {
        token: fakeJwt(Math.floor(Date.now() / 1000) + 3600),
        userId: 'user-1',
        username: 'testuser',
        email: 'test@example.com',
      }
      vi.mocked(authApi.login).mockResolvedValue(response)
      await store.login({ username: 'testuser', password: 'pass' })

      expect(store.isAuthenticated).toBe(true)

      store.logout()

      expect(store.token).toBeNull()
      expect(store.userId).toBeNull()
      expect(store.username).toBeNull()
      expect(store.email).toBeNull()
      expect(store.isAuthenticated).toBe(false)
      expect(localStorage.getItem('taskdeck_token')).toBeNull()
      expect(localStorage.getItem('taskdeck_session')).toBeNull()
    })
  })

  describe('restoreSession', () => {
    it('should restore session from localStorage', () => {
      const futureExp = Math.floor(Date.now() / 1000) + 3600
      const token = fakeJwt(futureExp)
      localStorage.setItem('taskdeck_token', token)
      localStorage.setItem('taskdeck_session', JSON.stringify({
        userId: 'user-1',
        username: 'restored',
        email: 'restored@example.com',
      }))

      store.restoreSession()

      expect(store.token).toBe(token)
      expect(store.userId).toBe('user-1')
      expect(store.username).toBe('restored')
      expect(store.email).toBe('restored@example.com')
      expect(store.isAuthenticated).toBe(true)
    })

    it('should clear session if token is expired', () => {
      const pastExp = Math.floor(Date.now() / 1000) - 3600
      const token = fakeJwt(pastExp)
      localStorage.setItem('taskdeck_token', token)
      localStorage.setItem('taskdeck_session', JSON.stringify({
        userId: 'user-1',
        username: 'expired',
        email: 'expired@example.com',
      }))

      store.restoreSession()

      expect(store.token).toBeNull()
      expect(store.isAuthenticated).toBe(false)
    })
  })

  describe('clearSession', () => {
    it('should remove session from localStorage', async () => {
      const response: AuthResponse = {
        token: fakeJwt(Math.floor(Date.now() / 1000) + 3600),
        userId: 'user-1',
        username: 'testuser',
        email: 'test@example.com',
      }
      vi.mocked(authApi.login).mockResolvedValue(response)
      await store.login({ username: 'testuser', password: 'pass' })

      store.clearSession()

      expect(store.token).toBeNull()
      expect(store.isAuthenticated).toBe(false)
      expect(localStorage.getItem('taskdeck_token')).toBeNull()
      expect(localStorage.getItem('taskdeck_session')).toBeNull()
    })
  })
})
