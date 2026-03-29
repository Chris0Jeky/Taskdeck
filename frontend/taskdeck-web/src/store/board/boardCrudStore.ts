/**
 * Board CRUD operations: fetch, create, update, delete boards.
 */
import { boardsApi } from '../../api/boardsApi'
import { buildDemoBoardList, buildDemoBoardDetail } from '../../utils/demoData'
import type { CreateBoardDto, UpdateBoardDto } from '../../types/board'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'

// Minimum gap between board-list fetches.  Multiple views (BoardsListView,
// ActivityView, ReviewView, etc.) can call fetchBoards on mount in quick
// succession; the throttle guard prevents duplicate network round-trips.
const FETCH_BOARDS_THROTTLE_MS = 5000

export function createBoardCrudActions(state: BoardState, helpers: BoardHelpers) {
  let lastFetchBoardsAt = 0

  async function fetchBoards(search?: string, includeArchived = false) {
    const now = Date.now()
    // Allow forced refreshes (search/archive filter changes) to bypass throttle.
    const isFilteredRequest = !!search || includeArchived
    if (!isFilteredRequest && now - lastFetchBoardsAt < FETCH_BOARDS_THROTTLE_MS) {
      return
    }
    if (helpers.isDemoMode) {
      state.loading.value = true
      state.error.value = null
      state.boards.value = buildDemoBoardList()
      state.loading.value = false
      return
    }

    try {
      state.loading.value = true
      state.error.value = null
      const freshBoards = await boardsApi.getBoards(search, includeArchived)
      lastFetchBoardsAt = Date.now()
      state.boards.value = freshBoards

      // Preserve selection guard: only update activeBoardId if there is no
      // current selection or the previously-selected board is no longer in the
      // refreshed list (e.g. it was deleted). This prevents polling/subscription
      // refreshes from resetting the user's active board to the first item.
      const currentId = state.activeBoardId.value
      const stillExists = currentId !== null && freshBoards.some((b) => b.id === currentId)
      if (!stillExists) {
        state.activeBoardId.value = freshBoards[0]?.id ?? null
      }
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to fetch boards')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function fetchBoard(
    id: string,
    fetchCards: (boardId: string) => Promise<void>,
    fetchLabels: (boardId: string) => Promise<void>,
  ) {
    if (helpers.isDemoMode) {
      state.loading.value = true
      state.error.value = null
      const demo = buildDemoBoardDetail(id)
      state.currentBoard.value = demo.board
      state.currentBoardCards.value = demo.cards
      state.currentBoardLabels.value = []
      state.cardCommentsByCardId.value = {}
      state.loading.value = false
      return
    }

    try {
      state.loading.value = true
      state.error.value = null
      state.currentBoard.value = await boardsApi.getBoard(id)
      state.cardCommentsByCardId.value = {}

      // Fetch cards and labels for the board
      await Promise.all([fetchCards(id), fetchLabels(id)])
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to fetch board')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function createBoard(board: CreateBoardDto) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const newBoard = await boardsApi.createBoard(board)
      state.boards.value.push(newBoard)
      helpers.toast.success(`Board "${newBoard.name}" created successfully`)
      return newBoard
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to create board')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function updateBoard(boardId: string, board: UpdateBoardDto) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const updatedBoard = await boardsApi.updateBoard(boardId, board)

      // Update in boards list
      const index = state.boards.value.findIndex((b) => b.id === boardId)
      if (index !== -1) {
        state.boards.value[index] = updatedBoard
      }

      // Update current board if it's the one being edited
      if (state.currentBoard.value?.id === boardId) {
        state.currentBoard.value = { ...state.currentBoard.value, ...updatedBoard }
      }

      helpers.toast.success('Board updated successfully')
      return updatedBoard
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to update board')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function deleteBoard(boardId: string) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      await boardsApi.deleteBoard(boardId)

      // Clear current board state in a single assignment to avoid cascading
      // reactive updates.  Previously, each `.value = …` triggered its own
      // flush cycle, which caused watchers/computed properties in mounted
      // views (BoardView, BoardCanvas, etc.) to re-evaluate on every
      // intermediate state — the root cause of the ~30 s freeze in #519.
      const isCurrent = state.currentBoard.value?.id === boardId
      if (isCurrent) {
        state.currentBoard.value = null
        state.currentBoardCards.value = []
        state.currentBoardLabels.value = []
        state.cardCommentsByCardId.value = {}
        state.boardPresenceMembers.value = []
        state.editingCardId.value = null
      }

      // Remove from boards list after clearing detail state so downstream
      // watchers on `boards` do not attempt to read stale detail refs.
      state.boards.value = state.boards.value.filter((b) => b.id !== boardId)

      // Clear activeBoardId if the deleted board was the active selection
      if (state.activeBoardId.value === boardId) {
        state.activeBoardId.value = state.boards.value[0]?.id ?? null
      }

      helpers.toast.success('Board archived successfully')
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to archive board')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  return {
    fetchBoards,
    fetchBoard,
    createBoard,
    updateBoard,
    deleteBoard,
  }
}
