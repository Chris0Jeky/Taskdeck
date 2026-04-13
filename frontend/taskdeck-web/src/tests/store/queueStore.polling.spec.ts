/**
 * queueStore polling and state transition integration tests.
 *
 * These tests exercise:
 * - Queue item state transitions (Pending → Processing → Completed)
 * - Server-side item deletion (phantom entry removal)
 * - Stale state reconciliation on re-fetch
 * - Concurrent operations (submit while fetch in flight)
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

function makeRequest(overrides: Partial<Record<string, unknown>> = {}) {
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

describe('queueStore — polling and state transitions', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  // ── state transitions via re-fetch ────────────────────────────────────────

  describe('state transitions via sequential fetches', () => {
    it('reflects Pending → Processing → Completed transition across re-fetches', async () => {
      const store = useQueueStore()

      // First fetch: item is Pending
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [makeRequest({ id: 'req-transition', status: 'Pending' })],
      })
      await store.fetchUserRequests()
      expect(store.requests[0].status).toBe('Pending')

      // Second fetch: item is Processing
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [makeRequest({ id: 'req-transition', status: 'Processing' })],
      })
      await store.fetchUserRequests()
      expect(store.requests[0].status).toBe('Processing')

      // Third fetch: item is Completed
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [makeRequest({ id: 'req-transition', status: 'Completed', processedAt: '2026-01-02T00:00:00Z' })],
      })
      await store.fetchUserRequests()
      expect(store.requests[0].status).toBe('Completed')
      expect(store.requests[0].processedAt).toBe('2026-01-02T00:00:00Z')
    })

    it('reflects Pending → Failed transition with error message', async () => {
      const store = useQueueStore()

      vi.mocked(http.get).mockResolvedValueOnce({
        data: [makeRequest({ id: 'req-fail', status: 'Pending' })],
      })
      await store.fetchUserRequests()
      expect(store.requests[0].status).toBe('Pending')

      vi.mocked(http.get).mockResolvedValueOnce({
        data: [makeRequest({ id: 'req-fail', status: 'Failed', errorMessage: 'Provider timeout' })],
      })
      await store.fetchUserRequests()
      expect(store.requests[0].status).toBe('Failed')
      expect(store.requests[0].errorMessage).toBe('Provider timeout')
    })
  })

  // ── server-side deletion (phantom entry) ──────────────────────────────────

  describe('server-side deletion', () => {
    it('removes items from store that no longer exist on the server', async () => {
      const store = useQueueStore()

      // Initial fetch: two items
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [
          makeRequest({ id: 'req-alive' }),
          makeRequest({ id: 'req-ghost' }),
        ],
      })
      await store.fetchUserRequests()
      expect(store.requests).toHaveLength(2)

      // Re-fetch: server deleted req-ghost
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [makeRequest({ id: 'req-alive' })],
      })
      await store.fetchUserRequests()
      expect(store.requests).toHaveLength(1)
      expect(store.requests[0].id).toBe('req-alive')
    })

    it('handles empty list when all items are deleted server-side', async () => {
      const store = useQueueStore()

      vi.mocked(http.get).mockResolvedValueOnce({
        data: [makeRequest({ id: 'req-temp' })],
      })
      await store.fetchUserRequests()
      expect(store.requests).toHaveLength(1)

      vi.mocked(http.get).mockResolvedValueOnce({ data: [] })
      await store.fetchUserRequests()
      expect(store.requests).toHaveLength(0)
    })
  })

  // ── stale state reconciliation ────────────────────────────────────────────

  describe('stale state reconciliation', () => {
    it('replaces cached items with fresh data on re-fetch', async () => {
      const store = useQueueStore()

      // Initial fetch: old data
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [
          makeRequest({ id: 'req-stale', status: 'Pending', retryCount: 0 }),
        ],
      })
      await store.fetchUserRequests()

      // Re-fetch: server returns updated data (retried)
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [
          makeRequest({ id: 'req-stale', status: 'Processing', retryCount: 2 }),
        ],
      })
      await store.fetchUserRequests()

      expect(store.requests[0].retryCount).toBe(2)
      expect(store.requests[0].status).toBe('Processing')
    })

    it('adds new server-side items that were not in local state', async () => {
      const store = useQueueStore()

      // Initial fetch: one item
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [makeRequest({ id: 'req-1' })],
      })
      await store.fetchUserRequests()

      // Re-fetch: server has a new item (e.g., submitted from another device)
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [
          makeRequest({ id: 'req-1' }),
          makeRequest({ id: 'req-2', status: 'Processing' }),
        ],
      })
      await store.fetchUserRequests()

      expect(store.requests).toHaveLength(2)
      expect(store.requests.find(r => r.id === 'req-2')?.status).toBe('Processing')
    })
  })

  // ── submit while existing items present ──────────────────────────────────

  describe('submit preserves existing items', () => {
    it('appends new request to the end of the existing list', async () => {
      const store = useQueueStore()

      // Start with one existing request
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [makeRequest({ id: 'req-existing', status: 'Processing' })],
      })
      await store.fetchUserRequests()

      // Submit a new request
      const newRequest = makeRequest({ id: 'req-submitted', status: 'Pending' })
      vi.mocked(http.post).mockResolvedValue({ data: newRequest })

      await store.submitRequest({
        requestType: 'Instruction',
        payload: 'new task',
        boardId: 'board-1',
      })

      expect(store.requests).toHaveLength(2)
      expect(store.requests[0].id).toBe('req-existing')
      expect(store.requests[1].id).toBe('req-submitted')
    })
  })

  // ── cancel while other items in-flight ────────────────────────────────────

  describe('cancel isolation', () => {
    it('cancel only removes the targeted item, leaving others in their current state', async () => {
      const store = useQueueStore()
      store.requests = [
        makeRequest({ id: 'req-cancel', status: 'Pending' }),
        makeRequest({ id: 'req-processing', status: 'Processing' }),
        makeRequest({ id: 'req-completed', status: 'Completed' }),
      ]

      vi.mocked(http.post).mockResolvedValue({ data: undefined })
      await store.cancelRequest('req-cancel')

      expect(store.requests).toHaveLength(2)
      expect(store.requests.map(r => r.id)).toEqual(['req-processing', 'req-completed'])
      // Other items must retain their original status
      expect(store.requests[0].status).toBe('Processing')
      expect(store.requests[1].status).toBe('Completed')
    })

    it('keeps the item in the list when cancel API fails', async () => {
      const store = useQueueStore()
      store.requests = [makeRequest({ id: 'req-cant-cancel', status: 'Processing' })]

      vi.mocked(http.post).mockRejectedValue({
        response: { status: 409, data: { message: 'Cannot cancel a processing request' } },
      })

      await expect(store.cancelRequest('req-cant-cancel')).rejects.toBeDefined()

      // Item must still be in the list
      expect(store.requests).toHaveLength(1)
      expect(store.requests[0].id).toBe('req-cant-cancel')
    })
  })

  // ── processNext interaction with existing state ──────────────────────────

  describe('processNext', () => {
    it('returns the processed request with normalized status', async () => {
      const processed = makeRequest({
        id: 'req-processed',
        status: 'Completed',
        processedAt: '2026-02-01T00:00:00Z',
      })
      vi.mocked(http.post).mockResolvedValue({ data: processed })

      const store = useQueueStore()
      const result = await store.processNext()

      expect(result?.status).toBe('Completed')
      expect(result?.processedAt).toBe('2026-02-01T00:00:00Z')
    })

    it('normalizes numeric status from processNext response', async () => {
      vi.mocked(http.post).mockResolvedValue({
        data: makeRequest({ id: 'req-numeric', status: 2 }),
      })

      const store = useQueueStore()
      const result = await store.processNext()

      expect(result?.status).toBe('Completed')
    })
  })

  // ── fetchStats updates stats independently from requests ──────────────────

  describe('fetchStats independence', () => {
    it('updates stats without affecting the requests list', async () => {
      const store = useQueueStore()

      // Load requests
      vi.mocked(http.get).mockResolvedValueOnce({
        data: [makeRequest({ id: 'req-1' })],
      })
      await store.fetchUserRequests()
      expect(store.requests).toHaveLength(1)

      // Fetch stats separately
      vi.mocked(http.get).mockResolvedValueOnce({
        data: { pendingCount: 10, processingCount: 3, completedCount: 50, failedCount: 2 },
      })
      await store.fetchStats()

      // Both should coexist
      expect(store.requests).toHaveLength(1)
      expect(store.stats?.pendingCount).toBe(10)
      expect(store.stats?.completedCount).toBe(50)
    })
  })
})
