import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { queueApi } from '../../api/queueApi'

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

  it('createRequest sends payload and normalizes status', async () => {
    const mockRequest = {
      id: 'req-1',
      userId: 'user-1',
      boardId: null,
      requestType: 'generate',
      status: 0,
      errorMessage: null,
      createdAt: new Date().toISOString(),
      processedAt: null,
      retryCount: 0,
    }
    vi.mocked(http.post).mockResolvedValue({ data: mockRequest })

    const dto = { requestType: 'generate', payload: '{"prompt":"hello"}' }
    const result = await queueApi.createRequest(dto)

    expect(http.post).toHaveBeenCalledWith('/llm-queue', dto)
    expect(result.status).toBe('Pending')
  })

  it('createRequest forwards optional boardId in payload', async () => {
    const mockRequest = {
      id: 'req-2',
      userId: 'user-2',
      boardId: 'board-2',
      requestType: 'instruction',
      status: 0,
      errorMessage: null,
      createdAt: new Date().toISOString(),
      processedAt: null,
      retryCount: 0,
    }
    vi.mocked(http.post).mockResolvedValue({ data: mockRequest })

    const dto = { requestType: 'instruction', payload: 'rename board', boardId: 'board-2' }
    await queueApi.createRequest(dto)

    expect(http.post).toHaveBeenCalledWith('/llm-queue', dto)
  })

  it('getUserRequests normalizes statuses', async () => {
    vi.mocked(http.get).mockResolvedValue({
      data: [
        {
          id: 'req-1',
          userId: 'user/1',
          boardId: null,
          requestType: 'generate',
          status: 2,
          errorMessage: null,
          createdAt: new Date().toISOString(),
          processedAt: new Date().toISOString(),
          retryCount: 1,
        },
      ],
    })

    const result = await queueApi.getUserRequests()

    expect(http.get).toHaveBeenCalledWith('/llm-queue/user')
    expect(result[0]?.status).toBe('Completed')
  })

  it('getRequestsByStatus encodes status in path segment', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await queueApi.getRequestsByStatus('Pending Review')

    expect(http.get).toHaveBeenCalledWith('/llm-queue/status/Pending%20Review')
  })

  it('cancelRequest encodes requestId', async () => {
    vi.mocked(http.post).mockResolvedValue({})

    await queueApi.cancelRequest('req/1')

    expect(http.post).toHaveBeenCalledWith('/llm-queue/req%2F1/cancel')
  })

  it('processNext returns null when API responds with NotFound', async () => {
    vi.mocked(http.post).mockRejectedValue({
      response: {
        status: 404,
        data: { errorCode: 'NotFound' },
      },
    })

    const result = await queueApi.processNext()

    expect(result).toBeNull()
  })

  it('getStats returns queue stats payload', async () => {
    const mockStats = {
      pendingCount: 5,
      processingCount: 2,
      completedCount: 10,
      failedCount: 1,
    }
    vi.mocked(http.get).mockResolvedValue({ data: mockStats })

    const result = await queueApi.getStats()

    expect(http.get).toHaveBeenCalledWith('/llm-queue/stats')
    expect(result).toEqual(mockStats)
  })
})
