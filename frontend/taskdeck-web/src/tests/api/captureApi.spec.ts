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

  it('loads capture item detail by id', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: { id: 'capture-42' } })

    await captureApi.getItem('capture-42')

    expect(http.get).toHaveBeenCalledWith('/capture/items/capture-42')
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

    expect(http.post).toHaveBeenCalledWith('/capture/items/capture-2/triage')
    expect(result.status).toBe('Triaging')
    expect(result.alreadyTriaging).toBe(false)
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
})
