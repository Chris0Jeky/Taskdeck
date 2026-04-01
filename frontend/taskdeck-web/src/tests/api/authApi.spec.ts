import { describe, it, expect, beforeEach, vi } from 'vitest'
import { authApi } from '../../api/authApi'
import http from '../../api/http'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('authApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('login', () => {
    it('should send POST to /auth/login with credentials', async () => {
      const mockResponse = {
        token: 'jwt-token',
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
      vi.mocked(http.post).mockResolvedValue({ data: mockResponse })

      const credentials = { usernameOrEmail: 'testuser', password: 'pass123' }
      const result = await authApi.login(credentials)

      expect(http.post).toHaveBeenCalledWith('/auth/login', credentials)
      expect(result).toEqual(mockResponse)
    })
  })

  describe('register', () => {
    it('should send POST to /auth/register with request', async () => {
      const mockResponse = {
        token: 'jwt-token',
        user: {
          id: 'user-2',
          username: 'newuser',
          email: 'new@example.com',
          defaultRole: 2,
          isActive: true,
          createdAt: '2026-01-01T00:00:00Z',
          updatedAt: '2026-01-01T00:00:00Z',
        },
      }
      vi.mocked(http.post).mockResolvedValue({ data: mockResponse })

      const request = { username: 'newuser', email: 'new@example.com', password: 'pass123' }
      const result = await authApi.register(request)

      expect(http.post).toHaveBeenCalledWith('/auth/register', request)
      expect(result).toEqual(mockResponse)
    })
  })

  describe('changePassword', () => {
    it('should send POST to /auth/change-password', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: undefined })

      const request = { userId: 'user-1', currentPassword: 'old', newPassword: 'new' }
      await authApi.changePassword(request)

      expect(http.post).toHaveBeenCalledWith('/auth/change-password', request)
    })
  })

  describe('getProviders', () => {
    it('should send GET to /auth/providers', async () => {
      const mockResponse = { gitHub: true }
      vi.mocked(http.get).mockResolvedValue({ data: mockResponse })

      const result = await authApi.getProviders()

      expect(http.get).toHaveBeenCalledWith('/auth/providers')
      expect(result).toEqual({ gitHub: true })
    })

    it('should return false when GitHub is not configured', async () => {
      const mockResponse = { gitHub: false }
      vi.mocked(http.get).mockResolvedValue({ data: mockResponse })

      const result = await authApi.getProviders()

      expect(result.gitHub).toBe(false)
    })
  })

  describe('exchangeOAuthCode', () => {
    it('should send POST to /auth/github/exchange with code', async () => {
      const mockResponse = {
        token: 'jwt-token-from-github',
        user: {
          id: 'user-gh',
          username: 'octocat',
          email: 'octocat@github.com',
          defaultRole: 2,
          isActive: true,
          createdAt: '2026-01-01T00:00:00Z',
          updatedAt: '2026-01-01T00:00:00Z',
        },
      }
      vi.mocked(http.post).mockResolvedValue({ data: mockResponse })

      const result = await authApi.exchangeOAuthCode('test-auth-code')

      expect(http.post).toHaveBeenCalledWith('/auth/github/exchange', { code: 'test-auth-code' })
      expect(result).toEqual(mockResponse)
    })
  })
})
