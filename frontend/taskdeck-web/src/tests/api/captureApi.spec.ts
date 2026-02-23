import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { captureApi } from '../../api/captureApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
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
})
