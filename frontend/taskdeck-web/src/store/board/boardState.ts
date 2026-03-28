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

export function createBoardState() {
  const boards = ref<Board[]>([])
  const currentBoard = ref<BoardDetail | null>(null)
  const currentBoardCards = ref<Card[]>([])
  const currentBoardLabels = ref<Label[]>([])
  const cardCommentsByCardId = ref<Record<string, CardComment[]>>({})
  const boardPresenceMembers = ref<BoardPresenceMember[]>([])
  const editingCardId = ref<string | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const filters = ref<CardFilters>({
    searchText: '',
    labelIds: [],
    dueDateFilter: 'all',
    showBlockedOnly: false,
  })

  return {
    boards,
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
