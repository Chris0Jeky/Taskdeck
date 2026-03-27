import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { captureApi } from '../../api/captureApi'
import { useCaptureStore } from '../../store/captureStore'

const toastMocks = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
}))

vi.mock('../../api/captureApi', () => ({
  captureApi: {
    createItem: vi.fn(),
    listItems: vi.fn(),
    getItem: vi.fn(),
    ignoreItem: vi.fn(),
    cancelItem: vi.fn(),
    enqueueTriage: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

describe('captureStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('loads capture summaries', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.listItems).mockResolvedValue([
      {
        id: 'c1',
        userId: 'u1',
        boardId: null,
        status: 'New',
        source: 'Typed',
        textExcerpt: 'excerpt',
        createdAt: new Date().toISOString(),
        processedAt: null,
      },
    ])

    await store.fetchItems({ limit: 100 })

    expect(store.items).toHaveLength(1)
    expect(captureApi.listItems).toHaveBeenCalledWith({ limit: 100 })
  })

  it('loads and caches capture details', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.getItem).mockResolvedValue({
      id: 'c2',
      userId: 'u1',
      boardId: null,
      status: 'Triaging',
      source: 'Typed',
      textExcerpt: 'excerpt',
      rawText: 'full text',
      createdAt: new Date().toISOString(),
      processedAt: null,
      retryCount: 0,
    })

    await store.fetchDetail('c2')
    await store.fetchDetail('c2')

    expect(captureApi.getItem).toHaveBeenCalledTimes(1)
    expect(store.detailById.c2?.rawText).toBe('full text')
  })

  it('does not overwrite fresher list summaries when a cached detail is reopened', async () => {
    const store = useCaptureStore()
    const createdAt = new Date().toISOString()

    store.items = [
      {
        id: 'c2',
        userId: 'u1',
        boardId: 'b1',
        status: 'Triaging',
        source: 'Typed',
        textExcerpt: 'fresh summary excerpt',
        createdAt,
        processedAt: createdAt,
      },
    ]
    store.detailById.c2 = {
      id: 'c2',
      userId: 'u1',
      boardId: 'b1',
      status: 'New',
      source: 'Typed',
      textExcerpt: 'stale cached excerpt',
      rawText: 'cached detail',
      createdAt,
      processedAt: null,
      retryCount: 0,
    }

    await store.fetchDetail('c2')

    expect(captureApi.getItem).not.toHaveBeenCalled()
    expect(store.items[0]).toMatchObject({
      id: 'c2',
      status: 'Triaging',
      textExcerpt: 'fresh summary excerpt',
      processedAt: createdAt,
    })
    expect(store.detailById.c2?.rawText).toBe('cached detail')
  })

  it('can force-refresh a detail peek without mutating the current list summary', async () => {
    const store = useCaptureStore()
    const createdAt = new Date().toISOString()

    store.items = [
      {
        id: 'c2',
        userId: 'u1',
        boardId: 'b1',
        status: 'Triaging',
        source: 'Typed',
        textExcerpt: 'fresh summary excerpt',
        createdAt,
        processedAt: createdAt,
      },
    ]
    store.detailById.c2 = {
      id: 'c2',
      userId: 'u1',
      boardId: 'b1',
      status: 'New',
      source: 'Typed',
      textExcerpt: 'stale cached excerpt',
      rawText: 'cached detail',
      createdAt,
      processedAt: null,
      retryCount: 0,
    }
    vi.mocked(captureApi.getItem).mockResolvedValue({
      id: 'c2',
      userId: 'u1',
      boardId: 'b1',
      status: 'ProposalCreated',
      source: 'Typed',
      textExcerpt: 'fresh detail excerpt',
      rawText: 'fresh detail',
      createdAt,
      processedAt: createdAt,
      retryCount: 0,
    })

    const detail = await store.peekDetail('c2', {
      forceRefresh: true,
      recordError: false,
      showToast: false,
    })

    expect(captureApi.getItem).toHaveBeenCalledWith('c2')
    expect(detail).toMatchObject({
      id: 'c2',
      status: 'ProposalCreated',
      rawText: 'fresh detail',
    })
    expect(store.detailById.c2).toMatchObject({
      status: 'New',
      rawText: 'cached detail',
    })
    expect(store.items[0]).toMatchObject({
      status: 'Triaging',
      textExcerpt: 'fresh summary excerpt',
      processedAt: createdAt,
    })
  })

  it('returns cached detail from peekDetail without reloading the API', async () => {
    const store = useCaptureStore()
    const createdAt = new Date().toISOString()

    store.detailById.c2 = {
      id: 'c2',
      userId: 'u1',
      boardId: 'b1',
      status: 'New',
      source: 'Typed',
      textExcerpt: 'cached detail excerpt',
      rawText: 'cached detail text',
      createdAt,
      processedAt: null,
      retryCount: 0,
    }

    const detail = await store.peekDetail('c2')

    expect(detail).toMatchObject({
      id: 'c2',
      rawText: 'cached detail text',
    })
    expect(captureApi.getItem).not.toHaveBeenCalled()
  })

  it('creates capture items and prepends summary', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.createItem).mockResolvedValue({
      id: 'c3',
      userId: 'u1',
      boardId: null,
      status: 'New',
      source: 'Typed',
      textExcerpt: 'new excerpt',
      rawText: 'new full text',
      createdAt: new Date().toISOString(),
      processedAt: null,
      retryCount: 0,
    })

    await store.createItem({
      boardId: null,
      text: 'new full text',
    })

    expect(store.items[0].id).toBe('c3')
    expect(store.detailById.c3?.rawText).toBe('new full text')
    expect(toastMocks.success).toHaveBeenCalledWith('Capture saved to inbox')
  })

  it('updates selection detail after ignore action', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.ignoreItem).mockResolvedValue(undefined)
    vi.mocked(captureApi.getItem).mockResolvedValue({
      id: 'c4',
      userId: 'u1',
      boardId: null,
      status: 'Ignored',
      source: 'Typed',
      textExcerpt: 'ignored',
      rawText: 'full text',
      createdAt: new Date().toISOString(),
      processedAt: new Date().toISOString(),
      retryCount: 0,
    })

    await store.ignoreItem('c4')

    expect(captureApi.ignoreItem).toHaveBeenCalledWith('c4')
    expect(store.detailById.c4?.status).toBe('Ignored')
  })

  it('surfaces errors when list loading fails', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.listItems).mockRejectedValue(new Error('network'))

    await expect(store.fetchItems()).rejects.toBeInstanceOf(Error)

    expect(store.listError).toBe('Failed to load inbox items')
    expect(toastMocks.error).toHaveBeenCalledWith('Failed to load inbox items')
  })

  it('keeps list error intact when detail loading fails', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.listItems).mockRejectedValueOnce(new Error('network'))
    vi.mocked(captureApi.getItem).mockRejectedValueOnce(new Error('detail-network'))

    await expect(store.fetchItems()).rejects.toBeInstanceOf(Error)
    await expect(store.fetchDetail('c5')).rejects.toBeInstanceOf(Error)

    expect(store.listError).toBe('Failed to load inbox items')
    expect(store.detailError).toBe('Failed to load inbox item')
  })

  it('stores action errors separately from list/detail errors', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.cancelItem).mockRejectedValueOnce(new Error('cancel-network'))

    await expect(store.cancelItem('c6')).rejects.toBeInstanceOf(Error)

    expect(store.actionError).toBe('Failed to cancel capture item')
    expect(store.listError).toBeNull()
    expect(store.detailError).toBeNull()
  })

  it('can silently force-refresh a detail peek without recording view error state', async () => {
    const store = useCaptureStore()
    store.detailError = 'existing detail error'
    vi.mocked(captureApi.getItem).mockRejectedValueOnce(new Error('detail-network'))

    await expect(store.peekDetail('c10', {
      forceRefresh: true,
      recordError: false,
      showToast: false,
    })).rejects.toBeInstanceOf(Error)

    expect(store.detailError).toBe('existing detail error')
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('records detail errors and shows a toast when a default peekDetail request fails', async () => {
    const store = useCaptureStore()
    store.detailError = 'old detail error'
    vi.mocked(captureApi.getItem).mockRejectedValueOnce(new Error('detail-network'))

    await expect(store.peekDetail('c11')).rejects.toBeInstanceOf(Error)

    expect(store.detailError).toBe('Failed to load inbox item')
    expect(toastMocks.error).toHaveBeenCalledWith('Failed to load inbox item')
  })

  it('enqueues triage and refreshes detail state', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.enqueueTriage).mockResolvedValue({
      id: 'c7',
      status: 'Triaging',
      alreadyTriaging: false,
    })
    vi.mocked(captureApi.getItem).mockResolvedValue({
      id: 'c7',
      userId: 'u1',
      boardId: 'b1',
      status: 'Triaging',
      source: 'Typed',
      textExcerpt: 'triaging',
      rawText: 'triage me',
      createdAt: new Date().toISOString(),
      processedAt: null,
      retryCount: 0,
      provenance: null,
    })

    await store.triageItem('c7')

    expect(captureApi.enqueueTriage).toHaveBeenCalledWith('c7')
    expect(captureApi.getItem).toHaveBeenCalledWith('c7')
    expect(store.detailById.c7?.status).toBe('Triaging')
    expect(toastMocks.success).toHaveBeenCalledWith('Capture item triage queued')
  })

  it('optimistically updates cached detail status without overwriting a fresher summary when triage starts from an open item', async () => {
    const store = useCaptureStore()
    const createdAt = new Date().toISOString()
    let resolveDetailRefresh: ((value: Awaited<ReturnType<typeof captureApi.getItem>>) => void) | null = null

    store.items = [
      {
        id: 'c7-detail',
        userId: 'u1',
        boardId: 'b1',
        status: 'New',
        source: 'Typed',
        textExcerpt: 'summary excerpt',
        createdAt,
        processedAt: null,
      },
    ]
    store.detailById['c7-detail'] = {
      id: 'c7-detail',
      userId: 'u1',
      boardId: 'b1',
      status: 'New',
      source: 'Typed',
      textExcerpt: 'detail excerpt',
      rawText: 'detail text',
      createdAt,
      processedAt: null,
      retryCount: 0,
      provenance: null,
    }

    vi.mocked(captureApi.enqueueTriage).mockResolvedValue({
      id: 'c7-detail',
      status: 'Triaging',
      alreadyTriaging: false,
    })
    vi.mocked(captureApi.getItem).mockImplementationOnce(() => new Promise((resolve) => {
      resolveDetailRefresh = resolve
    }))

    const triagePromise = store.triageItem('c7-detail')
    await Promise.resolve()

    expect(store.detailById['c7-detail']).toMatchObject({
      status: 'Triaging',
      rawText: 'detail text',
    })
    expect(store.items[0]).toMatchObject({
      id: 'c7-detail',
      status: 'Triaging',
      textExcerpt: 'summary excerpt',
      processedAt: null,
    })

    resolveDetailRefresh?.({
      id: 'c7-detail',
      userId: 'u1',
      boardId: 'b1',
      status: 'Triaging',
      source: 'Typed',
      textExcerpt: 'refreshed detail excerpt',
      rawText: 'refreshed detail text',
      createdAt,
      processedAt: createdAt,
      retryCount: 1,
      provenance: null,
    })
    await triagePromise

    expect(store.detailById['c7-detail']).toMatchObject({
      status: 'Triaging',
      rawText: 'refreshed detail text',
    })
    expect(store.items[0]).toMatchObject({
      id: 'c7-detail',
      status: 'Triaging',
      textExcerpt: 'refreshed detail excerpt',
      processedAt: createdAt,
    })
  })

  it('keeps the fresher summary visible when triage refresh fails after starting from stale cached detail', async () => {
    const store = useCaptureStore()
    const createdAt = new Date().toISOString()

    store.items = [
      {
        id: 'c7-stale',
        userId: 'u1',
        boardId: 'b1',
        status: 'ProposalCreated',
        source: 'Typed',
        textExcerpt: 'fresh summary excerpt',
        createdAt,
        processedAt: createdAt,
      },
    ]
    store.detailById['c7-stale'] = {
      id: 'c7-stale',
      userId: 'u1',
      boardId: 'b1',
      status: 'New',
      source: 'Typed',
      textExcerpt: 'stale detail excerpt',
      rawText: 'stale detail text',
      createdAt,
      processedAt: null,
      retryCount: 0,
      provenance: null,
    }

    vi.mocked(captureApi.enqueueTriage).mockResolvedValue({
      id: 'c7-stale',
      status: 'Triaging',
      alreadyTriaging: false,
    })
    vi.mocked(captureApi.getItem).mockRejectedValueOnce(new Error('detail-refresh-failed'))

    await expect(store.triageItem('c7-stale')).rejects.toBeInstanceOf(Error)

    expect(store.detailById['c7-stale']).toMatchObject({
      status: 'Triaging',
      textExcerpt: 'stale detail excerpt',
      rawText: 'stale detail text',
    })
    expect(store.items[0]).toMatchObject({
      id: 'c7-stale',
      status: 'Triaging',
      textExcerpt: 'fresh summary excerpt',
      processedAt: createdAt,
    })
  })

  it('optimistically updates summary status when triage starts without cached detail', async () => {
    const store = useCaptureStore()
    const createdAt = new Date().toISOString()
    let resolveDetailRefresh: ((value: Awaited<ReturnType<typeof captureApi.getItem>>) => void) | null = null

    store.items = [
      {
        id: 'c7-summary',
        userId: 'u1',
        boardId: 'b1',
        status: 'New',
        source: 'Typed',
        textExcerpt: 'summary excerpt',
        createdAt,
        processedAt: null,
      },
    ]

    vi.mocked(captureApi.enqueueTriage).mockResolvedValue({
      id: 'c7-summary',
      status: 'Triaging',
      alreadyTriaging: false,
    })
    vi.mocked(captureApi.getItem).mockImplementationOnce(() => new Promise((resolve) => {
      resolveDetailRefresh = resolve
    }))

    const triagePromise = store.triageItem('c7-summary')
    await Promise.resolve()

    expect(store.detailById['c7-summary']).toBeUndefined()
    expect(store.items[0]).toMatchObject({
      id: 'c7-summary',
      status: 'Triaging',
      textExcerpt: 'summary excerpt',
    })

    resolveDetailRefresh?.({
      id: 'c7-summary',
      userId: 'u1',
      boardId: 'b1',
      status: 'Triaging',
      source: 'Typed',
      textExcerpt: 'refreshed summary excerpt',
      rawText: 'refreshed detail text',
      createdAt,
      processedAt: createdAt,
      retryCount: 1,
      provenance: null,
    })
    await triagePromise

    expect(store.detailById['c7-summary']).toMatchObject({
      status: 'Triaging',
      rawText: 'refreshed detail text',
    })
    expect(store.items[0]).toMatchObject({
      id: 'c7-summary',
      status: 'Triaging',
      textExcerpt: 'refreshed summary excerpt',
    })
  })

  it('stores action error when triage enqueue fails', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.enqueueTriage).mockRejectedValueOnce(new Error('triage-network'))

    await expect(store.triageItem('c8')).rejects.toBeInstanceOf(Error)

    expect(store.actionError).toBe('Failed to triage capture item')
    expect(toastMocks.error).toHaveBeenCalledWith('Failed to triage capture item')
  })

  describe('pollTriageCompletion', () => {
    it('polls until terminal status and updates cached detail', async () => {
      vi.useFakeTimers()
      const store = useCaptureStore()
      const createdAt = new Date().toISOString()
      let callCount = 0

      vi.mocked(captureApi.getItem).mockImplementation(async () => {
        callCount++
        return {
          id: 'poll-1',
          userId: 'u1',
          boardId: 'b1',
          status: callCount < 3 ? 'Triaging' : 'ProposalCreated',
          source: 'Typed' as const,
          textExcerpt: 'excerpt',
          rawText: 'full text',
          createdAt,
          processedAt: callCount < 3 ? null : createdAt,
          retryCount: 0,
          provenance: callCount < 3 ? null : { captureItemId: 'poll-1', triageRunId: 'tr1', proposalId: 'p1', promptVersion: 'triage.v1' },
        }
      })

      const stop = store.pollTriageCompletion('poll-1')
      expect(store.triagePollingItemId).toBe('poll-1')

      // First tick at 2s — still Triaging
      await vi.advanceTimersByTimeAsync(2_000)
      expect(callCount).toBe(1)
      expect(store.detailById['poll-1']?.status).toBe('Triaging')

      // Second tick at 4s — still Triaging
      await vi.advanceTimersByTimeAsync(2_000)
      expect(callCount).toBe(2)
      expect(store.detailById['poll-1']?.status).toBe('Triaging')

      // Third tick at 6s — now ProposalCreated, polling should stop
      await vi.advanceTimersByTimeAsync(2_000)
      expect(callCount).toBe(3)
      expect(store.detailById['poll-1']?.status).toBe('ProposalCreated')
      expect(store.triagePollingItemId).toBeNull()

      // No more ticks after terminal
      await vi.advanceTimersByTimeAsync(4_000)
      expect(callCount).toBe(3)

      stop()
      vi.useRealTimers()
    })

    it('stops polling when stop function is called', async () => {
      vi.useFakeTimers()
      const store = useCaptureStore()
      let callCount = 0

      vi.mocked(captureApi.getItem).mockImplementation(async () => {
        callCount++
        return {
          id: 'poll-2',
          userId: 'u1',
          boardId: null,
          status: 'Triaging' as const,
          source: 'Typed' as const,
          textExcerpt: 'excerpt',
          rawText: 'full text',
          createdAt: new Date().toISOString(),
          processedAt: null,
          retryCount: 0,
        }
      })

      const stop = store.pollTriageCompletion('poll-2')
      await vi.advanceTimersByTimeAsync(2_000)
      expect(callCount).toBe(1)

      stop()
      expect(store.triagePollingItemId).toBeNull()

      await vi.advanceTimersByTimeAsync(4_000)
      expect(callCount).toBe(1)

      vi.useRealTimers()
    })

    it('stops after max polls without terminal status', async () => {
      vi.useFakeTimers()
      const store = useCaptureStore()

      vi.mocked(captureApi.getItem).mockResolvedValue({
        id: 'poll-3',
        userId: 'u1',
        boardId: null,
        status: 'Triaging',
        source: 'Typed',
        textExcerpt: 'excerpt',
        rawText: 'full text',
        createdAt: new Date().toISOString(),
        processedAt: null,
        retryCount: 0,
      })

      store.pollTriageCompletion('poll-3')

      // Advance through all 15 polls (15 * 2s = 30s)
      for (let i = 0; i < 15; i++) {
        await vi.advanceTimersByTimeAsync(2_000)
      }
      expect(store.triagePollingItemId).toBeNull()

      // No more calls after max
      const callsBefore = vi.mocked(captureApi.getItem).mock.calls.length
      await vi.advanceTimersByTimeAsync(4_000)
      expect(vi.mocked(captureApi.getItem).mock.calls.length).toBe(callsBefore)

      vi.useRealTimers()
    })

    it('continues polling after transient API errors', async () => {
      vi.useFakeTimers()
      const store = useCaptureStore()
      let callCount = 0

      vi.mocked(captureApi.getItem).mockImplementation(async () => {
        callCount++
        if (callCount === 1) throw new Error('transient')
        return {
          id: 'poll-4',
          userId: 'u1',
          boardId: null,
          status: 'ProposalCreated' as const,
          source: 'Typed' as const,
          textExcerpt: 'excerpt',
          rawText: 'full text',
          createdAt: new Date().toISOString(),
          processedAt: new Date().toISOString(),
          retryCount: 0,
        }
      })

      store.pollTriageCompletion('poll-4')

      // First tick — error, but keeps going
      await vi.advanceTimersByTimeAsync(2_000)
      expect(callCount).toBe(1)

      // Second tick — success with terminal status
      await vi.advanceTimersByTimeAsync(2_000)
      expect(callCount).toBe(2)
      expect(store.detailById['poll-4']?.status).toBe('ProposalCreated')
      expect(store.triagePollingItemId).toBeNull()

      vi.useRealTimers()
    })

    it('stops any existing poll before starting a new one', async () => {
      vi.useFakeTimers()
      const store = useCaptureStore()

      vi.mocked(captureApi.getItem).mockImplementation(async (itemId: string) => ({
        id: itemId,
        userId: 'u1',
        boardId: null,
        status: 'Triaging',
        source: 'Typed',
        textExcerpt: `${itemId} excerpt`,
        rawText: `${itemId} full text`,
        createdAt: new Date().toISOString(),
        processedAt: null,
        retryCount: 0,
      }))

      store.pollTriageCompletion('poll-old')
      store.pollTriageCompletion('poll-new')

      expect(store.triagePollingItemId).toBe('poll-new')

      await vi.advanceTimersByTimeAsync(2_000)

      expect(captureApi.getItem).toHaveBeenCalledTimes(1)
      expect(captureApi.getItem).toHaveBeenCalledWith('poll-new')
      expect(store.triagePollingItemId).toBe('poll-new')

      vi.useRealTimers()
    })
  })

  it('emits a single triage error toast when detail refresh fails after enqueue', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.enqueueTriage).mockResolvedValue({
      id: 'c9',
      status: 'Triaging',
      alreadyTriaging: false,
    })
    vi.mocked(captureApi.getItem).mockRejectedValueOnce(new Error('detail-refresh-failed'))

    await expect(store.triageItem('c9')).rejects.toBeInstanceOf(Error)

    expect(toastMocks.error).toHaveBeenCalledTimes(1)
    expect(toastMocks.error).toHaveBeenCalledWith('Failed to triage capture item')
  })
})
