/**
 * Regression tests for #686: WIP-limit duplicate toast emission.
 *
 * Guarantees that a single rejected createCard or moveCard action caused by a
 * WIP-limit violation emits exactly ONE error toast — not zero, not two or more.
 *
 * The failure mode being guarded against: if the error-handling path were
 * duplicated (e.g. both an inline `toast.error()` and a subsequent
 * `handleApiError()` call for the same rejection), the toast store would
 * accumulate extra entries that the user would see as a stacked duplicate.
 */
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useBoardStore } from '../../store/boardStore'
import { useToastStore } from '../../store/toastStore'
import { cardsApi } from '../../api/cardsApi'
import type { BoardDetail, Column } from '../../types/board'

// ── API mocks ──────────────────────────────────────────────────────────────────

vi.mock('../../api/boardsApi', () => ({
  boardsApi: {
    getBoards: vi.fn(),
    getBoard: vi.fn(),
    createBoard: vi.fn(),
    updateBoard: vi.fn(),
    deleteBoard: vi.fn(),
  },
}))

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getCards: vi.fn(),
    createCard: vi.fn(),
    updateCard: vi.fn(),
    moveCard: vi.fn(),
    deleteCard: vi.fn(),
    getCardProvenance: vi.fn(),
  },
}))

vi.mock('../../api/cardCommentsApi', () => ({
  cardCommentsApi: {
    getComments: vi.fn(),
    createComment: vi.fn(),
    updateComment: vi.fn(),
    deleteComment: vi.fn(),
  },
}))

vi.mock('../../api/columnsApi', () => ({
  columnsApi: {
    createColumn: vi.fn(),
    updateColumn: vi.fn(),
    deleteColumn: vi.fn(),
  },
}))

vi.mock('../../api/labelsApi', () => ({
  labelsApi: {
    getLabels: vi.fn(),
    createLabel: vi.fn(),
    updateLabel: vi.fn(),
    deleteLabel: vi.fn(),
  },
}))

// ── Helpers ────────────────────────────────────────────────────────────────────

/** Constructs a WIP-limit-exceeded API error in Axios response shape. */
function makeWipLimitError(message = 'Work-in-progress limit would be exceeded.') {
  return {
    response: {
      status: 422,
      data: {
        errorCode: 'WipLimitExceeded',
        message,
      },
    },
    message,
  }
}

function makeColumn(overrides: Partial<Column> = {}): Column {
  return {
    id: 'column-1',
    boardId: 'board-1',
    name: 'In Progress',
    position: 0,
    wipLimit: 2,
    cardCount: 2, // already at capacity
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  }
}

function makeBoard(columns: Column[] = []): BoardDetail {
  return {
    id: 'board-1',
    name: 'Test Board',
    description: '',
    isArchived: false,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    columns,
  }
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe('boardStore — WIP-limit toast deduplication (#686)', () => {
  let boardStore: ReturnType<typeof useBoardStore>
  let toastStore: ReturnType<typeof useToastStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    boardStore = useBoardStore()
    toastStore = useToastStore()
    vi.clearAllMocks()

    // Seed a board with a full column so context is realistic
    boardStore.currentBoard = makeBoard([makeColumn()])
  })

  // ── createCard ──────────────────────────────────────────────────────────────

  describe('createCard — WIP-limit rejection', () => {
    it('emits exactly one error toast when the API rejects with WipLimitExceeded', async () => {
      vi.mocked(cardsApi.createCard).mockRejectedValue(makeWipLimitError())

      await expect(
        boardStore.createCard('board-1', {
          title: 'Card over limit',
          columnId: 'column-1',
        }),
      ).rejects.toBeDefined()

      const errorToasts = toastStore.toasts.filter((t) => t.type === 'error')
      expect(errorToasts).toHaveLength(1)
    })

    it('does not accumulate toasts across two consecutive WIP-limit rejections', async () => {
      vi.mocked(cardsApi.createCard).mockRejectedValue(makeWipLimitError())

      await expect(
        boardStore.createCard('board-1', {
          title: 'First card over limit',
          columnId: 'column-1',
        }),
      ).rejects.toBeDefined()

      await expect(
        boardStore.createCard('board-1', {
          title: 'Second card over limit',
          columnId: 'column-1',
        }),
      ).rejects.toBeDefined()

      // Each rejected call should add exactly one toast; two calls → two toasts total.
      // If the error path is duplicated internally, the count would be 4 (two per call).
      const errorToasts = toastStore.toasts.filter((t) => t.type === 'error')
      expect(errorToasts).toHaveLength(2)
    })

    it('toast message matches the WIP-limit error message returned by the API', async () => {
      const apiMessage = 'Work-in-progress limit would be exceeded.'
      vi.mocked(cardsApi.createCard).mockRejectedValue(makeWipLimitError(apiMessage))

      await expect(
        boardStore.createCard('board-1', {
          title: 'Over limit',
          columnId: 'column-1',
        }),
      ).rejects.toBeDefined()

      expect(toastStore.toasts[0]?.message).toBe(apiMessage)
    })
  })

  // ── moveCard ────────────────────────────────────────────────────────────────

  describe('moveCard — WIP-limit rejection', () => {
    it('emits exactly one error toast when the API rejects with WipLimitExceeded', async () => {
      vi.mocked(cardsApi.moveCard).mockRejectedValue(makeWipLimitError())

      await expect(
        boardStore.moveCard('board-1', 'card-1', 'column-1', 2),
      ).rejects.toBeDefined()

      const errorToasts = toastStore.toasts.filter((t) => t.type === 'error')
      expect(errorToasts).toHaveLength(1)
    })

    it('does not accumulate toasts across two consecutive move rejections', async () => {
      vi.mocked(cardsApi.moveCard).mockRejectedValue(makeWipLimitError())

      await expect(boardStore.moveCard('board-1', 'card-a', 'column-1', 2)).rejects.toBeDefined()
      await expect(boardStore.moveCard('board-1', 'card-b', 'column-1', 2)).rejects.toBeDefined()

      // Two calls, each producing exactly one toast → two total.
      const errorToasts = toastStore.toasts.filter((t) => t.type === 'error')
      expect(errorToasts).toHaveLength(2)
    })

    it('emits one error toast per move failure regardless of error message variation', async () => {
      vi.mocked(cardsApi.moveCard)
        .mockRejectedValueOnce(makeWipLimitError('WIP limit reached on In Progress'))
        .mockRejectedValueOnce(makeWipLimitError('WIP limit reached on Done'))

      await expect(boardStore.moveCard('board-1', 'card-a', 'column-1', 0)).rejects.toBeDefined()
      await expect(boardStore.moveCard('board-1', 'card-b', 'column-2', 0)).rejects.toBeDefined()

      const errorToasts = toastStore.toasts.filter((t) => t.type === 'error')
      expect(errorToasts).toHaveLength(2)
      // Confirm messages differ (i.e. they are separate toasts from separate calls)
      expect(errorToasts[0]?.message).not.toBe(errorToasts[1]?.message)
    })
  })

  // ── cross-action deduplication ──────────────────────────────────────────────

  describe('mixed createCard + moveCard rejections', () => {
    it('each rejected action produces exactly one toast — no cross-action duplication', async () => {
      vi.mocked(cardsApi.createCard).mockRejectedValue(makeWipLimitError())
      vi.mocked(cardsApi.moveCard).mockRejectedValue(makeWipLimitError())

      await expect(
        boardStore.createCard('board-1', {
          title: 'New card',
          columnId: 'column-1',
        }),
      ).rejects.toBeDefined()

      await expect(boardStore.moveCard('board-1', 'card-x', 'column-1', 0)).rejects.toBeDefined()

      const errorToasts = toastStore.toasts.filter((t) => t.type === 'error')
      expect(errorToasts).toHaveLength(2)
    })
  })
})
