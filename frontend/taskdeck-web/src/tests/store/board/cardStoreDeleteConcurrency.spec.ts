import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

const { mockCardsApi } = vi.hoisted(() => ({
  mockCardsApi: {
    setArchived: vi.fn(),
    getCards: vi.fn(),
    createCard: vi.fn(),
    updateCard: vi.fn(),
    deleteCard: vi.fn(),
    moveCard: vi.fn(),
    getCardProvenance: vi.fn(),
  },
}))

vi.mock('../../../api/cardsApi', () => ({ cardsApi: mockCardsApi }))
vi.mock('../../../utils/errorMessage', () => ({
  getErrorMessage: vi.fn((_error: unknown, fallback: string) => fallback),
}))

import { createCardActions } from '../../../store/board/cardStore'
import { createBoardState } from '../../../store/board/boardState'

function createState() {
  return {
    ...createBoardState(),
    currentBoard: ref<{
      id: string
      columns: Array<{ id: string; name: string; cardCount: number }>
    } | null>({
      id: 'board-1',
      columns: [
        { id: 'col-1', name: 'Todo', cardCount: 1 },
        { id: 'col-2', name: 'Done', cardCount: 0 },
      ],
    }),
    currentBoardCards: ref([
      {
        id: 'card-1',
        boardId: 'board-1',
        columnId: 'col-1',
        title: 'Ship release',
        description: '',
        dueDate: null,
        isBlocked: false,
        blockReason: null,
        position: 0,
        labels: [],
        createdAt: '2026-09-20T10:00:00Z',
        updatedAt: '2026-09-20T10:00:00Z',
      },
    ]),
    cardCommentsByCardId: ref<Record<string, unknown>>({
      'card-1': [{ id: 'comment-1' }],
    }),
    loading: ref(false),
    error: ref<string | null>(null),
  }
}

function createHelpers(state: ReturnType<typeof createState>) {
  const updateColumnCardCount = vi.fn((columnId: string, delta: number) => {
    const column = state.currentBoard.value?.columns.find(candidate => candidate.id === columnId)
    if (column) column.cardCount += delta
  })

  return {
    guardDemoMutation: vi.fn(),
    handleApiError: vi.fn(),
    isHttpConflict: vi.fn().mockReturnValue(false),
    isDemoMode: false,
    toast: { success: vi.fn(), error: vi.fn() },
    updateColumnCardCount,
    markBoardDetailMutation: vi.fn(),
  }
}

function deferredDelete() {
  let resolve!: () => void
  const promise = new Promise<void>((settle) => {
    resolve = () => settle()
  })
  return { promise, resolve }
}

describe('cardStore delete concurrency', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockCardsApi.deleteCard.mockReset()
  })

  it('decrements the card column that is current when the delete commits', async () => {
    const state = createState()
    const helpers = createHelpers(state)
    const deletion = deferredDelete()
    mockCardsApi.deleteCard.mockReturnValueOnce(deletion.promise)
    const { deleteCard } = createCardActions(
      state as never,
      helpers as never,
      vi.fn().mockResolvedValue(true),
    )

    const pendingDelete = deleteCard('board-1', 'card-1')

    state.currentBoardCards.value[0].columnId = 'col-2'
    state.currentBoard.value!.columns[0].cardCount = 0
    state.currentBoard.value!.columns[1].cardCount = 1
    deletion.resolve()
    await pendingDelete

    expect(state.currentBoardCards.value).toEqual([])
    expect(state.currentBoard.value!.columns.map(column => column.cardCount)).toEqual([0, 0])
    expect(helpers.updateColumnCardCount).toHaveBeenCalledOnce()
    expect(helpers.updateColumnCardCount).toHaveBeenCalledWith('col-2', -1)
    expect(state.cardCommentsByCardId.value).not.toHaveProperty('card-1')
  })

  it('does not apply a late delete response to a newly selected board', async () => {
    const state = createState()
    const helpers = createHelpers(state)
    const deletion = deferredDelete()
    mockCardsApi.deleteCard.mockReturnValueOnce(deletion.promise)
    const { deleteCard } = createCardActions(
      state as never,
      helpers as never,
      vi.fn().mockResolvedValue(true),
    )

    const pendingDelete = deleteCard('board-1', 'card-1')

    const nextBoardCard = {
      id: 'card-2',
      boardId: 'board-2',
      columnId: 'col-next',
      title: 'Next board card',
    }
    state.currentBoard.value = {
      id: 'board-2',
      columns: [{ id: 'col-next', name: 'Next', cardCount: 1 }],
    }
    state.currentBoardCards.value = [nextBoardCard as never]
    state.cardCommentsByCardId.value = {
      'card-2': [{ id: 'comment-2' }],
    }
    deletion.resolve()
    await pendingDelete

    expect(state.currentBoardCards.value).toEqual([nextBoardCard])
    expect(state.currentBoard.value.columns[0].cardCount).toBe(1)
    expect(state.cardCommentsByCardId.value).toEqual({
      'card-2': [{ id: 'comment-2' }],
    })
    expect(helpers.updateColumnCardCount).not.toHaveBeenCalled()
  })

  it('does not decrement again when a refresh already removed the card', async () => {
    const state = createState()
    const helpers = createHelpers(state)
    const deletion = deferredDelete()
    mockCardsApi.deleteCard.mockReturnValueOnce(deletion.promise)
    const { deleteCard } = createCardActions(
      state as never,
      helpers as never,
      vi.fn().mockResolvedValue(true),
    )

    const pendingDelete = deleteCard('board-1', 'card-1')

    state.currentBoardCards.value = []
    state.currentBoard.value!.columns[0].cardCount = 0
    deletion.resolve()
    await pendingDelete

    expect(state.currentBoardCards.value).toEqual([])
    expect(state.currentBoard.value!.columns[0].cardCount).toBe(0)
    expect(helpers.updateColumnCardCount).not.toHaveBeenCalled()
    expect(state.cardCommentsByCardId.value).not.toHaveProperty('card-1')
  })
})
