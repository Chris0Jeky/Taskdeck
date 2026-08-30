import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { captureApi } from '../../api/captureApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}))

describe('captureApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('queries capture items with filters', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await captureApi.listItems({ status: 'Triaging', limit: 20 })

    expect(http.get).toHaveBeenCalledWith('/capture/items?status=Triaging&limit=20')
  })

  it('forwards abortable fail-fast options only for background capture reads', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })
    const controller = new AbortController()
    const options = { signal: controller.signal, skipRetry: true }

    await captureApi.listItems({ boardId: 'board-1', limit: 200 }, options)
    await captureApi.getItem('capture-42', options)

    expect(http.get).toHaveBeenNthCalledWith(
      1,
      '/capture/items?boardId=board-1&limit=200',
      options,
    )
    expect(http.get).toHaveBeenNthCalledWith(2, '/capture/items/capture-42', options)
  })

  it('creates a capture item', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { id: 'capture-1' } })

    await captureApi.createItem({
      boardId: null,
      text: 'capture text',
    })

    expect(http.post).toHaveBeenCalledWith('/capture/items', {
      boardId: null,
      text: 'capture text',
    })
  })

  it('preserves composer due date and label names in the capture request', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { id: 'capture-2' } })

    await captureApi.createItem({
      boardId: 'board-1',
      text: 'Buy milk',
      dueDate: '2026-08-23',
      labels: ['shopping'],
    })

    expect(http.post).toHaveBeenCalledWith('/capture/items', {
      boardId: 'board-1',
      text: 'Buy milk',
      dueDate: '2026-08-23',
      labels: ['shopping'],
    })
  })

  it('loads capture item detail by id', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: { id: 'capture-42' } })

    await captureApi.getItem('capture-42')

    expect(http.get).toHaveBeenCalledWith('/capture/items/capture-42')
  })

  it('posts keep and archive dispositions and returns their receipts', async () => {
    vi.mocked(http.post)
      .mockResolvedValueOnce({ data: { id: 'capture-2', disposition: { kind: 'Kept' } } })
      .mockResolvedValueOnce({ data: { id: 'capture-3', disposition: { kind: 'Archived' } } })

    const kept = await captureApi.keepItem('capture-2')
    const archived = await captureApi.archiveItem('capture-3')

    expect(http.post).toHaveBeenNthCalledWith(1, '/capture/items/capture-2/keep')
    expect(http.post).toHaveBeenNthCalledWith(2, '/capture/items/capture-3/archive')
    expect(kept.disposition?.kind).toBe('Kept')
    expect(archived.disposition?.kind).toBe('Archived')
  })

  it('posts ignore action', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: {} })

    await captureApi.ignoreItem('capture-2')

    expect(http.post).toHaveBeenCalledWith('/capture/items/capture-2/ignore')
  })

  it('posts cancel action', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: {} })

    await captureApi.cancelItem('capture-2')

    expect(http.post).toHaveBeenCalledWith('/capture/items/capture-2/cancel')
  })

  it('posts triage action and returns enqueue status', async () => {
    vi.mocked(http.post).mockResolvedValue({
      data: {
        id: 'capture-2',
        status: 'Triaging',
        alreadyTriaging: false,
      },
    })

    const result = await captureApi.enqueueTriage('capture-2')

    expect(http.post).toHaveBeenCalledWith('/capture/items/capture-2/triage', undefined)
    expect(result.status).toBe('Triaging')
    expect(result.alreadyTriaging).toBe(false)
  })

  it('posts triage action with a target board when supplied (#1764)', async () => {
    vi.mocked(http.post).mockResolvedValue({
      data: {
        id: 'capture-3',
        status: 'Triaging',
        alreadyTriaging: false,
      },
    })

    await captureApi.enqueueTriage('capture-3', 'board-9')

    expect(http.post).toHaveBeenCalledWith('/capture/items/capture-3/triage', { boardId: 'board-9' })
  })

  it('posts batch triage request', async () => {
    vi.mocked(http.post).mockResolvedValue({
      data: {
        total: 2,
        succeeded: 2,
        failed: 0,
        results: [
          { itemId: 'c1', success: true },
          { itemId: 'c2', success: true },
        ],
      },
    })

    const result = await captureApi.batchTriage([
      { itemId: 'c1', action: 'triage' },
      { itemId: 'c2', action: 'ignore' },
    ])

    expect(http.post).toHaveBeenCalledWith('/capture/items/batch-triage', {
      items: [
        { itemId: 'c1', action: 'triage' },
        { itemId: 'c2', action: 'ignore' },
      ],
    })
    expect(result.total).toBe(2)
    expect(result.succeeded).toBe(2)
  })

  it('puts suggestion update', async () => {
    vi.mocked(http.put).mockResolvedValue({
      data: { id: 'capture-3', rawText: 'updated text' },
    })

    const result = await captureApi.updateSuggestion('capture-3', {
      text: 'updated text',
      titleHint: 'New Title',
    })

    expect(http.put).toHaveBeenCalledWith('/capture/items/capture-3/suggestion', {
      text: 'updated text',
      titleHint: 'New Title',
    })
    expect(result.rawText).toBe('updated text')
  })

  it('puts an explicit metadata replacement for correction or clearing', async () => {
    vi.mocked(http.put).mockResolvedValue({
      data: {
        id: 'capture-3',
        rawText: 'updated text',
        metadata: { dueDate: null, labels: [] },
      },
    })

    const result = await captureApi.updateSuggestion('capture-3', {
      text: 'updated text',
      metadata: {
        dueDate: null,
        labels: [],
      },
    })

    expect(http.put).toHaveBeenCalledWith('/capture/items/capture-3/suggestion', {
      text: 'updated text',
      metadata: {
        dueDate: null,
        labels: [],
      },
    })
    expect(result.metadata).toEqual({ dueDate: null, labels: [] })
  })
})
