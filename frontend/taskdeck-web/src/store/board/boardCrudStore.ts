/**
 * Board CRUD operations: fetch, create, update, delete boards.
 */
import { boardsApi } from '../../api/boardsApi'
import { buildDemoBoardList, buildDemoBoardDetail } from '../../utils/demoData'
import type { CreateBoardDto, UpdateBoardDto } from '../../types/board'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'

export function createBoardCrudActions(state: BoardState, helpers: BoardHelpers) {
  async function fetchBoards(search?: string, includeArchived = false) {
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
      state.boards.value = await boardsApi.getBoards(search, includeArchived)
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

      // Remove from boards list
      state.boards.value = state.boards.value.filter((b) => b.id !== boardId)

      // Clear current board if it's the one being deleted
      if (state.currentBoard.value && state.currentBoard.value.id === boardId) {
        state.currentBoard.value = null
        state.currentBoardCards.value = []
        state.currentBoardLabels.value = []
        state.cardCommentsByCardId.value = {}
        state.boardPresenceMembers.value = []
        state.editingCardId.value = null
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
