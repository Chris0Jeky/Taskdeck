import { defineStore } from 'pinia'
import {
  createBoardState,
  createBoardHelpers,
  createBoardCrudActions,
  createColumnActions,
  createCardActions,
  createCardCommentActions,
  createLabelActions,
  createCardFilterActions,
  createBoardUiActions,
} from './board'

// Re-export the CardFilters type so existing consumers keep working
export type { CardFilters } from './board'

export const useBoardStore = defineStore('board', () => {
  // Shared state
  const state = createBoardState()

  // Shared helpers (toast, error handling, demo guard, etc.)
  const helpers = createBoardHelpers(state)

  // Domain action groups
  const boardCrud = createBoardCrudActions(state, helpers)
  const columns = createColumnActions(state, helpers)
  const cards = createCardActions(state, helpers)
  const comments = createCardCommentActions(state, helpers)
  const labels = createLabelActions(state, helpers)
  const filtering = createCardFilterActions(state)
  const ui = createBoardUiActions(state)

  // Wire fetchBoard to depend on card/label fetch
  async function fetchBoard(id: string) {
    return boardCrud.fetchBoard(id, cards.fetchCards, labels.fetchLabels)
  }

  return {
    // State
    boards: state.boards,
    currentBoard: state.currentBoard,
    currentBoardCards: state.currentBoardCards,
    currentBoardLabels: state.currentBoardLabels,
    cardCommentsByCardId: state.cardCommentsByCardId,
    boardPresenceMembers: state.boardPresenceMembers,
    editingCardId: state.editingCardId,
    loading: state.loading,
    error: state.error,
    filters: state.filters,

    // Computed
    cardsByColumn: filtering.cardsByColumn,
    filteredCardCount: filtering.filteredCardCount,
    totalCardCount: filtering.totalCardCount,

    // Actions — board CRUD
    fetchBoards: boardCrud.fetchBoards,
    fetchBoard,
    createBoard: boardCrud.createBoard,
    updateBoard: boardCrud.updateBoard,
    deleteBoard: boardCrud.deleteBoard,

    // Actions — columns
    createColumn: columns.createColumn,
    updateColumn: columns.updateColumn,
    deleteColumn: columns.deleteColumn,
    reorderColumns: columns.reorderColumns,

    // Actions — cards
    createCard: cards.createCard,
    updateCard: cards.updateCard,
    deleteCard: cards.deleteCard,
    fetchCards: cards.fetchCards,
    moveCard: cards.moveCard,
    fetchCardProvenance: cards.fetchCardProvenance,

    // Actions — labels
    createLabel: labels.createLabel,
    updateLabel: labels.updateLabel,
    deleteLabel: labels.deleteLabel,
    fetchLabels: labels.fetchLabels,

    // Actions — filters
    updateFilters: filtering.updateFilters,
    clearFilters: filtering.clearFilters,

    // Actions — UI state
    setBoardPresenceMembers: ui.setBoardPresenceMembers,
    setEditingCard: ui.setEditingCard,

    // Actions — comments
    getCardComments: comments.getCardComments,
    fetchCardComments: comments.fetchCardComments,
    createCardComment: comments.createCardComment,
    updateCardComment: comments.updateCardComment,
    deleteCardComment: comments.deleteCardComment,
  }
})
