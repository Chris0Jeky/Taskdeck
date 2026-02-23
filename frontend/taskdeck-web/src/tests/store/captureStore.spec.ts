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
})
