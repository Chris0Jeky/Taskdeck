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
        userId: 'user-1',
        username: 'testuser',
        email: 'test@example.com',
      }
      vi.mocked(http.post).mockResolvedValue({ data: mockResponse })

      const credentials = { username: 'testuser', password: 'pass123' }
      const result = await authApi.login(credentials)

      expect(http.post).toHaveBeenCalledWith('/auth/login', credentials)
      expect(result).toEqual(mockResponse)
    })
  })

  describe('register', () => {
    it('should send POST to /auth/register with request', async () => {
      const mockResponse = {
        token: 'jwt-token',
        userId: 'user-2',
        username: 'newuser',
        email: 'new@example.com',
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
})
