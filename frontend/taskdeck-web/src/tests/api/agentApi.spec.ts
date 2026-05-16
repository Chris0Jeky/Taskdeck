import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { agentApi } from '../../api/agentApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('agentApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('listProfiles', () => {
    it('fetches profiles and normalizes scopeType', async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: [
          { id: 'a1', name: 'Agent 1', scopeType: 0 },
          { id: 'a2', name: 'Agent 2', scopeType: 'Board' },
        ],
      })

      const result = await agentApi.listProfiles()

      expect(http.get).toHaveBeenCalledWith('/agents')
      expect(result[0].scopeType).toBe('Workspace')
      expect(result[1].scopeType).toBe('Board')
    })
  })

  describe('getProfile', () => {
    it('fetches a single profile and normalizes', async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: { id: 'a1', name: 'Agent', scopeType: 1 },
      })

      const result = await agentApi.getProfile('a1')

      expect(http.get).toHaveBeenCalledWith('/agents/a1')
      expect(result.scopeType).toBe('Board')
    })

    it('encodes special characters in id', async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: { id: 'a/1', name: 'Agent', scopeType: 0 },
      })

      await agentApi.getProfile('a/1')

      expect(http.get).toHaveBeenCalledWith('/agents/a%2F1')
    })
  })

  describe('listRuns', () => {
    it('fetches runs and normalizes status', async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: [
          { id: 'r1', status: 0 },
          { id: 'r2', status: 'Completed' },
        ],
      })

      const result = await agentApi.listRuns('a1')

      expect(http.get).toHaveBeenCalledWith('/agents/a1/runs?limit=100')
      expect(result[0].status).toBe('Queued')
      expect(result[1].status).toBe('Completed')
    })

    it('respects custom limit', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      await agentApi.listRuns('a1', 25)

      expect(http.get).toHaveBeenCalledWith('/agents/a1/runs?limit=25')
    })

    it('encodes special characters in agentId', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      await agentApi.listRuns('a/1')

      expect(http.get).toHaveBeenCalledWith('/agents/a%2F1/runs?limit=100')
    })
  })

  describe('getRunDetail', () => {
    it('fetches run detail and normalizes status', async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: { id: 'r1', status: 2 },
      })

      const result = await agentApi.getRunDetail('a1', 'r1')

      expect(http.get).toHaveBeenCalledWith('/agents/a1/runs/r1')
      expect(result.status).toBe('Planning')
    })

    it('encodes special characters', async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: { id: 'r/1', status: 0 },
      })

      await agentApi.getRunDetail('a/1', 'r/1')

      expect(http.get).toHaveBeenCalledWith('/agents/a%2F1/runs/r%2F1')
    })
  })
})
