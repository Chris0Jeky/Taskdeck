/**
 * Card filtering: filter state, matching logic, and computed views.
 */
import { computed } from 'vue'
import type { Card } from '../../types/board'
import { addCalendarDays, localCalendarDateKey, toCalendarDateKey } from '../../utils/dueDates'
import type { BoardState, CardFilters } from './boardState'

export function createCardFilterActions(state: BoardState) {
  // Helper function to check if a card matches current filters
  const cardMatchesFilters = (card: Card): boolean => {
    // Search text filter
    if (state.filters.value.searchText) {
      const searchLower = state.filters.value.searchText.toLowerCase()
      const matchesTitle = card.title.toLowerCase().includes(searchLower)
      const matchesDescription = card.description?.toLowerCase().includes(searchLower)
      if (!matchesTitle && !matchesDescription) return false
    }

    // Label filter
    if (state.filters.value.labelIds.length > 0) {
      const cardLabelIds = card.labels.map((l) => l.id)
      const hasMatchingLabel = state.filters.value.labelIds.some((id) => cardLabelIds.includes(id))
      if (!hasMatchingLabel) return false
    }

    // Due date filter
    if (state.filters.value.dueDateFilter !== 'all') {
      const todayKey = localCalendarDateKey()
      const weekFromNowKey = addCalendarDays(todayKey, 7)
      const dueDateKey = toCalendarDateKey(card.dueDate)

      switch (state.filters.value.dueDateFilter) {
        case 'overdue':
          if (!dueDateKey || dueDateKey >= todayKey) return false
          break
        case 'due-today':
          if (dueDateKey !== todayKey) return false
          break
        case 'due-week':
          if (!dueDateKey || !weekFromNowKey || dueDateKey < todayKey || dueDateKey > weekFromNowKey) return false
          break
        case 'no-date':
          if (card.dueDate) return false
          break
      }
    }

    // Blocked status filter
    if (state.filters.value.showBlockedOnly && !card.isBlocked) {
      return false
    }

    return true
  }

  // Computed
  const cardsByColumn = computed(() => {
    const map = new Map<string, Card[]>()

    // Filter cards first
    const filteredCards = state.currentBoardCards.value.filter(cardMatchesFilters)

    filteredCards.forEach((card) => {
      if (!map.has(card.columnId)) {
        map.set(card.columnId, [])
      }
      map.get(card.columnId)!.push(card)
    })

    // Sort cards by position within each column
    map.forEach((cards) => {
      cards.sort((a, b) => a.position - b.position)
    })

    return map
  })

  const filteredCardCount = computed(() => {
    return state.currentBoardCards.value.filter(cardMatchesFilters).length
  })

  const totalCardCount = computed(() => {
    return state.currentBoardCards.value.length
  })

  // Filter actions
  const updateFilters = (newFilters: CardFilters) => {
    state.filters.value = { ...newFilters }
  }

  const clearFilters = () => {
    state.filters.value = {
      searchText: '',
      labelIds: [],
      dueDateFilter: 'all',
      showBlockedOnly: false,
    }
  }

  return {
    cardsByColumn,
    filteredCardCount,
    totalCardCount,
    updateFilters,
    clearFilters,
  }
}
