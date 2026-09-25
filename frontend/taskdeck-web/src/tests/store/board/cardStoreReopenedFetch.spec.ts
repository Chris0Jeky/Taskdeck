import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createBoardState } from '../../../store/board/boardState'
import { createBoardUiActions } from '../../../store/board/boardUiStore'
import type { BoardHelpers } from '../../../store/board/boardStoreHelpers'
import type { BoardDetail, Card } from '../../../types/board'

const api = vi.hoisted(() => ({
  getBoard: vi.fn(), getCards: vi.fn(), getLabels: vi.fn(), moveCard: vi.fn(), deleteCard: vi.fn(),
}))
vi.mock('../../../api/boardsApi', () => ({ boardsApi: { getBoard: api.getBoard } }))
vi.mock('../../../api/cardsApi', () => ({ cardsApi: api }))
vi.mock('../../../api/labelsApi', () => ({ labelsApi: { getLabels: api.getLabels } }))
vi.mock('../../../api/http', () => ({ BOARD_REQUEST_TIMEOUT_MS: 10000 }))
vi.mock('../../../utils/tokenStorage', () => ({ getToken: () => null, getObservedCredentialGeneration: () => 0 }))
vi.mock('../../../utils/demoData', () => ({ buildDemoBoardList: () => [] }))
vi.mock('../../../utils/errorMessage', () => ({ getErrorMessage: (_error: unknown, fallback: string) => fallback }))
vi.mock('axios', () => ({ default: { isCancel: () => false } }))
import { createCardActions } from '../../../store/board/cardStore'
import { createBoardCrudActions } from '../../../store/board/boardCrudStore'

const stamp = '2026-09-25T10:00:00Z'
function board(id = 'board'): BoardDetail {
  return {
    id, name: id, description: null, isArchived: false, createdAt: stamp, updatedAt: stamp,
    columns: ['todo', 'doing'].map((columnId, position) => ({
      id: columnId, boardId: id, name: columnId, position, wipLimit: null,
      cardCount: position === 0 ? 1 : 0, createdAt: stamp, updatedAt: stamp,
    })),
  }
}
function card(columnId = 'todo', boardId = 'board'): Card {
  return {
    id: boardId === 'board' ? 'card' : 'other-card', boardId, columnId, title: 'Release', description: '',
    dueDate: null, isBlocked: false, blockReason: null, position: 0, labels: [], createdAt: stamp, updatedAt: stamp,
  }
}
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(settle => { resolve = settle })
  return { promise, resolve }
}

// Use the real shared detail reader, not a stubbed refresh callback: a late
// mutation invalidates its explicit read, which does not schedule a successor.
describe('card mutation reconciliation with an in-flight reopened-board read', () => {
  beforeEach(() => { Object.values(api).forEach(mock => mock.mockReset()) })

  for (const operation of ['move', 'delete'] as const) {
    it.each(['board', 'other-board'])(`${operation} recovers the reopened board while %s is still installed`, async installedId => {
      const state = createBoardState()
      state.currentBoard.value = board()
      state.currentBoardCards.value = [card()]
      const epochs = new Map<string, number>()
      const helpers = {
        isDemoMode: false, guardDemoMutation: vi.fn(), handleApiError: vi.fn(),
        toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
        updateColumnCardCount: vi.fn(),
        getBoardDetailMutationEpoch: (id: string) => epochs.get(id) ?? 0,
        markBoardDetailMutation: vi.fn((id: string) => { epochs.set(id, (epochs.get(id) ?? 0) + 1) }),
      }
      const typedHelpers = helpers as unknown as BoardHelpers
      const crud = createBoardCrudActions(state, typedHelpers)
      const actions = createCardActions(state, typedHelpers, crud.fetchBoard)
      const ui = createBoardUiActions(state)
      ui.beginBoardViewVisit('board')
      const mutation = deferred<Card>()
      api.moveCard.mockReturnValueOnce(mutation.promise)
      api.deleteCard.mockReturnValueOnce(mutation.promise)
      const pendingMutation = operation === 'move'
        ? actions.moveCard('board', 'card', 'doing', 0)
        : actions.deleteCard('board', 'card')

      ui.beginBoardViewVisit('other-board')
      state.currentBoard.value = board(installedId)
      state.currentBoardCards.value = [card('todo', installedId)]
      ui.beginBoardViewVisit('board')
      const oldBoard = deferred<BoardDetail>()
      const oldCards = deferred<Card[]>()
      const expectedCards = operation === 'move' ? [card('doing')] : []
      api.getBoard.mockReturnValueOnce(oldBoard.promise).mockResolvedValueOnce(board())
      api.getCards.mockReturnValueOnce(oldCards.promise).mockResolvedValueOnce(expectedCards)
      api.getLabels.mockResolvedValue([])
      const explicitRead = crud.fetchBoard('board')

      mutation.resolve(card('doing'))
      await Promise.resolve()
      await Promise.resolve()
      // Recovery must wait for the existing read, not start parallel fan-out.
      expect(api.getBoard).toHaveBeenCalledTimes(1)
      oldBoard.resolve(board())
      oldCards.resolve([card()])
      expect(await explicitRead).toBe(false)
      await pendingMutation

      expect(api.getBoard).toHaveBeenCalledTimes(2)
      expect(api.getCards).toHaveBeenCalledTimes(2)
      expect(state.currentBoard.value?.id).toBe('board')
      expect(state.currentBoardCards.value).toEqual(expectedCards)
      expect(state.currentBoard.value?.columns.map(column => column.cardCount))
        .toEqual(operation === 'move' ? [0, 1] : [0, 0])
      expect(helpers.updateColumnCardCount).not.toHaveBeenCalled()
      expect(helpers.toast.success).not.toHaveBeenCalled()
      expect(api.getBoard.mock.calls[1][1].timeout).toBe(10000)
      expect(api.getBoard.mock.calls[1][1].skipRetry).toBe(true)
    })
  }
})
