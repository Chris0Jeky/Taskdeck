import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import * as fc from 'fast-check'
import { useBoardStore } from '../../store/boardStore'
import { useCaptureStore } from '../../store/captureStore'
import { boardsApi } from '../../api/boardsApi'
import { cardsApi } from '../../api/cardsApi'
import { labelsApi } from '../../api/labelsApi'
import { captureApi } from '../../api/captureApi'
import type { Board, Card, Column } from '../../types/board'

/**
 * Store resilience property tests.
 * Key property: any sequence of store actions produces consistent state,
 * and any API error is handled (sets error state) even though it re-throws.
 */

// Mock all API modules
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

vi.mock('../../api/captureApi', () => ({
  captureApi: {
    createItem: vi.fn(),
    listItems: vi.fn(),
    getItem: vi.fn(),
    ignoreItem: vi.fn(),
    cancelItem: vi.fn(),
    enqueueTriage: vi.fn(),
    batchTriage: vi.fn(),
    updateSuggestion: vi.fn(),
  },
}))

// Suppress console noise in error-path tests
vi.spyOn(console, 'error').mockImplementation(() => {})
vi.spyOn(console, 'warn').mockImplementation(() => {})

function makeFakeBoard(overrides: Partial<Board> = {}): Board {
  return {
    id: crypto.randomUUID(),
    name: 'Test Board',
    description: null,
    isArchived: false,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  }
}

function makeFakeColumn(boardId: string, overrides: Partial<Column> = {}): Column {
  return {
    id: crypto.randomUUID(),
    boardId,
    name: 'Test Column',
    position: 0,
    wipLimit: null,
    cardCount: 0,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  }
}

describe('Board Store Resilience: API Error Handling', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('fetchBoards handles network errors and sets error state', async () => {
    vi.mocked(boardsApi.getBoards).mockRejectedValue(new Error('Network error'))

    const store = useBoardStore()
    // The store re-throws, but it should have set error state first
    await expect(store.fetchBoards()).rejects.toThrow('Network error')
    expect(store.error).toBeTruthy()
    expect(store.loading).toBe(false)
  })

  it('fetchBoards handles various error types and always sets error state', async () => {
    const errorTypes = [
      new Error('Network error'),
      new TypeError('Failed to fetch'),
      { message: 'Unknown error' },
    ]

    for (const err of errorTypes) {
      setActivePinia(createPinia())
      vi.mocked(boardsApi.getBoards).mockRejectedValue(err)

      const store = useBoardStore()
      try {
        await store.fetchBoards()
      } catch {
        // Expected: store re-throws
      }
      expect(store.error).toBeTruthy()
      expect(store.loading).toBe(false)
    }
  })

  it('createBoard handles API rejection with adversarial board names', async () => {
    const adversarialNames = [
      "<script>alert('xss')</script>",
      "'; DROP TABLE boards; --",
      '\u0000\uFEFF\u200B',
      '\u{1F468}\u200D\u{1F469}\u200D\u{1F467}\u200D\u{1F466}',
      'a'.repeat(10_000),
    ]

    for (const name of adversarialNames) {
      setActivePinia(createPinia())
      vi.mocked(boardsApi.createBoard).mockRejectedValue(
        new Error(`Validation failed for: ${name}`),
      )

      const store = useBoardStore()
      try {
        await store.createBoard({ name, description: null })
      } catch {
        // Expected: store re-throws after setting error state
      }
      // Store should remain in a consistent state
      expect(store.loading).toBe(false)
      expect(Array.isArray(store.boards)).toBe(true)
    }
  })

  it('createCard handles API rejection without corrupting state', async () => {
    setActivePinia(createPinia())

    const board = makeFakeBoard()
    const col = makeFakeColumn(board.id)

    vi.mocked(boardsApi.getBoard).mockResolvedValue({
      ...board,
      columns: [col],
    })
    vi.mocked(cardsApi.getCards).mockResolvedValue([])
    vi.mocked(labelsApi.getLabels).mockResolvedValue([])
    vi.mocked(cardsApi.createCard).mockRejectedValue(new Error('Validation error'))

    const store = useBoardStore()
    await store.fetchBoard(board.id)

    try {
      await store.createCard({
        boardId: board.id,
        columnId: col.id,
        title: "<script>alert('xss')</script>",
      })
    } catch {
      // Expected
    }

    // State should remain consistent
    expect(store.loading).toBe(false)
  })
})

describe('Board Store Resilience: State Consistency', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('rapid sequential fetchBoards calls maintain consistent state', async () => {
    const boards = [makeFakeBoard({ name: 'Board 1' }), makeFakeBoard({ name: 'Board 2' })]
    vi.mocked(boardsApi.getBoards).mockResolvedValue(boards)

    const store = useBoardStore()

    // Fire multiple fetches concurrently
    await Promise.all([store.fetchBoards(), store.fetchBoards(), store.fetchBoards()])

    expect(store.boards.length).toBe(2)
    expect(store.loading).toBe(false)
  })

  it('deleteBoard after fetchBoards maintains consistent board list', async () => {
    const board1 = makeFakeBoard({ name: 'Board 1' })
    const board2 = makeFakeBoard({ name: 'Board 2' })

    vi.mocked(boardsApi.getBoards).mockResolvedValue([board1, board2])
    vi.mocked(boardsApi.deleteBoard).mockResolvedValue(undefined)

    const store = useBoardStore()
    await store.fetchBoards()
    expect(store.boards.length).toBe(2)

    await store.deleteBoard(board1.id)
    expect(store.boards.length).toBe(1)
    expect(store.boards[0].id).toBe(board2.id)
  })
})

describe('Board Store Resilience: Property-Based Action Sequences', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  type StoreAction =
    | { type: 'fetchBoards' }
    | { type: 'createBoard'; name: string }
    | { type: 'deleteBoard'; index: number }

  const actionArb: fc.Arbitrary<StoreAction> = fc.oneof(
    fc.constant({ type: 'fetchBoards' } as StoreAction),
    fc
      .string({ minLength: 1, maxLength: 100 })
      .map((name) => ({ type: 'createBoard', name }) as StoreAction),
    fc.nat({ max: 10 }).map((index) => ({ type: 'deleteBoard', index }) as StoreAction),
  )

  it('any sequence of store actions produces consistent state', async () => {
    await fc.assert(
      fc.asyncProperty(fc.array(actionArb, { minLength: 1, maxLength: 10 }), async (actions) => {
        setActivePinia(createPinia())
        const store = useBoardStore()

        const fakeBoards: Board[] = [makeFakeBoard(), makeFakeBoard()]
        vi.mocked(boardsApi.getBoards).mockResolvedValue(fakeBoards)
        vi.mocked(boardsApi.createBoard).mockImplementation(async (dto) =>
          makeFakeBoard({
            name: typeof dto === 'string' ? dto : (dto as { name: string }).name,
          }),
        )
        vi.mocked(boardsApi.deleteBoard).mockResolvedValue(undefined)

        for (const action of actions) {
          try {
            switch (action.type) {
              case 'fetchBoards':
                await store.fetchBoards()
                break
              case 'createBoard':
                await store.createBoard({ name: action.name, description: null })
                break
              case 'deleteBoard':
                if (store.boards.length > 0) {
                  const idx = action.index % store.boards.length
                  await store.deleteBoard(store.boards[idx].id)
                }
                break
            }
          } catch {
            // Actions may fail — that's expected
          }
        }

        // Invariants that must hold after any action sequence
        expect(Array.isArray(store.boards)).toBe(true)
        expect(typeof store.loading).toBe('boolean')
        for (const board of store.boards) {
          expect(board.id).toBeTruthy()
          expect(typeof board.name).toBe('string')
        }
      }),
      { numRuns: 50 },
    )
  })
})

describe('Capture Store Resilience: API Error Handling', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('fetchItems handles network errors and sets error state', async () => {
    vi.mocked(captureApi.listItems).mockRejectedValue(new Error('Network error'))

    const store = useCaptureStore()
    // Store re-throws the error after setting error state
    try {
      await store.fetchItems()
    } catch {
      // Expected
    }
    expect(store.listError).toBeTruthy()
  })

  it('createItem handles rejection with adversarial content', async () => {
    const adversarialContent = [
      "<script>alert('xss')</script>",
      "'; DROP TABLE capture_items; --",
      '\u0000',
      'a'.repeat(100_000),
    ]

    for (const content of adversarialContent) {
      setActivePinia(createPinia())
      vi.mocked(captureApi.createItem).mockRejectedValue(new Error('Validation error'))

      const store = useCaptureStore()
      try {
        await store.createItem({ text: content })
      } catch {
        // Expected: store re-throws
      }
      expect(store.actionError).toBeTruthy()
    }
  })
})
