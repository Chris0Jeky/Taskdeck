/**
 * Shared reactive state for the board store modules.
 *
 * All board-related sub-stores read and write these refs so that
 * state stays consistent across module boundaries.  The refs are
 * created lazily via `useBoardState()` which must be called inside
 * a Pinia store setup function (i.e. after `setActivePinia`).
 */
import { ref } from 'vue'
import type { Board, BoardDetail, Card, Label } from '../../types/board'
import type { CardComment } from '../../types/comments'
import type { BoardPresenceMember } from '../../types/realtime'

export interface CardFilters {
  searchText: string
  labelIds: string[]
  dueDateFilter: 'all' | 'overdue' | 'due-today' | 'due-week' | 'no-date'
  showBlockedOnly: boolean
}

/**
 * The card-filter defaults: nothing searched, no labels selected, no due-date
 * narrowing, blocked cards not isolated.
 *
 * The single source for all three sites that reset to this value — the initial
 * state below, `cardFilterStore.clearFilters` and `boardCrudStore.resetForLogout`
 * — which previously each carried the literal.  A function rather than a shared
 * constant because every site needs its own object: `labelIds` is an array, so
 * one exported instance would let a filter change in one board reach the value
 * the next reset restores.
 */
export function initialCardFilters(): CardFilters {
  return {
    searchText: '',
    labelIds: [],
    dueDateFilter: 'all',
    showBlockedOnly: false,
  }
}

export function createBoardState() {
  const boards = ref<Board[]>([])
  const activeBoardId = ref<string | null>(null)
  const currentBoard = ref<BoardDetail | null>(null)
  const currentBoardCards = ref<Card[]>([])
  const currentBoardLabels = ref<Label[]>([])
  const cardCommentsByCardId = ref<Record<string, CardComment[]>>({})
  const boardPresenceMembers = ref<BoardPresenceMember[]>([])
  const editingCardId = ref<string | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const filters = ref<CardFilters>(initialCardFilters())

  return {
    boards,
    activeBoardId,
    currentBoard,
    currentBoardCards,
    currentBoardLabels,
    cardCommentsByCardId,
    boardPresenceMembers,
    editingCardId,
    loading,
    error,
    filters,
  }
}

export type BoardState = ReturnType<typeof createBoardState>
