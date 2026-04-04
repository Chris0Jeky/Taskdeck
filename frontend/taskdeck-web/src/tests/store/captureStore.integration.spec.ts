/**
 * captureStore integration tests — store + real captureApi module, HTTP layer mocked.
 *
 * These tests verify the store → captureApi → http path.  Mocking http (not
 * captureApi) catches any shape mismatches introduced between what the API
 * module returns and what the store state accepts.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import http from '../../api/http'
import { useCaptureStore } from '../../store/captureStore'

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

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: false }
})

function makeSummaryPayload(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: 'c-1',
    userId: 'u-1',
    boardId: null,
    status: 'New',
    source: 'Typed',
    textExcerpt: 'capture excerpt',
    createdAt: '2026-01-01T00:00:00Z',
    processedAt: null,
    ...overrides,
  }
}

function makeDetailPayload(overrides: Partial<Record<string, unknown>> = {}) {
  const { rawText, ...summaryOverrides } = overrides as { rawText?: string } & Record<string, unknown>
  return {
    ...makeSummaryPayload(summaryOverrides),
    rawText: rawText ?? 'full capture text',
    retryCount: 0,
    provenance: null,
  }
}

describe('captureStore — integration (real captureApi, mocked HTTP)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  // ── listItems → store.items ───────────────────────────────────────────────

  describe('fetchItems', () => {
    it('maps GET /capture/items response array into store.items', async () => {
      const items = [makeSummaryPayload(), makeSummaryPayload({ id: 'c-2', textExcerpt: 'second' })]
      vi.mocked(http.get).mockResolvedValue({ data: items })

      const store = useCaptureStore()
      await store.fetchItems({ limit: 50 })

      expect(store.items).toHaveLength(2)
      expect(store.items[0].id).toBe('c-1')
      expect(store.items[1].id).toBe('c-2')
      // Query string should be forwarded
      expect(http.get).toHaveBeenCalledWith(expect.stringContaining('limit=50'))
    })

    it('sets listError when GET /capture/items fails', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('network'))

      const store = useCaptureStore()
      await expect(store.fetchItems()).rejects.toBeInstanceOf(Error)

      expect(store.listError).toBe('Failed to load inbox items')
      // Existing items must not be cleared
    })

    it('filters query string: status is forwarded to the API URL', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      const store = useCaptureStore()
      await store.fetchItems({ status: 'Triaging' })

      const calledUrl: string = vi.mocked(http.get).mock.calls[0][0] as string
      expect(calledUrl).toContain('status=Triaging')
    })
  })

  // ── createItem → store.items prepended ───────────────────────────────────

  describe('createItem', () => {
    it('posts to /capture/items and prepends the returned item to the list', async () => {
      const detail = makeDetailPayload({ id: 'c-new', rawText: 'new text' })
      vi.mocked(http.post).mockResolvedValue({ data: detail })

      const store = useCaptureStore()
      await store.createItem({ boardId: null, text: 'new text' })

      expect(store.items[0].id).toBe('c-new')
      expect(store.detailById['c-new']?.rawText).toBe('new text')
      expect(http.post).toHaveBeenCalledWith('/capture/items', expect.objectContaining({ text: 'new text' }))
    })

    it('does not add a phantom summary when the API rejects with 400', async () => {
      vi.mocked(http.post).mockRejectedValue({ response: { status: 400, data: { message: 'Validation error' } } })

      const store = useCaptureStore()
      await expect(store.createItem({ boardId: null, text: '' })).rejects.toBeDefined()

      expect(store.items).toHaveLength(0)
    })
  })

  // ── getItem → detailById caching ──────────────────────────────────────────

  describe('fetchDetail', () => {
    it('fetches from GET /capture/items/:id and caches in detailById', async () => {
      const detail = makeDetailPayload({ id: 'c-42', rawText: 'detailed content' })
      vi.mocked(http.get).mockResolvedValue({ data: detail })

      const store = useCaptureStore()
      await store.fetchDetail('c-42')

      expect(store.detailById['c-42']?.rawText).toBe('detailed content')
      const calledUrl: string = vi.mocked(http.get).mock.calls[0][0] as string
      expect(calledUrl).toContain('/capture/items/c-42')
    })

    it('does not issue a second GET when detail is already cached', async () => {
      const detail = makeDetailPayload({ id: 'c-10' })
      vi.mocked(http.get).mockResolvedValue({ data: detail })

      const store = useCaptureStore()
      await store.fetchDetail('c-10')
      await store.fetchDetail('c-10')

      expect(http.get).toHaveBeenCalledTimes(1)
    })

    it('sets detailError when GET /capture/items/:id fails', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('not found'))

      const store = useCaptureStore()
      await expect(store.fetchDetail('missing')).rejects.toBeInstanceOf(Error)

      expect(store.detailError).toBe('Failed to load inbox item')
    })
  })

  // ── enqueueTriage → status transition ─────────────────────────────────────

  describe('triageItem', () => {
    it('posts to /capture/items/:id/triage and refreshes detail with GET after', async () => {
      const enqueueResponse = { id: 'c-5', status: 'Triaging', alreadyTriaging: false }
      const refreshedDetail = makeDetailPayload({ id: 'c-5', status: 'Triaging' })

      vi.mocked(http.post).mockResolvedValue({ data: enqueueResponse })
      vi.mocked(http.get).mockResolvedValue({ data: refreshedDetail })

      const store = useCaptureStore()
      await store.triageItem('c-5')

      // Triage post first, then GET for refresh
      expect(http.post).toHaveBeenCalledWith(
        expect.stringContaining('/capture/items/c-5/triage'),
      )
      expect(http.get).toHaveBeenCalledWith(expect.stringContaining('/capture/items/c-5'))
      expect(store.detailById['c-5']?.status).toBe('Triaging')
    })

    it('sets actionError when the triage enqueue POST fails', async () => {
      vi.mocked(http.post).mockRejectedValue(new Error('queue full'))

      const store = useCaptureStore()
      await expect(store.triageItem('c-6')).rejects.toBeInstanceOf(Error)

      expect(store.actionError).toBe('Failed to triage capture item')
    })

    it('optimistically marks detail as Triaging before the refresh GET completes', async () => {
      const createdAt = '2026-01-01T00:00:00Z'
      let resolveGet!: (val: unknown) => void
      let resolvePost!: (val: unknown) => void

      vi.mocked(http.post).mockReturnValue(new Promise<unknown>((resolve) => { resolvePost = resolve }))
      vi.mocked(http.get).mockReturnValue(new Promise<unknown>((resolve) => { resolveGet = resolve }))

      const store = useCaptureStore()
      store.detailById['c-opt'] = makeDetailPayload({ id: 'c-opt', status: 'New', createdAt })

      const promise = store.triageItem('c-opt')

      // Resolve the POST first — after this, optimistic state should be set.
      // The triageItem flow involves multiple await hops (http.post → enqueueTriage → triageItem),
      // so we flush the microtask queue three times to ensure all continuations run.
      resolvePost({ data: { id: 'c-opt', status: 'Triaging', alreadyTriaging: false } })
      await Promise.resolve()
      await Promise.resolve()
      await Promise.resolve()

      // Status must already be Triaging even though GET has not resolved yet
      expect(store.detailById['c-opt']?.status).toBe('Triaging')

      resolveGet({ data: makeDetailPayload({ id: 'c-opt', status: 'Triaging', createdAt }) })
      await promise
    })
  })

  // ── ignoreItem ────────────────────────────────────────────────────────────

  describe('ignoreItem', () => {
    it('posts to ignore endpoint then refreshes detail', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: undefined })
      const ignored = makeDetailPayload({ id: 'c-7', status: 'Ignored' })
      vi.mocked(http.get).mockResolvedValue({ data: ignored })

      const store = useCaptureStore()
      await store.ignoreItem('c-7')

      expect(http.post).toHaveBeenCalledWith(expect.stringContaining('/capture/items/c-7/ignore'))
      expect(store.detailById['c-7']?.status).toBe('Ignored')
    })
  })

  // ── updateSuggestion ──────────────────────────────────────────────────────

  describe('updateSuggestion', () => {
    it('sends PUT /capture/items/:id/suggestion and caches the updated detail', async () => {
      const updated = makeDetailPayload({ id: 'c-8', rawText: 'edited text' })
      vi.mocked(http.put).mockResolvedValue({ data: updated })

      const store = useCaptureStore()
      const result = await store.updateSuggestion('c-8', { text: 'edited text' })

      expect(result.rawText).toBe('edited text')
      expect(store.detailById['c-8']?.rawText).toBe('edited text')
      expect(http.put).toHaveBeenCalledWith(
        expect.stringContaining('/capture/items/c-8/suggestion'),
        expect.objectContaining({ text: 'edited text' }),
      )
    })

    it('sets actionError when suggestion PUT fails', async () => {
      vi.mocked(http.put).mockRejectedValue(new Error('server error'))

      const store = useCaptureStore()
      await expect(store.updateSuggestion('c-9', { text: 'new' })).rejects.toBeInstanceOf(Error)

      expect(store.actionError).toBe('Failed to update capture text')
    })
  })

  // ── batchTriage ───────────────────────────────────────────────────────────

  describe('batchTriage', () => {
    it('posts to /capture/items/batch-triage with items array and refreshes list on success', async () => {
      const batchResult = { total: 2, succeeded: 2, failed: 0, results: [
        { itemId: 'c-1', success: true },
        { itemId: 'c-2', success: true },
      ] }
      vi.mocked(http.post).mockResolvedValue({ data: batchResult })
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      const store = useCaptureStore()
      const result = await store.batchTriage(['c-1', 'c-2'], 'triage')

      expect(result.succeeded).toBe(2)
      expect(http.post).toHaveBeenCalledWith(
        '/capture/items/batch-triage',
        expect.objectContaining({
          items: [
            { itemId: 'c-1', action: 'triage' },
            { itemId: 'c-2', action: 'triage' },
          ],
        }),
      )
      // List should be refreshed after batch
      expect(http.get).toHaveBeenCalledWith(expect.stringContaining('/capture/items'))
    })

    it('sets batchError when the batch POST fails', async () => {
      vi.mocked(http.post).mockRejectedValue(new Error('network'))

      const store = useCaptureStore()
      await expect(store.batchTriage(['c-1'], 'ignore')).rejects.toBeInstanceOf(Error)

      expect(store.batchError).toBe('Failed to process batch triage')
    })
  })
})
