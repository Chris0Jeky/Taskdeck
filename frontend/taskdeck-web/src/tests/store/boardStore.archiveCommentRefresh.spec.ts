import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useBoardStore } from '../../store/boardStore'
import { boardsApi } from '../../api/boardsApi'
import { cardsApi } from '../../api/cardsApi'
import { labelsApi } from '../../api/labelsApi'
import type { BoardDetail, Card } from '../../types/board'

const { warning, error, success } = vi.hoisted(() => ({
  warning: vi.fn(),
  error: vi.fn(),
  success: vi.fn(),
}))

vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))
vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getCards: vi.fn(),
    setArchived: vi.fn(),
  },
}))
vi.mock('../../api/labelsApi', () => ({ labelsApi: { getLabels: vi.fn() } }))
vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({ warning, error, success }),
}))
vi.mock('../../utils/demoMode', async (original) => ({
  ...await original<typeof import('../../utils/demoMode')>(),
  isDemoMode: false,
}))

function board(): BoardDetail {
  return {
    id: 'board-1',
    name: 'Board',
    columns: [{
      id: 'column-1',
      boardId: 'board-1',
      name: 'Todo',
      position: 0,
      cardCount: 2,
      wipLimit: null,
    }],
  } as BoardDetail
}

function card(id: string, parentCardId: string | null = null): Card {
  return {
    id,
    boardId: 'board-1',
    columnId: 'column-1',
    title: id,
    parentCardId,
    labels: [],
    updatedAt: '2026-09-18T00:00:00Z',
  } as unknown as Card
}

describe('archive hierarchy refresh comment ownership', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.mocked(boardsApi.getBoard).mockResolvedValue(board())
    vi.mocked(cardsApi.getCards).mockResolvedValue([card('child')])
    vi.mocked(cardsApi.setArchived).mockResolvedValue({
      ...card('parent'),
      isArchived: true,
    })
    vi.mocked(labelsApi.getLabels).mockResolvedValue([])
  })

  it('keeps the open editor comment cache while installing detached children', async () => {
    const store = useBoardStore()
    store.currentBoard = board()
    store.currentBoardCards = [card('parent'), card('child', 'parent')]
    const cachedComments = {
      parent: [{ id: 'comment-parent', content: 'Edit started while archive was pending' }],
      child: [{ id: 'comment-child', content: 'Loaded child discussion' }],
    } as any
    store.cardCommentsByCardId = cachedComments
    // Pinia exposes the assigned object through its reactive store proxy. Capture
    // that installed reference so the assertion proves reconciliation itself did
    // not replace the cache, rather than comparing a proxy with its raw source.
    const installedCommentCache = store.cardCommentsByCardId

    await store.setCardArchived(
      'board-1',
      'parent',
      true,
      '2026-09-18T00:00:00Z',
      'v1:children',
    )

    expect(store.currentBoardCards).toEqual([expect.objectContaining({
      id: 'child',
      parentCardId: null,
    })])
    expect(store.cardCommentsByCardId).toBe(installedCommentCache)
    expect(warning).not.toHaveBeenCalled()
    expect(error).not.toHaveBeenCalled()
  })
})
