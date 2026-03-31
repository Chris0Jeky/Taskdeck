import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { searchApi } from '../../api/searchApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
  },
}))

const mockResult = {
  boards: [],
  cards: [],
  totalCardCount: 0,
  hasMoreCards: false,
  offset: 0,
  maxResults: 20,
}

describe('searchApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(http.get).mockResolvedValue({ data: mockResult })
  })

  it('searches with query only', async () => {
    await searchApi.search('test')

    expect(http.get).toHaveBeenCalledWith('/search?q=test', { signal: undefined })
  })

  it('passes abort signal', async () => {
    const controller = new AbortController()

    await searchApi.search('test', controller.signal)

    expect(http.get).toHaveBeenCalledWith('/search?q=test', { signal: controller.signal })
  })

  it('appends maxResults when provided', async () => {
    await searchApi.search('test', undefined, { maxResults: 50 })

    expect(http.get).toHaveBeenCalledWith('/search?q=test&maxResults=50', { signal: undefined })
  })

  it('appends offset when provided', async () => {
    await searchApi.search('test', undefined, { offset: 20 })

    expect(http.get).toHaveBeenCalledWith('/search?q=test&offset=20', { signal: undefined })
  })

  it('appends both maxResults and offset when provided', async () => {
    await searchApi.search('test', undefined, { maxResults: 10, offset: 30 })

    expect(http.get).toHaveBeenCalledWith('/search?q=test&maxResults=10&offset=30', { signal: undefined })
  })

  it('omits maxResults and offset when options is empty', async () => {
    await searchApi.search('test', undefined, {})

    expect(http.get).toHaveBeenCalledWith('/search?q=test', { signal: undefined })
  })
})
