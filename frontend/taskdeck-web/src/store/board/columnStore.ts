/**
 * Column operations: create, update, delete, reorder columns.
 */
import { watch } from 'vue'
import { columnsApi } from '../../api/columnsApi'
import type { CreateColumnDto, UpdateColumnDto } from '../../types/board'
import type { BoardState, BoardViewVisit } from './boardState'
import { beginBoardLoading, type BoardHelpers } from './boardStoreHelpers'

interface ColumnMutationVisit {
  boardId: string
  ownerBoardId: string | null
  generation: number
  viewVisit: BoardViewVisit | undefined
  sessionGeneration: number
}

class StaleBoardVisitError extends Error {
  constructor() {
    super('The board visit that queued this column change has ended.')
    this.name = 'StaleBoardVisitError'
  }
}

export function createColumnActions(state: BoardState, helpers: BoardHelpers) {
  // Every column write can affect the board's ordered column set. The API has
  // no revision precondition, so serialize one lane per board: server commit
  // order then follows user-intent order while unrelated boards remain free to
  // progress independently. The visit generation prevents a queued pre-logout
  // intent from starting transport with a later session's credentials.
  const mutationTailByBoardId = new Map<string, Promise<void>>()
  const mutationVersionByBoardId = new Map<string, number>()
  let boardVisitGeneration = 0

  watch(
    () => state.currentBoard.value?.id ?? null,
    (nextBoardId, previousBoardId) => {
      if (nextBoardId !== previousBoardId) boardVisitGeneration++
    },
    { flush: 'sync' },
  )

  function captureColumnVisit(boardId: string): ColumnMutationVisit {
    return {
      boardId,
      ownerBoardId: state.currentBoard.value?.id ?? null,
      generation: boardVisitGeneration,
      viewVisit: state.boardViewVisit.value,
      sessionGeneration: state.boardMutationSessionGeneration.value,
    }
  }

  function isCurrentVisit(visit: ColumnMutationVisit) {
    return (
      (state.currentBoard.value?.id ?? null) === visit.ownerBoardId &&
      boardVisitGeneration === visit.generation &&
      state.boardMutationSessionGeneration.value === visit.sessionGeneration &&
      state.boardViewVisit.value === visit.viewVisit &&
      (visit.viewVisit === undefined || visit.viewVisit.boardId === visit.boardId)
    )
  }

  function ownsTargetBoard(visit: ColumnMutationVisit) {
    return isCurrentVisit(visit) && state.currentBoard.value?.id === visit.boardId
  }

  function currentMutationVersion(boardId: string) {
    return mutationVersionByBoardId.get(boardId) ?? 0
  }

  function markColumnMutation(visit: ColumnMutationVisit) {
    if (state.boardMutationSessionGeneration.value !== visit.sessionGeneration) return
    helpers.markBoardDetailMutation(visit.boardId)
    mutationVersionByBoardId.set(visit.boardId, currentMutationVersion(visit.boardId) + 1)
  }

  async function runColumnMutation<T>(
    visit: ColumnMutationVisit,
    mutation: () => Promise<T>,
  ): Promise<T> {
    const finishLoading = beginBoardLoading(state)
    try {
      const previous = mutationTailByBoardId.get(visit.boardId)
      let operation: Promise<T>

      if (previous) {
        operation = previous.catch(() => undefined).then(() => {
          if (!isCurrentVisit(visit)) throw new StaleBoardVisitError()
          return mutation()
        })
      } else {
        // The first intent starts transport in the initiating call stack. Only a
        // later intent is queued and therefore needs a pre-transport session gate.
        if (!isCurrentVisit(visit)) throw new StaleBoardVisitError()
        operation = mutation()
      }

      const tail = operation.then(
        () => undefined,
        () => undefined,
      )
      mutationTailByBoardId.set(visit.boardId, tail)

      try {
        return await operation
      } finally {
        if (mutationTailByBoardId.get(visit.boardId) === tail) {
          mutationTailByBoardId.delete(visit.boardId)
        }
      }
    } finally {
      finishLoading()
    }
  }

  async function reconcileReopenedBoard(previousVisit: ColumnMutationVisit, deletedColumnId?: string) {
    const { boardId } = previousVisit
    if (
      state.boardMutationSessionGeneration.value !== previousVisit.sessionGeneration ||
      state.currentBoard.value?.id !== boardId
    ) return

    const visit = captureColumnVisit(boardId)
    if (!ownsTargetBoard(visit)) return
    const mutationVersion = currentMutationVersion(boardId)
    try {
      const columns = await columnsApi.getColumns(boardId)
      if (
        ownsTargetBoard(visit) &&
        currentMutationVersion(boardId) === mutationVersion
      ) {
        state.currentBoard.value!.columns = columns
        if (deletedColumnId) {
          // Apply the cascade under the same guard as the refreshed columns;
          // another route can take ownership at the next await boundary.
          state.currentBoardCards.value = state.currentBoardCards.value.filter(
            card => card.columnId !== deletedColumnId,
          )
        }
      }
    } catch {
      if (ownsTargetBoard(visit)) {
        helpers.toast.warning(
          'Column change saved, but columns could not be refreshed. Refresh the board before editing again.',
        )
      }
    }
  }

  async function createColumn(boardId: string, column: CreateColumnDto) {
    helpers.guardDemoMutation()
    const visit = captureColumnVisit(boardId)
    return runColumnMutation(visit, async () => {
      try {
        state.error.value = null
        const newColumn = await columnsApi.createColumn(boardId, column)
        markColumnMutation(visit)

        if (ownsTargetBoard(visit)) {
          // A same-board detail refresh can install the new column before this
          // request resolves. Preserve that fresher object when the stable id exists.
          const currentColumns = state.currentBoard.value!.columns
          if (!currentColumns.some(existingColumn => existingColumn.id === newColumn.id)) {
            currentColumns.push(newColumn)
          }
        } else if (!isCurrentVisit(visit) && state.currentBoard.value?.id === boardId) {
          await reconcileReopenedBoard(visit)
        }

        if (isCurrentVisit(visit)) {
          helpers.toast.success(`Column "${newColumn.name}" created successfully`)
        }
        return newColumn
      } catch (e: unknown) {
        if (isCurrentVisit(visit)) {
          helpers.handleApiError(e, 'Failed to create column')
        }
        throw e
      }
    })
  }

  async function updateColumn(boardId: string, columnId: string, column: UpdateColumnDto) {
    helpers.guardDemoMutation()
    const visit = captureColumnVisit(boardId)
    return runColumnMutation(visit, async () => {
      try {
        state.error.value = null
        const updatedColumn = await columnsApi.updateColumn(boardId, columnId, column)
        markColumnMutation(visit)

        if (ownsTargetBoard(visit)) {
          const currentColumns = state.currentBoard.value!.columns
          const index = currentColumns.findIndex(candidate => candidate.id === columnId)
          if (index !== -1) currentColumns[index] = updatedColumn
        } else if (!isCurrentVisit(visit) && state.currentBoard.value?.id === boardId) {
          await reconcileReopenedBoard(visit)
        }

        if (isCurrentVisit(visit)) helpers.toast.success('Column updated successfully')
        return updatedColumn
      } catch (e: unknown) {
        if (isCurrentVisit(visit)) {
          helpers.handleApiError(e, 'Failed to update column')
        }
        throw e
      }
    })
  }

  async function deleteColumn(boardId: string, columnId: string) {
    helpers.guardDemoMutation()
    const visit = captureColumnVisit(boardId)
    return runColumnMutation(visit, async () => {
      try {
        state.error.value = null
        await columnsApi.deleteColumn(boardId, columnId)
        markColumnMutation(visit)

        if (ownsTargetBoard(visit)) {
          state.currentBoard.value!.columns = state.currentBoard.value!.columns.filter(
            candidate => candidate.id !== columnId,
          )
          state.currentBoardCards.value = state.currentBoardCards.value.filter(
            card => card.columnId !== columnId,
          )
        } else if (!isCurrentVisit(visit) && state.currentBoard.value?.id === boardId) {
          await reconcileReopenedBoard(visit, columnId)
        }

        if (isCurrentVisit(visit)) helpers.toast.success('Column deleted successfully')
      } catch (e: unknown) {
        if (isCurrentVisit(visit)) {
          helpers.handleApiError(e, 'Failed to delete column')
        }
        throw e
      }
    })
  }

  async function reorderColumns(boardId: string, columnIds: string[]) {
    helpers.guardDemoMutation()
    const visit = captureColumnVisit(boardId)
    return runColumnMutation(visit, async () => {
      try {
        state.error.value = null
        const reorderedColumns = await columnsApi.reorderColumns(boardId, columnIds)
        markColumnMutation(visit)

        if (ownsTargetBoard(visit)) {
          state.currentBoard.value!.columns = reorderedColumns
        } else if (!isCurrentVisit(visit) && state.currentBoard.value?.id === boardId) {
          await reconcileReopenedBoard(visit)
        }

        if (isCurrentVisit(visit)) helpers.toast.success('Columns reordered successfully')
        return reorderedColumns
      } catch (e: unknown) {
        if (isCurrentVisit(visit)) {
          helpers.handleApiError(e, 'Failed to reorder columns')
        }
        throw e
      }
    })
  }

  return {
    createColumn,
    updateColumn,
    deleteColumn,
    reorderColumns,
  }
}
