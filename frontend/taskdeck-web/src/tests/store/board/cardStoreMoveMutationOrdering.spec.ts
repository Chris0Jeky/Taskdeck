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

interface TestCard {
  id: string
  boardId: string
  columnId: string
  title: string
  description: string
  dueDate: null
  isBlocked: boolean
  blockReason: null
  position: number
  labels: never[]
  createdAt: string
  updatedAt: string
}

const originalCard: TestCard = {
  id: 'card-1',
  boardId: 'board-1',
  columnId: 'col-a',
  title: 'Ship release',
  description: '',
  dueDate: null,
  isBlocked: false,
  blockReason: null,
  position: 0,
  labels: [],
  createdAt: '2026-09-20T10:00:00Z',
  updatedAt: '2026-09-20T10:00:00Z',
}

function createState() {
  return {
    currentBoard: ref<{
      id: string
      columns: Array<{ id: string; name: string; cardCount: number }>
    } | null>({
      id: 'board-1',
      columns: [
        { id: 'col-a', name: 'Todo', cardCount: 1 },
        { id: 'col-b', name: 'Doing', cardCount: 0 },
        { id: 'col-c', name: 'Done', cardCount: 0 },
      ],
    }),
    currentBoardCards: ref<TestCard[]>([{ ...originalCard }]),
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

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((settle, fail) => {
    resolve = settle
    reject = fail
  })
  return { promise, resolve, reject }
}

async function flushPromises() {
  await Promise.resolve()
  await Promise.resolve()
}

function moved(columnId: string, updatedAt: string): TestCard {
  return { ...originalCard, columnId, updatedAt }
}

function counts(state: ReturnType<typeof createState>) {
  return state.currentBoard.value!.columns.map(column => column.cardCount)
}

describe('cardStore move/delete mutation ordering', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockCardsApi.moveCard.mockReset()
    mockCardsApi.deleteCard.mockReset()
  })

  it('ignores an older move response after authoritative state advanced to another column', async () => {
    const state = createState()
    const helpers = createHelpers(state)
    const firstMove = deferred<TestCard>()
    mockCardsApi.moveCard.mockReturnValueOnce(firstMove.promise)
    const actions = createCardActions(state as never, helpers as never, vi.fn().mockResolvedValue(true))

    const pendingMove = actions.moveCard('board-1', 'card-1', 'col-b', 0)

    state.currentBoardCards.value[0] = moved('col-c', '2026-09-20T10:02:00Z')
    state.currentBoard.value!.columns[0].cardCount = 0
    state.currentBoard.value!.columns[2].cardCount = 1

    firstMove.resolve(moved('col-b', '2026-09-20T10:01:00Z'))
    await pendingMove

    expect(state.currentBoardCards.value).toEqual([
      moved('col-c', '2026-09-20T10:02:00Z'),
    ])
    expect(counts(state)).toEqual([0, 0, 1])
    expect(helpers.updateColumnCardCount).not.toHaveBeenCalled()
  })

  it('serializes two accepted moves so the later intent settles last', async () => {
    const state = createState()
    const helpers = createHelpers(state)
    const firstMove = deferred<TestCard>()
    const secondMove = deferred<TestCard>()
    mockCardsApi.moveCard
      .mockReturnValueOnce(firstMove.promise)
      .mockReturnValueOnce(secondMove.promise)
    const actions = createCardActions(state as never, helpers as never, vi.fn().mockResolvedValue(true))

    const pendingFirst = actions.moveCard('board-1', 'card-1', 'col-b', 0)
    const pendingSecond = actions.moveCard('board-1', 'card-1', 'col-c', 0)

    await flushPromises()
    expect(mockCardsApi.moveCard).toHaveBeenCalledTimes(1)

    firstMove.resolve(moved('col-b', '2026-09-20T10:01:00Z'))
    await pendingFirst
    await flushPromises()
    expect(mockCardsApi.moveCard).toHaveBeenCalledTimes(2)
    expect(state.currentBoardCards.value).toEqual([
      moved('col-b', '2026-09-20T10:01:00Z'),
    ])
    expect(counts(state)).toEqual([0, 1, 0])

    secondMove.resolve(moved('col-c', '2026-09-20T10:02:00Z'))
    await pendingSecond

    expect(state.currentBoardCards.value).toEqual([
      moved('col-c', '2026-09-20T10:02:00Z'),
    ])
    expect(counts(state)).toEqual([0, 0, 1])
  })

  it('does not let a move response reinsert a card deleted by the later intent', async () => {
    const state = createState()
    const helpers = createHelpers(state)
    const firstMove = deferred<TestCard>()
    mockCardsApi.moveCard.mockReturnValueOnce(firstMove.promise)
    mockCardsApi.deleteCard.mockResolvedValueOnce(undefined)
    const actions = createCardActions(state as never, helpers as never, vi.fn().mockResolvedValue(true))

    const pendingMove = actions.moveCard('board-1', 'card-1', 'col-b', 0)
    const pendingDelete = actions.deleteCard('board-1', 'card-1')

    await flushPromises()
    expect(mockCardsApi.moveCard).toHaveBeenCalledTimes(1)
    expect(mockCardsApi.deleteCard).not.toHaveBeenCalled()

    firstMove.resolve(moved('col-b', '2026-09-20T10:01:00Z'))
    await pendingMove
    await pendingDelete

    expect(mockCardsApi.deleteCard).toHaveBeenCalledTimes(1)
    expect(state.currentBoardCards.value).toEqual([])
    expect(counts(state)).toEqual([0, 0, 0])
    expect(state.cardCommentsByCardId.value).not.toHaveProperty('card-1')
  })

  it('keeps the earlier confirmed move when the later serialized move fails', async () => {
    const state = createState()
    const helpers = createHelpers(state)
    const firstMove = deferred<TestCard>()
    const secondMove = deferred<TestCard>()
    mockCardsApi.moveCard
      .mockReturnValueOnce(firstMove.promise)
      .mockReturnValueOnce(secondMove.promise)
    const actions = createCardActions(state as never, helpers as never, vi.fn().mockResolvedValue(true))

    const pendingFirst = actions.moveCard('board-1', 'card-1', 'col-b', 0)
    const pendingSecond = actions.moveCard('board-1', 'card-1', 'col-c', 0)

    firstMove.resolve(moved('col-b', '2026-09-20T10:01:00Z'))
    await pendingFirst
    await flushPromises()

    const failure = new Error('newer move failed')
    secondMove.reject(failure)
    await expect(pendingSecond).rejects.toBe(failure)

    expect(state.currentBoardCards.value).toEqual([
      moved('col-b', '2026-09-20T10:01:00Z'),
    ])
    expect(counts(state)).toEqual([0, 1, 0])
    expect(helpers.handleApiError).toHaveBeenCalledWith(failure, 'Failed to move card')
  })
})
