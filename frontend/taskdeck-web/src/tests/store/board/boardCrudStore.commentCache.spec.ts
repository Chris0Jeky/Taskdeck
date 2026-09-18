import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

const { mockBoardsApi, mockCardsApi, mockLabelsApi } = vi.hoisted(() => ({
  mockBoardsApi: { getBoard: vi.fn() },
  mockCardsApi: { getCards: vi.fn() },
  mockLabelsApi: { getLabels: vi.fn() },
}))

vi.mock('../../../api/boardsApi', () => ({ boardsApi: mockBoardsApi }))
vi.mock('../../../api/cardsApi', () => ({ cardsApi: mockCardsApi }))
vi.mock('../../../api/labelsApi', () => ({ labelsApi: mockLabelsApi }))
vi.mock('../../../utils/demoData', () => ({
  buildDemoBoardList: vi.fn(() => []),
  buildDemoBoardDetail: vi.fn(),
}))

import { createBoardCrudActions } from '../../../store/board/boardCrudStore'
import { initialCardFilters } from '../../../store/board/boardState'

type BoardFixture = {
  id: string
  name: string
  columns: Array<{ id: string; cardCount: number }>
}

type CardFixture = {
  id: string
  columnId: string
}

function board(name: string, id = 'board-1'): BoardFixture {
  return {
    id,
    name,
    columns: [{ id: 'column-1', cardCount: 0 }],
  }
}

function card(id = 'card-1'): CardFixture {
  return { id, columnId: 'column-1' }
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((innerResolve, innerReject) => {
    resolve = innerResolve
    reject = innerReject
  })
  return { promise, resolve, reject }
}

function createState() {
  return {
    boards: ref([{ id: 'board-1', name: 'Board' }]),
    activeBoardId: ref<string | null>('board-1'),
    currentBoard: ref<BoardFixture | null>(board('Old')),
    currentBoardRequestGeneration: ref(0),
    currentBoardPayloadGeneration: ref(0),
    currentBoardCards: ref<CardFixture[]>([card()]),
    currentBoardLabels: ref<Array<{ id: string }>>([]),
    cardCommentsByCardId: ref<Record<string, unknown>>({
      'card-1': [{ id: 'comment-1', content: 'Draft-adjacent comment' }],
    }),
    boardPresenceMembers: ref<Array<{ id: string }>>([]),
    editingCardId: ref<string | null>('card-1'),
    loading: ref(false),
    error: ref<string | null>(null),
    filters: ref(initialCardFilters()),
  }
}

function createHelpers() {
  const epochs = new Map<string, number>()
  return {
    guardDemoMutation: vi.fn(),
    handleApiError: vi.fn((_error: unknown, fallback: string) => fallback),
    isDemoMode: false,
    toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
    getBoardDetailMutationEpoch: vi.fn((boardId: string) => epochs.get(boardId) ?? 0),
    markBoardDetailMutation: vi.fn((boardId: string) => {
      epochs.set(boardId, (epochs.get(boardId) ?? 0) + 1)
    }),
  }
}

describe('board detail comment-cache ownership', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockBoardsApi.getBoard.mockResolvedValue(board('Fresh'))
    mockCardsApi.getCards.mockResolvedValue([card()])
    mockLabelsApi.getLabels.mockResolvedValue([])
  })

  it('preserves loaded comments only for an opted-in same-board background reconciliation', async () => {
    const state = createState()
    const cachedComments = state.cardCommentsByCardId.value
    const { fetchBoard } = createBoardCrudActions(state as any, createHelpers() as any)

    await expect(fetchBoard('board-1', {
      intent: 'background',
      preserveCardComments: true,
    })).resolves.toBe(true)

    expect(state.currentBoard.value).toEqual({
      id: 'board-1',
      name: 'Fresh',
      columns: [{ id: 'column-1', cardCount: 1 }],
    })
    expect(state.cardCommentsByCardId.value).toBe(cachedComments)

    await expect(fetchBoard('board-1')).resolves.toBe(true)
    expect(state.cardCommentsByCardId.value).toEqual({})
  })

  it('does not carry one board comment cache into a different board', async () => {
    const state = createState()
    mockBoardsApi.getBoard.mockResolvedValue(board('Other', 'board-2'))
    mockCardsApi.getCards.mockResolvedValue([])
    const { fetchBoard } = createBoardCrudActions(state as any, createHelpers() as any)

    await expect(fetchBoard('board-2', {
      intent: 'background',
      preserveCardComments: true,
    })).resolves.toBe(true)

    expect(state.currentBoard.value?.id).toBe('board-2')
    expect(state.cardCommentsByCardId.value).toEqual({})
  })

  it('upgrades a same-board explicit read when a kept-open editor queues preservation behind it', async () => {
    const state = createState()
    const cachedComments = state.cardCommentsByCardId.value
    const boardRead = deferred<BoardFixture>()
    const cardRead = deferred<CardFixture[]>()
    const labelRead = deferred<Array<{ id: string }>>()
    mockBoardsApi.getBoard
      .mockReturnValueOnce(boardRead.promise)
      .mockResolvedValueOnce(board('Reconciled'))
    mockCardsApi.getCards
      .mockReturnValueOnce(cardRead.promise)
      .mockResolvedValueOnce([card()])
    mockLabelsApi.getLabels
      .mockReturnValueOnce(labelRead.promise)
      .mockResolvedValueOnce([])

    const { fetchBoard } = createBoardCrudActions(state as any, createHelpers() as any)
    const explicitRead = fetchBoard('board-1')
    const keptOpenRefresh = fetchBoard('board-1', {
      intent: 'background',
      preserveCardComments: true,
    })

    boardRead.resolve(board('Explicit'))
    cardRead.resolve([card()])
    labelRead.resolve([])

    await expect(explicitRead).resolves.toBe(true)
    await expect(keptOpenRefresh).resolves.toBe(true)

    expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)
    expect(state.currentBoard.value?.name).toBe('Reconciled')
    expect(state.cardCommentsByCardId.value).toBe(cachedComments)
  })
})
