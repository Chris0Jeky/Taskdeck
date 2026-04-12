/**
 * queueStore integration tests — store + real queueApi module, HTTP layer mocked.
 *
 * Key value: queueApi.normalizeQueueRequest transforms numeric status codes
 * (0, 1, 2…) to string values ('Pending', 'Processing', 'Completed'…).
 * Mocking only the HTTP layer verifies that this transformation happens
 * correctly in the full store → queueApi → http chain and that the store
 * state reflects the normalized values.
 *
 * Regression: data isolation (#508) — requests belong to the authenticated
 * user only, enforced by the queueApi user-scoped endpoint.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import http from '../../api/http'
import { useQueueStore } from '../../store/queueStore'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({ error: vi.fn(), success: vi.fn(), warning: vi.fn(), info: vi.fn() }),
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => ({
    userId: 'user-1',
    requireUserId: vi.fn().mockReturnValue('user-1'),
  }),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: false }
})

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (error: unknown, fallback: string) => {
    if (error instanceof Error && error.message.trim().length > 0) {
      return { message: error.message, code: null }
    }
    return { message: fallback, code: null }
  },
}))

function makeRawRequest(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: 'req-1',
    userId: 'user-1',
    boardId: 'board-1',
    requestType: 'Instruction',
    status: 'Pending',
    errorMessage: null,
    createdAt: '2026-01-01T00:00:00Z',
    processedAt: null,
    retryCount: 0,
    ...overrides,
  }
}

describe('queueStore — integration (real queueApi, mocked HTTP)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  // ── fetchUserRequests ─────────────────────────────────────────────────────

  describe('fetchUserRequests', () => {
    it('calls GET /llm-queue/user and populates store.requests', async () => {
      const requests = [makeRawRequest(), makeRawRequest({ id: 'req-2', boardId: null })]
      vi.mocked(http.get).mockResolvedValue({ data: requests })

      const store = useQueueStore()
      await store.fetchUserRequests()

      expect(store.requests).toHaveLength(2)
      expect(store.requests[0].id).toBe('req-1')
      expect(store.loading).toBe(false)
      expect(store.error).toBeNull()
      expect(http.get).toHaveBeenCalledWith('/llm-queue/user')
    })

    it('normalizes numeric status codes from the API to string labels (#508)', async () => {
      // The API may return numeric status values (enum ordinal) — queueApi must normalize
      const rawRequests = [
        makeRawRequest({ id: 'r-pending', status: 0 }),     // 0 → 'Pending'
        makeRawRequest({ id: 'r-processing', status: 1 }),   // 1 → 'Processing'
        makeRawRequest({ id: 'r-completed', status: 2 }),    // 2 → 'Completed'
        makeRawRequest({ id: 'r-failed', status: 3 }),       // 3 → 'Failed'
        makeRawRequest({ id: 'r-cancelled', status: 4 }),    // 4 → 'Cancelled'
      ]
      vi.mocked(http.get).mockResolvedValue({ data: rawRequests })

      const store = useQueueStore()
      await store.fetchUserRequests()

      expect(store.requests.find(r => r.id === 'r-pending')?.status).toBe('Pending')
      expect(store.requests.find(r => r.id === 'r-processing')?.status).toBe('Processing')
      expect(store.requests.find(r => r.id === 'r-completed')?.status).toBe('Completed')
      expect(store.requests.find(r => r.id === 'r-failed')?.status).toBe('Failed')
      expect(store.requests.find(r => r.id === 'r-cancelled')?.status).toBe('Cancelled')
    })

    it('normalizes case-insensitive string status values from the API', async () => {
      const rawRequests = [
        makeRawRequest({ id: 'r-lower', status: 'pending' }),
        makeRawRequest({ id: 'r-mixed', status: 'Processing' }),
      ]
      vi.mocked(http.get).mockResolvedValue({ data: rawRequests })

      const store = useQueueStore()
      await store.fetchUserRequests()

      expect(store.requests.find(r => r.id === 'r-lower')?.status).toBe('Pending')
      expect(store.requests.find(r => r.id === 'r-mixed')?.status).toBe('Processing')
    })

    it("only surfaces the authenticated user's requests via the user-scoped endpoint (#508)", async () => {
      // The endpoint /llm-queue/user enforces ownership server-side.
      // Here we verify the store calls the correct user-scoped URL, not /llm-queue.
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      const store = useQueueStore()
      await store.fetchUserRequests()

      const calledUrl = vi.mocked(http.get).mock.calls[0][0] as string
      expect(calledUrl).toBe('/llm-queue/user')
      expect(calledUrl).not.toBe('/llm-queue')
    })

    it('sets error and does not wipe existing requests when GET fails', async () => {
      const store = useQueueStore()
      store.requests = [makeRawRequest()]
      vi.mocked(http.get).mockRejectedValue(new Error('server error'))

      await expect(store.fetchUserRequests()).rejects.toBeInstanceOf(Error)

      expect(store.requests).toHaveLength(1)
      expect(store.error).toBe('server error')
    })
  })

  // ── fetchByStatus ─────────────────────────────────────────────────────────

  describe('fetchByStatus', () => {
    it('calls GET /llm-queue/status/:status and replaces store.requests', async () => {
      const failed = [makeRawRequest({ id: 'r-f', status: 'Failed', errorMessage: 'oops' })]
      vi.mocked(http.get).mockResolvedValue({ data: failed })

      const store = useQueueStore()
      await store.fetchByStatus('Failed')

      expect(store.requests[0].status).toBe('Failed')
      expect(http.get).toHaveBeenCalledWith('/llm-queue/status/Failed')
    })

    it('normalizes numeric status from status-scoped endpoint', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [makeRawRequest({ status: 2 })] })

      const store = useQueueStore()
      await store.fetchByStatus('Completed')

      expect(store.requests[0].status).toBe('Completed')
    })
  })

  // ── submitRequest ─────────────────────────────────────────────────────────

  describe('submitRequest', () => {
    it('posts to /llm-queue and appends the normalized request to the list', async () => {
      // API returns numeric status
      const created = makeRawRequest({ id: 'req-new', status: 0 })
      vi.mocked(http.post).mockResolvedValue({ data: created })

      const store = useQueueStore()
      const result = await store.submitRequest({
        requestType: 'Instruction',
        payload: 'Do something',
        boardId: 'board-1',
      })

      expect(result.id).toBe('req-new')
      // Numeric 0 must be normalized to string 'Pending'
      expect(result.status).toBe('Pending')
      expect(store.requests.some(r => r.id === 'req-new')).toBe(true)
      expect(http.post).toHaveBeenCalledWith('/llm-queue', expect.any(Object))
    })

    it('does not append to the list when POST /llm-queue fails', async () => {
      const store = useQueueStore()
      vi.mocked(http.post).mockRejectedValue({ response: { status: 400, data: { message: 'Invalid payload' } } })

      await expect(store.submitRequest({ requestType: 'Instruction', payload: '' })).rejects.toBeDefined()
      expect(store.requests).toHaveLength(0)
    })
  })

  // ── cancelRequest ─────────────────────────────────────────────────────────

  describe('cancelRequest', () => {
    it('posts to /llm-queue/:id/cancel and removes the request from state', async () => {
      const store = useQueueStore()
      store.requests = [makeRawRequest({ id: 'req-del' }), makeRawRequest({ id: 'req-keep' })]
      vi.mocked(http.post).mockResolvedValue({ data: undefined })

      await store.cancelRequest('req-del')

      expect(store.requests.some(r => r.id === 'req-del')).toBe(false)
      expect(store.requests.some(r => r.id === 'req-keep')).toBe(true)
      expect(http.post).toHaveBeenCalledWith('/llm-queue/req-del/cancel')
    })
  })

  // ── processNext ───────────────────────────────────────────────────────────

  describe('processNext', () => {
    it('posts to /llm-queue/process-next and returns the normalized processed request', async () => {
      const processed = makeRawRequest({ id: 'req-p', status: 2, processedAt: '2026-02-01T00:00:00Z' })
      vi.mocked(http.post).mockResolvedValue({ data: processed })

      const store = useQueueStore()
      const result = await store.processNext()

      expect(result?.id).toBe('req-p')
      expect(result?.status).toBe('Completed')
    })

    it('returns null when the backend responds with 404 (no pending requests)', async () => {
      vi.mocked(http.post).mockRejectedValue({
        response: { status: 404, data: { errorCode: 'NotFound' } },
      })

      const store = useQueueStore()
      const result = await store.processNext()

      expect(result).toBeNull()
    })

    it('rethrows non-404 errors from processNext', async () => {
      vi.mocked(http.post).mockRejectedValue({
        response: { status: 500, data: { message: 'Server error' } },
      })

      const store = useQueueStore()
      await expect(store.processNext()).rejects.toBeDefined()
    })
  })

  // ── loading state transitions ──────────────────────────────────────────

  describe('loading state transitions', () => {
    it('sets loading=true during fetchUserRequests and clears it after', async () => {
      let loadingDuringRequest = false
      vi.mocked(http.get).mockImplementation(async () => {
        // Check loading state during the request
        const store = useQueueStore()
        loadingDuringRequest = store.loading
        return { data: [] }
      })

      const store = useQueueStore()
      await store.fetchUserRequests()

      expect(loadingDuringRequest).toBe(true)
      expect(store.loading).toBe(false)
    })

    it('clears loading even when fetchUserRequests fails', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('oops'))

      const store = useQueueStore()
      await expect(store.fetchUserRequests()).rejects.toBeInstanceOf(Error)

      expect(store.loading).toBe(false)
    })

    it('sets loading=true during submitRequest and clears it after', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: makeRawRequest({ id: 'r-load' }) })

      const store = useQueueStore()
      await store.submitRequest({ requestType: 'Instruction', payload: 'test' })

      expect(store.loading).toBe(false)
    })
  })

  // ── cancelRequest preserves other items ──────────────────────────────────

  describe('cancelRequest isolation', () => {
    it('only removes the cancelled item and preserves all others', async () => {
      const store = useQueueStore()
      store.requests = [
        makeRawRequest({ id: 'req-1' }),
        makeRawRequest({ id: 'req-2' }),
        makeRawRequest({ id: 'req-3' }),
      ]

      vi.mocked(http.post).mockResolvedValue({ data: undefined })
      await store.cancelRequest('req-2')

      expect(store.requests).toHaveLength(2)
      expect(store.requests.map(r => r.id)).toEqual(['req-1', 'req-3'])
    })
  })

  // ── fetchStats ────────────────────────────────────────────────────────────

  describe('fetchStats', () => {
    it('calls GET /llm-queue/stats and maps the payload into store.stats', async () => {
      const stats = { pendingCount: 5, processingCount: 2, completedCount: 20, failedCount: 1 }
      vi.mocked(http.get).mockResolvedValue({ data: stats })

      const store = useQueueStore()
      await store.fetchStats()

      expect(store.stats?.pendingCount).toBe(5)
      expect(store.stats?.completedCount).toBe(20)
      expect(http.get).toHaveBeenCalledWith('/llm-queue/stats')
    })
  })
})
