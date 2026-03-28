/**
 * Card filtering: filter state, matching logic, and computed views.
 */
import { computed } from 'vue'
import type { Card } from '../../types/board'
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
      const now = new Date()
      const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
      const weekFromNow = new Date(today)
      weekFromNow.setDate(weekFromNow.getDate() + 7)

      switch (state.filters.value.dueDateFilter) {
        case 'overdue':
          if (!card.dueDate || new Date(card.dueDate) >= today) return false
          break
        case 'due-today':
        {
          if (!card.dueDate) return false
          const dueDate = new Date(card.dueDate)
          const dueDateDay = new Date(dueDate.getFullYear(), dueDate.getMonth(), dueDate.getDate())
          if (dueDateDay.getTime() !== today.getTime()) return false
          break
        }
        case 'due-week':
        {
          if (!card.dueDate) return false
          const due = new Date(card.dueDate)
          if (due < today || due > weekFromNow) return false
          break
        }
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
