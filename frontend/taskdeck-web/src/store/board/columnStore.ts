/**
 * Column operations: create, update, delete, reorder columns.
 */
import { columnsApi } from '../../api/columnsApi'
import type { CreateColumnDto, UpdateColumnDto } from '../../types/board'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'

export function createColumnActions(state: BoardState, helpers: BoardHelpers) {
  async function createColumn(boardId: string, column: CreateColumnDto) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const newColumn = await columnsApi.createColumn(boardId, column)

      if (state.currentBoard.value && state.currentBoard.value.id === boardId) {
        state.currentBoard.value.columns.push(newColumn)
      }

      helpers.toast.success(`Column "${newColumn.name}" created successfully`)
      return newColumn
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to create column')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function updateColumn(boardId: string, columnId: string, column: UpdateColumnDto) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const updatedColumn = await columnsApi.updateColumn(boardId, columnId, column)

      // Update column in current board
      if (state.currentBoard.value && state.currentBoard.value.id === boardId) {
        const index = state.currentBoard.value.columns.findIndex((c) => c.id === columnId)
        if (index !== -1) {
          state.currentBoard.value.columns[index] = updatedColumn
        }
      }

      helpers.toast.success('Column updated successfully')
      return updatedColumn
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to update column')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function deleteColumn(boardId: string, columnId: string) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      await columnsApi.deleteColumn(boardId, columnId)

      // Remove column from current board
      if (state.currentBoard.value && state.currentBoard.value.id === boardId) {
        state.currentBoard.value.columns = state.currentBoard.value.columns.filter(
          (c) => c.id !== columnId,
        )
      }

      // Remove cards from deleted column
      state.currentBoardCards.value = state.currentBoardCards.value.filter(
        (card) => card.columnId !== columnId,
      )

      helpers.toast.success('Column deleted successfully')
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to delete column')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function reorderColumns(boardId: string, columnIds: string[]) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const reorderedColumns = await columnsApi.reorderColumns(boardId, columnIds)

      // Update columns in current board with reordered list
      if (state.currentBoard.value && state.currentBoard.value.id === boardId) {
        state.currentBoard.value.columns = reorderedColumns
      }

      helpers.toast.success('Columns reordered successfully')
      return reorderedColumns
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to reorder columns')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  return {
    createColumn,
    updateColumn,
    deleteColumn,
    reorderColumns,
  }
}
