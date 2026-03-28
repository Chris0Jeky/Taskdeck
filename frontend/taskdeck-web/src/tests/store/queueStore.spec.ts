import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { queueApi } from '../../api/queueApi'
import { useQueueStore } from '../../store/queueStore'

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

const sessionMocks = vi.hoisted(() => ({
  requireUserId: vi.fn(),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: false,
  }
})

vi.mock('../../api/queueApi', () => ({
  queueApi: {
    getUserRequests: vi.fn(),
    getRequestsByStatus: vi.fn(),
    createRequest: vi.fn(),
    cancelRequest: vi.fn(),
    processNext: vi.fn(),
    getStats: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => ({
    userId: 'user-1',
    requireUserId: sessionMocks.requireUserId,
  }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (error: unknown, fallback: string) => {
    if (error instanceof Error && error.message.trim().length > 0) {
      return { message: error.message, code: null }
    }

    return { message: fallback, code: null }
  },
}))

describe('queueStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('starts with empty default state', () => {
    const store = useQueueStore()

    expect(store.requests).toEqual([])
    expect(store.stats).toBeNull()
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('fetchUserRequests populates queue items from the API', async () => {
    const store = useQueueStore()
    const payload = [
      {
        id: 'req-1',
        userId: 'user-1',
        boardId: 'board-1',
        requestType: 'Instruction',
        status: 'Pending',
        errorMessage: null,
        createdAt: '2026-03-28T12:00:00Z',
        processedAt: null,
        retryCount: 0,
      },
    ]
    vi.mocked(queueApi.getUserRequests).mockResolvedValue(payload)

    await store.fetchUserRequests()

    expect(sessionMocks.requireUserId).toHaveBeenCalledWith('queue operations')
    expect(queueApi.getUserRequests).toHaveBeenCalledTimes(1)
    expect(store.requests).toEqual(payload)
  })

  it('fetchByStatus forwards the requested status and replaces state', async () => {
    const store = useQueueStore()
    const payload = [
      {
        id: 'req-2',
        userId: 'user-1',
        boardId: null,
        requestType: 'Instruction',
        status: 'Failed',
        errorMessage: 'bad input',
        createdAt: '2026-03-28T12:00:00Z',
        processedAt: '2026-03-28T12:05:00Z',
        retryCount: 1,
      },
    ]
    vi.mocked(queueApi.getRequestsByStatus).mockResolvedValue(payload)

    await store.fetchByStatus('Failed')

    expect(queueApi.getRequestsByStatus).toHaveBeenCalledWith('Failed')
    expect(store.requests).toEqual(payload)
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('submitRequest appends the new request to existing requests', async () => {
    const store = useQueueStore()
    const existingRequest = {
      id: 'req-existing',
      userId: 'user-1',
      boardId: 'board-1',
      requestType: 'Instruction',
      status: 'Completed',
      errorMessage: null,
      createdAt: '2026-03-28T11:00:00Z',
      processedAt: '2026-03-28T11:05:00Z',
      retryCount: 0,
    }
    store.requests = [existingRequest]
    const dto = {
      requestType: 'Instruction',
      payload: 'Create a new task',
      boardId: 'board-1',
    }
    const createdRequest = {
      id: 'req-3',
      userId: 'user-1',
      boardId: 'board-1',
      requestType: 'Instruction',
      status: 'Pending',
      errorMessage: null,
      createdAt: '2026-03-28T12:10:00Z',
      processedAt: null,
      retryCount: 0,
    }
    vi.mocked(queueApi.createRequest).mockResolvedValue(createdRequest)

    await expect(store.submitRequest(dto)).resolves.toEqual(createdRequest)

    expect(sessionMocks.requireUserId).toHaveBeenCalledWith('queue operations')
    expect(queueApi.createRequest).toHaveBeenCalledWith(dto)
    expect(store.requests).toEqual([existingRequest, createdRequest])
    expect(toastMocks.success).toHaveBeenCalledWith('Request submitted')
  })

  it('cancelRequest removes the cancelled request from state', async () => {
    const store = useQueueStore()
    store.requests = [
      {
        id: 'req-4',
        userId: 'user-1',
        boardId: 'board-1',
        requestType: 'Instruction',
        status: 'Pending',
        errorMessage: null,
        createdAt: '2026-03-28T12:00:00Z',
        processedAt: null,
        retryCount: 0,
      },
      {
        id: 'req-5',
        userId: 'user-1',
        boardId: null,
        requestType: 'Instruction',
        status: 'Completed',
        errorMessage: null,
        createdAt: '2026-03-28T11:00:00Z',
        processedAt: '2026-03-28T11:05:00Z',
        retryCount: 0,
      },
    ]
    vi.mocked(queueApi.cancelRequest).mockResolvedValue(undefined)

    await store.cancelRequest('req-4')

    expect(sessionMocks.requireUserId).toHaveBeenCalledWith('queue operations')
    expect(queueApi.cancelRequest).toHaveBeenCalledWith('req-4')
    expect(store.requests).toEqual([
      expect.objectContaining({ id: 'req-5' }),
    ])
    expect(toastMocks.success).toHaveBeenCalledWith('Request cancelled')
  })

  it('records API errors without corrupting existing state', async () => {
    const store = useQueueStore()
    store.requests = [
      {
        id: 'req-existing',
        userId: 'user-1',
        boardId: null,
        requestType: 'Instruction',
        status: 'Completed',
        errorMessage: null,
        createdAt: '2026-03-28T10:00:00Z',
        processedAt: '2026-03-28T10:02:00Z',
        retryCount: 0,
      },
    ]
    vi.mocked(queueApi.getUserRequests).mockRejectedValue(new Error('queue failed'))

    await expect(store.fetchUserRequests()).rejects.toBeInstanceOf(Error)

    expect(store.requests).toEqual([
      expect.objectContaining({ id: 'req-existing' }),
    ])
    expect(store.error).toBe('queue failed')
    expect(toastMocks.error).toHaveBeenCalledWith('queue failed')
  })

  it('sets loading during async operations and clears it after completion', async () => {
    const store = useQueueStore()
    let resolveRequest: ((value: null) => void) | null = null

    vi.mocked(queueApi.processNext).mockImplementation(() => new Promise((resolve) => {
      resolveRequest = resolve as (value: null) => void
    }))

    const processPromise = store.processNext()

    expect(store.loading).toBe(true)

    resolveRequest?.(null)
    await processPromise

    expect(store.loading).toBe(false)
    expect(toastMocks.info).toHaveBeenCalledWith('No pending requests')
  })

  it('processNext shows success toast when a request is processed', async () => {
    const store = useQueueStore()
    const processedRequest = {
      id: 'req-processed',
      userId: 'user-1',
      boardId: 'board-1',
      requestType: 'Instruction',
      status: 'Completed',
      errorMessage: null,
      createdAt: '2026-03-28T12:00:00Z',
      processedAt: '2026-03-28T12:01:00Z',
      retryCount: 0,
    }
    vi.mocked(queueApi.processNext).mockResolvedValue(processedRequest)

    const result = await store.processNext()

    expect(result).toEqual(processedRequest)
    expect(toastMocks.success).toHaveBeenCalledWith('Request processed')
    expect(toastMocks.info).not.toHaveBeenCalled()
    expect(store.loading).toBe(false)
  })

  it('fetchStats populates queue stats state', async () => {
    const store = useQueueStore()
    const statsPayload = {
      pendingCount: 2,
      processingCount: 1,
      completedCount: 5,
      failedCount: 1,
    }
    vi.mocked(queueApi.getStats).mockResolvedValue(statsPayload)

    await store.fetchStats()

    expect(queueApi.getStats).toHaveBeenCalledTimes(1)
    expect(store.stats).toEqual(statsPayload)
  })
})
