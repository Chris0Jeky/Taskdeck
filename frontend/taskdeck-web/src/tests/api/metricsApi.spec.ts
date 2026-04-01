import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { metricsApi } from '../../api/metricsApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('metricsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('getBoardMetrics sends correct URL with boardId', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: { boardId: 'b1' } })

    await metricsApi.getBoardMetrics({ boardId: 'b1' })

    expect(http.get).toHaveBeenCalledWith('/metrics/boards/b1')
  })

  it('getBoardMetrics includes from and to query params', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: { boardId: 'b1' } })

    await metricsApi.getBoardMetrics({
      boardId: 'b1',
      from: '2026-01-01T00:00:00Z',
      to: '2026-01-31T23:59:59Z',
    })

    expect(http.get).toHaveBeenCalledWith(
      '/metrics/boards/b1?from=2026-01-01T00%3A00%3A00Z&to=2026-01-31T23%3A59%3A59Z',
    )
  })

  it('getBoardMetrics includes labelId when provided', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: { boardId: 'b1' } })

    await metricsApi.getBoardMetrics({
      boardId: 'b1',
      labelId: 'label-123',
    })

    expect(http.get).toHaveBeenCalledWith(
      '/metrics/boards/b1?labelId=label-123',
    )
  })

  it('getBoardMetrics encodes special characters in boardId', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: { boardId: 'b/1' } })

    await metricsApi.getBoardMetrics({ boardId: 'b/1' })

    expect(http.get).toHaveBeenCalledWith('/metrics/boards/b%2F1')
  })
})
