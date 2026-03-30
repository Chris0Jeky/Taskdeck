import { ref, watch } from 'vue'
import { searchApi } from '../api/searchApi'
import type { SearchBoardHit, SearchCardHit } from '../api/searchApi'

export interface GlobalSearchState {
  query: string
  boards: SearchBoardHit[]
  cards: SearchCardHit[]
  loading: boolean
  error: string | null
}

export function useGlobalSearch(debounceMs = 250) {
  const query = ref('')
  const boards = ref<SearchBoardHit[]>([])
  const cards = ref<SearchCardHit[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  let abortController: AbortController | null = null

  function reset() {
    query.value = ''
    boards.value = []
    cards.value = []
    loading.value = false
    error.value = null
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
      return
    }

    // Cancel any in-flight request
    if (abortController) {
      abortController.abort()
    }
    abortController = new AbortController()

    loading.value = true
    error.value = null

    try {
      const result = await searchApi.search(searchQuery.trim(), abortController.signal)
      boards.value = result.boards
      cards.value = result.cards
    } catch (err: unknown) {
      if (err instanceof DOMException && err.name === 'AbortError') {
        return
      }
      error.value = 'Search failed. Please try again.'
      boards.value = []
      cards.value = []
    } finally {
      loading.value = false
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
    reset,
    executeSearch,
  }
}
