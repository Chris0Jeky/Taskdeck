import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { nextTick } from 'vue'
import { useGlobalSearch } from '../../composables/useGlobalSearch'

const mockSearchResult = {
  boards: [
    { id: 'b1', name: 'My Board', description: 'A test board', isArchived: false },
  ],
  cards: [
    {
      id: 'c1',
      boardId: 'b1',
      boardName: 'My Board',
      columnId: 'col1',
      columnName: 'To Do',
      title: 'Test Card',
      description: 'Some description',
    },
  ],
  totalCardCount: 1,
  hasMoreCards: false,
  offset: 0,
  maxResults: 20,
}

const mockSearchResultWithMore = {
  boards: [],
  cards: [
    {
      id: 'c1',
      boardId: 'b1',
      boardName: 'My Board',
      columnId: 'col1',
      columnName: 'To Do',
      title: 'Test Card',
      description: 'Some description',
    },
  ],
  totalCardCount: 25,
  hasMoreCards: true,
  offset: 0,
  maxResults: 20,
}

const mockLoadMoreResult = {
  boards: [],
  cards: [
    {
      id: 'c2',
      boardId: 'b1',
      boardName: 'My Board',
      columnId: 'col1',
      columnName: 'To Do',
      title: 'Another Card',
      description: 'More description',
    },
  ],
  totalCardCount: 25,
  hasMoreCards: false,
  offset: 1,
  maxResults: 20,
}

vi.mock('../../api/searchApi', () => ({
  searchApi: {
    search: vi.fn(),
  },
}))

// Import after mock so we get the mocked version
import { searchApi } from '../../api/searchApi'
const mockSearch = vi.mocked(searchApi.search)

describe('useGlobalSearch', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    mockSearch.mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('starts with empty state', () => {
    const { query, boards, cards, loading, error, totalCardCount, hasMoreCards, loadingMore } = useGlobalSearch()
    expect(query.value).toBe('')
    expect(boards.value).toEqual([])
    expect(cards.value).toEqual([])
    expect(loading.value).toBe(false)
    expect(error.value).toBeNull()
    expect(totalCardCount.value).toBe(0)
    expect(hasMoreCards.value).toBe(false)
    expect(loadingMore.value).toBe(false)
  })

  it('does not search when query is shorter than 2 characters', async () => {
    const { query, boards, cards } = useGlobalSearch(100)
    query.value = 'a'
    await nextTick()
    vi.advanceTimersByTime(200)
    await nextTick()
    expect(mockSearch).not.toHaveBeenCalled()
    expect(boards.value).toEqual([])
    expect(cards.value).toEqual([])
  })

  it('debounces and executes search for valid queries', async () => {
    mockSearch.mockResolvedValue(mockSearchResult)
    const { query, boards, cards } = useGlobalSearch(100)

    query.value = 'test'
    await nextTick()

    // Should not have fired yet
    expect(mockSearch).not.toHaveBeenCalled()

    // Advance past debounce timer
    vi.advanceTimersByTime(150)
    await nextTick()
    // Wait for the async resolution
    await vi.runAllTimersAsync()
    await nextTick()

    expect(mockSearch).toHaveBeenCalledWith('test', expect.any(AbortSignal), {
      maxResults: 20,
      offset: 0,
    })
    expect(boards.value).toEqual(mockSearchResult.boards)
    expect(cards.value).toEqual(mockSearchResult.cards)
  })

  it('sets loading to true while search is pending', async () => {
    let resolveSearch: (val: typeof mockSearchResult) => void
    mockSearch.mockReturnValue(
      new Promise((resolve) => { resolveSearch = resolve })
    )

    const { query, loading } = useGlobalSearch(50)
    query.value = 'test'
    await nextTick()

    // Loading should be set immediately (before debounce fires)
    expect(loading.value).toBe(true)

    vi.advanceTimersByTime(100)
    await nextTick()

    // Still loading while awaiting response
    expect(loading.value).toBe(true)

    // Resolve the search
    resolveSearch!(mockSearchResult)
    await nextTick()
    await vi.runAllTimersAsync()
    await nextTick()

    expect(loading.value).toBe(false)
  })

  it('handles search errors gracefully', async () => {
    mockSearch.mockRejectedValue(new Error('Network error'))
    const { query, error, boards, cards } = useGlobalSearch(50)

    query.value = 'failing'
    await nextTick()
    vi.advanceTimersByTime(100)
    await nextTick()
    await vi.runAllTimersAsync()
    await nextTick()

    expect(error.value).toBe('Search failed. Please try again.')
    expect(boards.value).toEqual([])
    expect(cards.value).toEqual([])
  })

  it('resets all state on reset()', async () => {
    mockSearch.mockResolvedValue(mockSearchResult)
    const { query, boards, cards, loading, error, totalCardCount, hasMoreCards, loadingMore, reset } = useGlobalSearch(50)

    query.value = 'test'
    await nextTick()
    vi.advanceTimersByTime(100)
    await vi.runAllTimersAsync()
    await nextTick()

    expect(boards.value.length).toBeGreaterThan(0)

    reset()

    expect(query.value).toBe('')
    expect(boards.value).toEqual([])
    expect(cards.value).toEqual([])
    expect(loading.value).toBe(false)
    expect(error.value).toBeNull()
    expect(totalCardCount.value).toBe(0)
    expect(hasMoreCards.value).toBe(false)
    expect(loadingMore.value).toBe(false)
  })

  it('clears results when query is cleared', async () => {
    mockSearch.mockResolvedValue(mockSearchResult)
    const { query, boards, cards } = useGlobalSearch(50)

    query.value = 'test'
    await nextTick()
    vi.advanceTimersByTime(100)
    await vi.runAllTimersAsync()
    await nextTick()

    expect(boards.value.length).toBeGreaterThan(0)

    query.value = ''
    await nextTick()

    expect(boards.value).toEqual([])
    expect(cards.value).toEqual([])
  })

  it('exposes pagination metadata from search response', async () => {
    mockSearch.mockResolvedValue(mockSearchResultWithMore)
    const { query, totalCardCount, hasMoreCards } = useGlobalSearch(50)

    query.value = 'test'
    await nextTick()
    vi.advanceTimersByTime(100)
    await vi.runAllTimersAsync()
    await nextTick()

    expect(totalCardCount.value).toBe(25)
    expect(hasMoreCards.value).toBe(true)
  })

  it('loadMore appends cards and updates pagination state', async () => {
    mockSearch.mockResolvedValueOnce(mockSearchResultWithMore)
    const { query, cards, hasMoreCards, loadMore } = useGlobalSearch(50)

    query.value = 'test'
    await nextTick()
    vi.advanceTimersByTime(100)
    await vi.runAllTimersAsync()
    await nextTick()

    expect(cards.value).toHaveLength(1)
    expect(hasMoreCards.value).toBe(true)

    mockSearch.mockResolvedValueOnce(mockLoadMoreResult)
    await loadMore()

    expect(cards.value).toHaveLength(2)
    expect(cards.value[1].id).toBe('c2')
    expect(hasMoreCards.value).toBe(false)
  })

  it('loadMore does nothing when hasMoreCards is false', async () => {
    mockSearch.mockResolvedValue(mockSearchResult)
    const { query, loadMore } = useGlobalSearch(50)

    query.value = 'test'
    await nextTick()
    vi.advanceTimersByTime(100)
    await vi.runAllTimersAsync()
    await nextTick()

    const callCount = mockSearch.mock.calls.length
    await loadMore()

    // No additional call should have been made
    expect(mockSearch.mock.calls.length).toBe(callCount)
  })

  it('loadMore passes correct offset', async () => {
    mockSearch.mockResolvedValueOnce(mockSearchResultWithMore)
    const { query, loadMore } = useGlobalSearch(50)

    query.value = 'test'
    await nextTick()
    vi.advanceTimersByTime(100)
    await vi.runAllTimersAsync()
    await nextTick()

    mockSearch.mockResolvedValueOnce(mockLoadMoreResult)
    await loadMore()

    // Second call should have offset = 1 (number of cards from first result)
    const secondCall = mockSearch.mock.calls[1]
    expect(secondCall[2]).toEqual(expect.objectContaining({ offset: 1 }))
  })
})
