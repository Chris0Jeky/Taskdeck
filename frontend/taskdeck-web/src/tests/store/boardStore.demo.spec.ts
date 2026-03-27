import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: true,
  }
})

vi.mock('../../api/boardsApi', () => ({
  boardsApi: {
    getBoards: vi.fn(),
    getBoard: vi.fn(),
    createBoard: vi.fn(),
  },
}))

vi.mock('../../api/cardsApi', () => ({
  cardsApi: { getCards: vi.fn(), createCard: vi.fn(), updateCard: vi.fn(), moveCard: vi.fn(), deleteCard: vi.fn() },
}))
vi.mock('../../api/cardCommentsApi', () => ({
  cardCommentsApi: { getComments: vi.fn(), createComment: vi.fn(), updateComment: vi.fn(), deleteComment: vi.fn() },
}))
vi.mock('../../api/columnsApi', () => ({
  columnsApi: { createColumn: vi.fn(), updateColumn: vi.fn(), deleteColumn: vi.fn() },
}))
vi.mock('../../api/labelsApi', () => ({
  labelsApi: { getLabels: vi.fn(), createLabel: vi.fn(), updateLabel: vi.fn(), deleteLabel: vi.fn() },
}))

const toastMocks = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

import { useBoardStore } from '../../store/boardStore'
import { boardsApi } from '../../api/boardsApi'

describe('boardStore demo mode', () => {
  let store: ReturnType<typeof useBoardStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    store = useBoardStore()
  })

  it('fetchBoards returns demo boards without calling API', async () => {
    await store.fetchBoards()

    expect(store.boards).toHaveLength(2)
    expect(store.boards[0].name).toBe('Product Backlog')
    expect(store.boards[1].name).toBe('Sprint 12')
    expect(boardsApi.getBoards).not.toHaveBeenCalled()
  })

  it('fetchBoard returns demo board detail with columns and cards', async () => {
    await store.fetchBoard('demo-board-1')

    expect(store.currentBoard).not.toBeNull()
    expect(store.currentBoard!.name).toBe('Product Backlog')
    expect(store.currentBoard!.columns).toHaveLength(3)
    expect(store.currentBoardCards.length).toBeGreaterThan(0)
    expect(boardsApi.getBoard).not.toHaveBeenCalled()
  })

  it('createBoard throws DemoModeError and shows toast', async () => {
    await expect(store.createBoard({ name: 'Test', description: null })).rejects.toThrow('view-only in demo mode')
    expect(toastMocks.info).toHaveBeenCalledWith('This action is view-only in demo mode.')
    expect(boardsApi.createBoard).not.toHaveBeenCalled()
  })
})
