/**
 * Shared helper utilities used across board sub-stores.
 */
import { useToastStore } from '../toastStore'
import { getErrorMessage } from '../../utils/errorMessage'
import { isDemoMode, DemoModeError } from '../../utils/demoMode'
import type { BoardState } from './boardState'

export function createBoardHelpers(state: BoardState) {
  const toast = useToastStore()
  const boardDetailMutationEpochs = new Map<string, number>()

  const handleApiError = (err: unknown, fallback: string) => {
    const message = getErrorMessage(err, fallback)
    state.error.value = message
    toast.error(message)
  }

  function guardDemoMutation(): never | void {
    if (isDemoMode) {
      toast.info('This action is view-only in demo mode.')
      throw new DemoModeError()
    }
  }

  const isHttpNotFound = (err: unknown): boolean => {
    const candidate = err as { response?: { status?: number } } | null
    return candidate?.response?.status === 404
  }

  const isHttpConflict = (err: unknown): boolean => {
    const candidate = err as { response?: { status?: number } } | null
    return candidate?.response?.status === 409
  }

  const updateColumnCardCount = (columnId: string, delta: number) => {
    if (!state.currentBoard.value) return

    const column = state.currentBoard.value.columns.find((c) => c.id === columnId)
    if (!column) return

    const nextCount = (column.cardCount ?? 0) + delta
    column.cardCount = Math.max(0, nextCount)
  }

  const getBoardDetailMutationEpoch = (boardId: string) =>
    boardDetailMutationEpochs.get(boardId) ?? 0

  const markBoardDetailMutation = (boardId: string) => {
    boardDetailMutationEpochs.set(boardId, getBoardDetailMutationEpoch(boardId) + 1)
  }

  return {
    toast,
    handleApiError,
    guardDemoMutation,
    isHttpNotFound,
    isHttpConflict,
    updateColumnCardCount,
    getBoardDetailMutationEpoch,
    markBoardDetailMutation,
    isDemoMode,
  }
}

export type BoardHelpers = ReturnType<typeof createBoardHelpers>
