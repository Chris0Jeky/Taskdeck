import { ref, watch } from 'vue'
import { searchApi } from '../api/searchApi'
import type { SearchBoardHit, SearchCardHit } from '../api/searchApi'

export interface GlobalSearchState {
  query: string
  boards: SearchBoardHit[]
  cards: SearchCardHit[]
  loading: boolean
  error: string | null
  totalCardCount: number
  hasMoreCards: boolean
  loadingMore: boolean
}

export function useGlobalSearch(debounceMs = 250) {
  const query = ref('')
  const boards = ref<SearchBoardHit[]>([])
  const cards = ref<SearchCardHit[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const totalCardCount = ref(0)
  const hasMoreCards = ref(false)
  const loadingMore = ref(false)
  const currentOffset = ref(0)
  const pageSize = 20

  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  let abortController: AbortController | null = null

  function reset() {
    query.value = ''
    boards.value = []
    cards.value = []
    loading.value = false
    error.value = null
    totalCardCount.value = 0
    hasMoreCards.value = false
    loadingMore.value = false
    currentOffset.value = 0
    if (debounceTimer) {
      clearTimeout(debounceTimer)
      debounceTimer = null
    }
    if (abortController) {
      abortController.abort()
      abortController = null
    }
  }

  async function executeSearch(searchQuery: string) {
    if (searchQuery.trim().length < 2) {
      boards.value = []
      cards.value = []
      loading.value = false
      error.value = null
      totalCardCount.value = 0
      hasMoreCards.value = false
      currentOffset.value = 0
      return
    }

    // Cancel any in-flight request
    if (abortController) {
      abortController.abort()
    }
    abortController = new AbortController()

    loading.value = true
    error.value = null
    currentOffset.value = 0

    try {
      const result = await searchApi.search(searchQuery.trim(), abortController.signal, {
        maxResults: pageSize,
        offset: 0,
      })
      boards.value = result.boards
      cards.value = result.cards
      totalCardCount.value = result.totalCardCount
      hasMoreCards.value = result.hasMoreCards
      currentOffset.value = result.cards.length
    } catch (err: unknown) {
      if (err instanceof DOMException && err.name === 'AbortError') {
        return
      }
      error.value = 'Search failed. Please try again.'
      boards.value = []
      cards.value = []
      totalCardCount.value = 0
      hasMoreCards.value = false
    } finally {
      loading.value = false
    }
  }

  async function loadMore() {
    if (!hasMoreCards.value || loadingMore.value) return

    const searchQuery = query.value.trim()
    if (searchQuery.length < 2) return

    // Cancel any in-flight request before starting load-more
    if (abortController) {
      abortController.abort()
    }
    abortController = new AbortController()

    loadingMore.value = true

    try {
      const result = await searchApi.search(searchQuery, abortController.signal, {
        maxResults: pageSize,
        offset: currentOffset.value,
      })
      cards.value = [...cards.value, ...result.cards]
      totalCardCount.value = result.totalCardCount
      hasMoreCards.value = result.hasMoreCards
      currentOffset.value = currentOffset.value + result.cards.length
    } catch (err: unknown) {
      if (err instanceof DOMException && err.name === 'AbortError') {
        return
      }
      error.value = 'Failed to load more results.'
    } finally {
      loadingMore.value = false
    }
  }

  watch(query, (newQuery) => {
    if (debounceTimer) {
      clearTimeout(debounceTimer)
    }

    if (newQuery.trim().length < 2) {
      boards.value = []
      cards.value = []
      loading.value = false
      error.value = null
      totalCardCount.value = 0
      hasMoreCards.value = false
      currentOffset.value = 0
      return
    }

    loading.value = true
    debounceTimer = setTimeout(() => {
      void executeSearch(newQuery)
    }, debounceMs)
  })

  return {
    query,
    boards,
    cards,
    loading,
    error,
    totalCardCount,
    hasMoreCards,
    loadingMore,
    reset,
    executeSearch,
    loadMore,
  }
}
