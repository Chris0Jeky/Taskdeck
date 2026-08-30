import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { apiKeysApi } from '../../api/apiKeysApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('apiKeysApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('listKeys', () => {
    it('fetches keys from GET /apikeys and returns the keys array', async () => {
      const mockKeys = [
        {
          id: 'key-1',
          keyPrefix: 'tdsk_abc',
          name: 'CI Key',
          scopes: ['read', 'manage'],
          createdAt: '2025-01-01T00:00:00Z',
          expiresAt: null,
          revokedAt: null,
          lastUsedAt: '2025-01-02T00:00:00Z',
          isActive: true,
        },
      ]
      vi.mocked(http.get).mockResolvedValue({ data: { keys: mockKeys } })

      const result = await apiKeysApi.listKeys()

      expect(http.get).toHaveBeenCalledWith('/apikeys')
      expect(result).toEqual(mockKeys)
    })

    it('returns empty array when no keys exist', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: { keys: [] } })

      const result = await apiKeysApi.listKeys()

      expect(result).toEqual([])
    })
  })

  describe('createKey', () => {
    it('posts to /apikeys with name and null expiresInDays by default', async () => {
      const mockResponse = {
        id: 'key-1',
        key: 'tdsk_plaintext123',
        keyPrefix: 'tdsk_pla',
        name: 'My Key',
        scopes: ['read', 'manage'],
        createdAt: '2025-01-01T00:00:00Z',
        expiresAt: null,
      }
      vi.mocked(http.post).mockResolvedValue({ data: mockResponse })

      const result = await apiKeysApi.createKey('My Key', ['read', 'manage'])

      expect(http.post).toHaveBeenCalledWith('/apikeys', {
        name: 'My Key',
        scopes: ['read', 'manage'],
        expiresInDays: null,
      })
      expect(result).toEqual(mockResponse)
    })

    it('passes expiresInDays when provided', async () => {
      vi.mocked(http.post).mockResolvedValue({
        data: {
          id: 'key-2',
          key: 'tdsk_abc',
          keyPrefix: 'tdsk_abc',
          name: 'Expiring Key',
          scopes: ['propose'],
          createdAt: '2025-01-01T00:00:00Z',
          expiresAt: '2025-04-01T00:00:00Z',
        },
      })

      await apiKeysApi.createKey('Expiring Key', ['propose'], 90)

      expect(http.post).toHaveBeenCalledWith('/apikeys', {
        name: 'Expiring Key',
        scopes: ['propose'],
        expiresInDays: 90,
      })
    })

    it('rejects an empty scope selection without issuing a request', async () => {
      await expect(apiKeysApi.createKey('Unsafe Key', [])).rejects.toThrow(
        'Select at least one known API key scope.',
      )

      expect(http.post).not.toHaveBeenCalled()
    })

    it('rejects an unknown scope without issuing a request', async () => {
      await expect(
        apiKeysApi.createKey('Unsafe Key', ['full' as never]),
      ).rejects.toThrow('Select at least one known API key scope.')

      expect(http.post).not.toHaveBeenCalled()
    })
  })

  describe('revokeKey', () => {
    it('sends DELETE to /apikeys/:id', async () => {
      vi.mocked(http.delete).mockResolvedValue({ data: undefined })

      await apiKeysApi.revokeKey('key-1')

      expect(http.delete).toHaveBeenCalledWith('/apikeys/key-1')
    })

    it('encodes special characters in key id', async () => {
      vi.mocked(http.delete).mockResolvedValue({ data: undefined })

      await apiKeysApi.revokeKey('key/special+chars')

      expect(http.delete).toHaveBeenCalledWith('/apikeys/key%2Fspecial%2Bchars')
    })
  })
})
