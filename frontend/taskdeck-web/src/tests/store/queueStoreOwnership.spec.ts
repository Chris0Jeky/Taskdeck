import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { queueApi } from '../../api/queueApi'
import { useQueueStore } from '../../store/queueStore'
import { useSessionStore } from '../../store/sessionStore'
import type { QueueRequest, QueueStats } from '../../types/queue'

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: false }
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

vi.mock('../../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    changePassword: vi.fn(),
    refreshToken: vi.fn(),
    exchangeOAuthCode: vi.fn(),
    exchangeOidcCode: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (error: unknown, fallback: string) => ({
    message: error instanceof Error ? error.message : fallback,
  }),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}

function token(suffix: string): string {
  const body = btoa(JSON.stringify({ exp: 1893456000 }))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/g, '')
  return `header.${body}.${suffix}`
}

function request(id: string, status: QueueRequest['status'] = 'Pending'): QueueRequest {
  return {
    id,
    userId: 'user-a',
    boardId: 'board-a',
    requestType: 'Instruction',
    status,
    errorMessage: null,
    createdAt: '2026-09-21T00:00:00Z',
    processedAt: status === 'Pending' ? null : '2026-09-21T00:01:00Z',
    retryCount: 0,
  }
}

function stats(pendingCount: number): QueueStats {
  return {
    pendingCount,
    processingCount: 1,
    completedCount: 2,
    failedCount: 3,
  }
}

describe('queueStore async ownership', () => {
  let session: ReturnType<typeof useSessionStore>
  let store: ReturnType<typeof useQueueStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'user-a'
    session.token = token('old')
    store = useQueueStore()
    vi.clearAllMocks()
  })

  it('keeps the newest request-list query when user and status reads settle in reverse order', async () => {
    const oldUser = deferred<QueueRequest[]>()
    const newStatus = deferred<QueueRequest[]>()
    vi.mocked(queueApi.getUserRequests).mockReturnValue(oldUser.promise)
    vi.mocked(queueApi.getRequestsByStatus).mockReturnValue(newStatus.promise)

    const oldRequest = store.fetchUserRequests()
    const newRequest = store.fetchByStatus('Failed')

    newStatus.resolve([request('new-status', 'Failed')])
    await newRequest
    oldUser.resolve([request('old-user')])
    await oldRequest

    expect(store.requests.map(item => item.id)).toEqual(['new-status'])
    expect(store.error).toBeNull()
  })

  it('suppresses an older request-list failure after a newer query succeeds', async () => {
    const oldUser = deferred<QueueRequest[]>()
    const newStatus = deferred<QueueRequest[]>()
    vi.mocked(queueApi.getUserRequests).mockReturnValue(oldUser.promise)
    vi.mocked(queueApi.getRequestsByStatus).mockReturnValue(newStatus.promise)

    const oldRequest = store.fetchUserRequests()
    const newRequest = store.fetchByStatus('Completed')

    newStatus.resolve([request('new-status', 'Completed')])
    await newRequest
    oldUser.reject(new Error('stale user queue failure'))
    await expect(oldRequest).rejects.toThrow('stale user queue failure')

    expect(store.requests.map(item => item.id)).toEqual(['new-status'])
    expect(store.error).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('does not let a stats refresh erase the current request-list failure', async () => {
    const queueStats = deferred<QueueStats>()
    vi.mocked(queueApi.getRequestsByStatus).mockRejectedValue(new Error('request list failed'))
    vi.mocked(queueApi.getStats).mockReturnValue(queueStats.promise)

    await expect(store.fetchByStatus('Failed')).rejects.toThrow('request list failed')
    expect(store.error).toBe('request list failed')

    const statsRequest = store.fetchStats()
    const errorAfterStatsStarted = store.error
    queueStats.resolve(stats(2))
    await statsRequest

    expect(errorAfterStatsStarted).toBe('request list failed')
    expect(store.error).toBe('request list failed')
  })

  it('does not let an older request read erase a confirmed submission', async () => {
    const oldRead = deferred<QueueRequest[]>()
    const create = deferred<QueueRequest>()
    vi.mocked(queueApi.getUserRequests).mockReturnValue(oldRead.promise)
    vi.mocked(queueApi.createRequest).mockReturnValue(create.promise)

    const readRequest = store.fetchUserRequests()
    const submitRequest = store.submitRequest({ requestType: 'Instruction', payload: 'Do the work' })

    create.resolve(request('created'))
    await submitRequest
    oldRead.resolve([request('existing')])
    await readRequest

    expect(store.requests.map(item => item.id)).toEqual(['created'])
  })

  it('does not let an older request read reinsert a confirmed cancellation', async () => {
    const snapshot = [request('cancelled-later'), request('kept')]
    const oldRead = deferred<QueueRequest[]>()
    const cancel = deferred<void>()
    store.requests = [...snapshot]
    vi.mocked(queueApi.getRequestsByStatus).mockReturnValue(oldRead.promise)
    vi.mocked(queueApi.cancelRequest).mockReturnValue(cancel.promise)

    const readRequest = store.fetchByStatus('Pending')
    const cancelRequest = store.cancelRequest('cancelled-later')

    cancel.resolve()
    await cancelRequest
    oldRead.resolve(snapshot)
    await readRequest

    expect(store.requests.map(item => item.id)).toEqual(['kept'])
  })

  it('does not install stats captured before a confirmed queue mutation', async () => {
    const oldStats = deferred<QueueStats>()
    const create = deferred<QueueRequest>()
    store.stats = stats(99)
    vi.mocked(queueApi.getStats).mockReturnValue(oldStats.promise)
    vi.mocked(queueApi.createRequest).mockReturnValue(create.promise)

    const statsRequest = store.fetchStats()
    const submitRequest = store.submitRequest({ requestType: 'Instruction', payload: 'Queue it' })

    create.resolve(request('created'))
    await submitRequest
    oldStats.resolve(stats(1))
    await statsRequest

    expect(store.stats?.pendingCount).toBe(99)
  })

  it('keeps loading true until independent request and stats operations both settle', async () => {
    const requests = deferred<QueueRequest[]>()
    const queueStats = deferred<QueueStats>()
    vi.mocked(queueApi.getRequestsByStatus).mockReturnValue(requests.promise)
    vi.mocked(queueApi.getStats).mockReturnValue(queueStats.promise)

    const requestsOperation = store.fetchByStatus('Pending')
    const statsOperation = store.fetchStats()
    expect(store.loading).toBe(true)

    requests.resolve([request('pending')])
    await requestsOperation
    expect(store.loading).toBe(true)

    queueStats.resolve(stats(1))
    await statsOperation
    expect(store.loading).toBe(false)
  })

  it('preserves loaded queue data on token rotation and ignores late read and submit successes', async () => {
    const oldRead = deferred<QueueRequest[]>()
    const freshRead = deferred<QueueRequest[]>()
    const oldSubmit = deferred<QueueRequest>()
    store.requests = [request('existing')]
    store.stats = stats(4)
    store.error = 'existing error'
    vi.mocked(queueApi.getRequestsByStatus)
      .mockReturnValueOnce(oldRead.promise)
      .mockReturnValueOnce(freshRead.promise)
    vi.mocked(queueApi.getUserRequests).mockResolvedValue([request('existing')])
    vi.mocked(queueApi.getStats).mockResolvedValue(stats(4))
    vi.mocked(queueApi.createRequest).mockReturnValue(oldSubmit.promise)

    const readOperation = store.fetchByStatus('Pending')
    const submitOperation = store.submitRequest({ requestType: 'Instruction', payload: 'Old session' })

    session.token = token('new')
    const immediate = {
      requestIds: store.requests.map(item => item.id),
      stats: store.stats,
      loading: store.loading,
      error: store.error,
    }

    oldRead.resolve([request('old-read')])
    oldSubmit.resolve(request('old-submit'))
    freshRead.resolve([request('existing')])
    await Promise.all([readOperation, submitOperation])

    expect(immediate).toEqual({ requestIds: ['existing'], stats: stats(4), loading: true, error: null })
    expect(store.requests.map(item => item.id)).toEqual(['existing'])
    expect(store.stats).toEqual(stats(4))
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
    expect(toastMocks.success).not.toHaveBeenCalled()
  })

  it('retries empty initial request and stats reads after same-user token rotation', async () => {
    const oldRequests = deferred<QueueRequest[]>()
    const freshRequests = deferred<QueueRequest[]>()
    const oldStats = deferred<QueueStats>()
    const freshStats = deferred<QueueStats>()
    vi.mocked(queueApi.getRequestsByStatus)
      .mockReturnValueOnce(oldRequests.promise)
      .mockReturnValueOnce(freshRequests.promise)
    vi.mocked(queueApi.getStats)
      .mockReturnValueOnce(oldStats.promise)
      .mockReturnValueOnce(freshStats.promise)

    const requestsOperation = store.fetchByStatus('Failed')
    const statsOperation = store.fetchStats()
    session.token = token('new')

    expect(queueApi.getRequestsByStatus).toHaveBeenCalledTimes(2)
    expect(queueApi.getRequestsByStatus).toHaveBeenNthCalledWith(2, 'Failed')
    expect(queueApi.getStats).toHaveBeenCalledTimes(2)
    expect(store.requests).toEqual([])
    expect(store.stats).toBeNull()
    expect(store.loading).toBe(true)

    oldRequests.resolve([request('old-token', 'Failed')])
    oldStats.resolve(stats(1))
    freshRequests.resolve([request('fresh-token', 'Failed')])
    freshStats.resolve(stats(7))
    await Promise.all([requestsOperation, statsOperation])

    expect(store.requests.map(item => item.id)).toEqual(['fresh-token'])
    expect(store.stats?.pendingCount).toBe(7)
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('retries an active status read with cached rows and joins the replacement', async () => {
    const oldRead = deferred<QueueRequest[]>()
    const freshRead = deferred<QueueRequest[]>()
    store.requests = [request('previous', 'Completed')]
    vi.mocked(queueApi.getRequestsByStatus)
      .mockReturnValueOnce(oldRead.promise)
      .mockReturnValueOnce(freshRead.promise)

    const operation = store.fetchByStatus('Pending')
    session.token = token('new')

    expect(queueApi.getRequestsByStatus).toHaveBeenCalledTimes(2)
    expect(queueApi.getRequestsByStatus).toHaveBeenNthCalledWith(2, 'Pending')

    oldRead.reject(new Error('old-token failure'))
    freshRead.resolve([request('fresh', 'Pending')])

    await expect(operation).resolves.toBeUndefined()
    expect(store.requests.map(item => item.id)).toEqual(['fresh'])
    expect(store.error).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('surfaces a replacement read failure and clears loading after token rotation', async () => {
    const oldRead = deferred<QueueRequest[]>()
    const freshRead = deferred<QueueRequest[]>()
    store.requests = [request('previous', 'Completed')]
    vi.mocked(queueApi.getRequestsByStatus)
      .mockReturnValueOnce(oldRead.promise)
      .mockReturnValueOnce(freshRead.promise)

    const operation = store.fetchByStatus('Pending')
    session.token = token('new')
    oldRead.resolve([request('old-token', 'Pending')])
    freshRead.reject(new Error('replacement failed'))

    await expect(operation).rejects.toThrow('replacement failed')
    expect(store.loading).toBe(false)
    expect(store.error).toBe('replacement failed')
    expect(toastMocks.error).toHaveBeenCalledTimes(1)
  })

  it('suppresses a stale mutation failure after token rotation while preserving cached data and rejection', async () => {
    const cancel = deferred<void>()
    store.requests = [request('old-request')]
    vi.mocked(queueApi.cancelRequest).mockReturnValue(cancel.promise)

    const operation = store.cancelRequest('old-request')
    session.token = token('new')
    cancel.reject(new Error('old-session cancellation failed'))
    await expect(operation).rejects.toThrow('old-session cancellation failed')

    expect(store.requests.map(item => item.id)).toEqual(['old-request'])
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('reconciles a successful stale submission after same-user token rotation', async () => {
    const pendingSubmit = deferred<QueueRequest>()
    const freshRequests = deferred<QueueRequest[]>()
    const freshStats = deferred<QueueStats>()
    const existing = request('existing')
    const created = request('created')
    store.requests = [existing]
    store.stats = stats(1)
    vi.mocked(queueApi.createRequest).mockReturnValue(pendingSubmit.promise)
    vi.mocked(queueApi.getUserRequests).mockReturnValue(freshRequests.promise)
    vi.mocked(queueApi.getStats).mockReturnValue(freshStats.promise)

    const operation = store.submitRequest({ requestType: 'Instruction', payload: 'Queue it' })
    session.token = token('new')
    pendingSubmit.resolve(created)

    await vi.waitFor(() => {
      expect(queueApi.getUserRequests).toHaveBeenCalledTimes(1)
      expect(queueApi.getStats).toHaveBeenCalledTimes(1)
    })

    freshRequests.resolve([existing, created])
    freshStats.resolve(stats(2))
    await expect(operation).resolves.toEqual(created)

    expect(store.requests.map(item => item.id)).toEqual(['existing', 'created'])
    expect(store.stats?.pendingCount).toBe(2)
    expect(toastMocks.success).not.toHaveBeenCalled()
  })

  it('reconciles a successful stale cancellation after same-user token rotation', async () => {
    const pendingCancel = deferred<void>()
    const freshRequests = deferred<QueueRequest[]>()
    const freshStats = deferred<QueueStats>()
    const existing = request('existing')
    store.requests = [existing]
    store.stats = stats(1)
    vi.mocked(queueApi.cancelRequest).mockReturnValue(pendingCancel.promise)
    vi.mocked(queueApi.getUserRequests).mockReturnValue(freshRequests.promise)
    vi.mocked(queueApi.getStats).mockReturnValue(freshStats.promise)

    const operation = store.cancelRequest('existing')
    session.token = token('new')
    pendingCancel.resolve()

    await vi.waitFor(() => {
      expect(queueApi.getUserRequests).toHaveBeenCalledTimes(1)
      expect(queueApi.getStats).toHaveBeenCalledTimes(1)
    })

    freshRequests.resolve([])
    freshStats.resolve(stats(0))
    await expect(operation).resolves.toBeUndefined()

    expect(store.requests).toEqual([])
    expect(store.stats?.pendingCount).toBe(0)
    expect(toastMocks.success).not.toHaveBeenCalled()
  })

  it('resets rather than retrying a read under a cleared identity', async () => {
    const pendingRead = deferred<QueueRequest[]>()
    store.requests = [request('existing')]
    vi.mocked(queueApi.getUserRequests).mockReturnValue(pendingRead.promise)

    const operation = store.fetchUserRequests()
    session.token = null
    session.userId = null

    expect(queueApi.getUserRequests).toHaveBeenCalledTimes(1)
    expect(store.requests).toEqual([])
    expect(store.stats).toBeNull()
    expect(store.loading).toBe(false)

    pendingRead.resolve([request('late')])
    await operation
    expect(store.requests).toEqual([])
  })

  it('does not emit a process result toast after credential replacement', async () => {
    const processing = deferred<QueueRequest | null>()
    vi.mocked(queueApi.processNext).mockReturnValue(processing.promise)

    const operation = store.processNext()
    session.token = token('new')
    processing.resolve(request('processed', 'Completed'))
    await operation

    expect(toastMocks.success).not.toHaveBeenCalled()
    expect(toastMocks.info).not.toHaveBeenCalled()
    expect(store.loading).toBe(false)
  })

  it('exposes a reset boundary that invalidates an in-flight request read', async () => {
    const pending = deferred<QueueRequest[]>()
    store.requests = [request('existing')]
    store.stats = stats(3)
    vi.mocked(queueApi.getUserRequests).mockReturnValue(pending.promise)

    const operation = store.fetchUserRequests()
    const reset = (store as unknown as { $reset?: () => void }).$reset
    const hasReset = typeof reset === 'function'
    reset?.()

    pending.resolve([request('late')])
    await operation

    expect(hasReset).toBe(true)
    expect(store.requests).toEqual([])
    expect(store.stats).toBeNull()
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })
})
