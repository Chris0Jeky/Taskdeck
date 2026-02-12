import { describe, it, expect, beforeEach, vi } from 'vitest'
import { queueApi } from '../../api/queueApi'
import http from '../../api/http'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('queueApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('createRequest', () => {
    it('should send POST with request data and userId', async () => {
      const mockRequest = {
        id: 'req-1',
        userId: 'user-1',
        requestType: 'generate',
        payload: '{"prompt":"hello"}',
        status: 'Pending',
        result: null,
        errorMessage: null,
        createdAt: new Date().toISOString(),
        processedAt: null,
      }
      vi.mocked(http.post).mockResolvedValue({ data: mockRequest })

      const dto = { requestType: 'generate', payload: '{"prompt":"hello"}' }
      const result = await queueApi.createRequest(dto, 'user-1')

      expect(http.post).toHaveBeenCalledWith('/llm-queue', { ...dto, userId: 'user-1' })
      expect(result).toEqual(mockRequest)
    })
  })

  describe('getUserRequests', () => {
    it('should send GET with userId', async () => {
      const mockRequests = [{ id: 'req-1' }]
      vi.mocked(http.get).mockResolvedValue({ data: mockRequests })

      const result = await queueApi.getUserRequests('user-1')

      expect(http.get).toHaveBeenCalledWith('/llm-queue/user/user-1')
      expect(result).toEqual(mockRequests)
    })
  })

  describe('getRequestsByStatus', () => {
    it('should send GET with status', async () => {
      const mockRequests = [{ id: 'req-1', status: 'Pending' }]
      vi.mocked(http.get).mockResolvedValue({ data: mockRequests })

      const result = await queueApi.getRequestsByStatus('Pending')

      expect(http.get).toHaveBeenCalledWith('/llm-queue/status/Pending')
      expect(result).toEqual(mockRequests)
    })
  })

  describe('cancelRequest', () => {
    it('should send POST to cancel endpoint', async () => {
      vi.mocked(http.post).mockResolvedValue({})

      await queueApi.cancelRequest('req-1', 'user-1')

      expect(http.post).toHaveBeenCalledWith('/llm-queue/req-1/cancel?userId=user-1')
    })
  })

  describe('processNext', () => {
    it('should send POST to process-next', async () => {
      const mockRequest = { id: 'req-1', status: 'Processing' }
      vi.mocked(http.post).mockResolvedValue({ data: mockRequest })

      const result = await queueApi.processNext()

      expect(http.post).toHaveBeenCalledWith('/llm-queue/process-next')
      expect(result).toEqual(mockRequest)
    })
  })

  describe('getStats', () => {
    it('should send GET to stats endpoint', async () => {
      const mockStats = {
        pending: 5,
        processing: 2,
        completed: 10,
        failed: 1,
        cancelled: 0,
        total: 18,
      }
      vi.mocked(http.get).mockResolvedValue({ data: mockStats })

      const result = await queueApi.getStats()

      expect(http.get).toHaveBeenCalledWith('/llm-queue/stats')
      expect(result).toEqual(mockStats)
    })
  })
})
