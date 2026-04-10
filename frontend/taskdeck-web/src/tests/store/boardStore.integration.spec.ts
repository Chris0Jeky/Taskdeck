/**
 * boardStore integration tests — store + real API module, HTTP layer mocked.
 *
 * These tests exercise the full store → boardsApi/cardsApi → http chain.
 * Mocking http (not the API modules) means any mismatch between API response
 * shapes and what the store expects will be caught here.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import http from '../../api/http'
import { useBoardStore } from '../../store/boardStore'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({ error: vi.fn(), success: vi.fn(), warning: vi.fn(), info: vi.fn() }),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: false }
})

function makeBoardPayload(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: 'board-1',
    name: 'My Board',
    description: 'desc',
    isArchived: false,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

function makeCardPayload(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: 'card-1',
    boardId: 'board-1',
    columnId: 'col-1',
    title: 'Task',
    description: '',
    position: 0,
    dueDate: null,
    isBlocked: false,
    blockReason: null,
    labels: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('boardStore — integration (real API module, mocked HTTP)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  // ── fetchBoards ───────────────────────────────────────────────────────────

  describe('fetchBoards', () => {
    it('populates boards from the API response shape', async () => {
      const boards = [makeBoardPayload(), makeBoardPayload({ id: 'board-2', name: 'Board 2' })]
      vi.mocked(http.get).mockResolvedValue({ data: boards })

      const store = useBoardStore()
      await store.fetchBoards()

      expect(store.boards).toHaveLength(2)
      expect(store.boards[0].id).toBe('board-1')
      expect(store.boards[1].id).toBe('board-2')
      // loading must be cleared after success
      expect(store.loading).toBe(false)
      expect(store.error).toBeNull()
    })

    it('calls GET /boards with the correct URL structure', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      const store = useBoardStore()
      await store.fetchBoards()

      const calledUrl: string = vi.mocked(http.get).mock.calls[0][0] as string
      expect(calledUrl).toMatch(/^\/boards/)
    })

    it('sets error state when the API returns a network failure', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('Network Error'))

      const store = useBoardStore()
      await expect(store.fetchBoards()).rejects.toThrow('Network Error')

      expect(store.error).toBe('Network Error')
      expect(store.loading).toBe(false)
    })

    it('preserves activeBoardId when the selected board is still present after a refresh', async () => {
      vi.useFakeTimers()
      vi.mocked(http.get).mockResolvedValue({ data: [makeBoardPayload()] })

      const store = useBoardStore()
      await store.fetchBoards()
      expect(store.activeBoardId).toBe('board-1')

      // Advance past the throttle window so the next call is not suppressed
      vi.advanceTimersByTime(6_000)

      vi.mocked(http.get).mockResolvedValue({ data: [makeBoardPayload()] })
      await store.fetchBoards()

      expect(store.activeBoardId).toBe('board-1')
    })

    it('does not switch activeBoardId when a second board is created (regression #509)', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [makeBoardPayload()] })

      const store = useBoardStore()
      await store.fetchBoards()
      expect(store.activeBoardId).toBe('board-1')

      // Create a second board — activeBoardId must not flip to the new one
      vi.mocked(http.post).mockResolvedValue({ data: makeBoardPayload({ id: 'board-2', name: 'Board 2' }) })
      await store.createBoard({ name: 'Board 2' })

      expect(store.activeBoardId).toBe('board-1')
    })
  })

  // ── createBoard ───────────────────────────────────────────────────────────

  describe('createBoard', () => {
    it('sends the DTO to POST /boards and appends the returned board', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })
      const store = useBoardStore()
      await store.fetchBoards()

      const newBoard = makeBoardPayload({ id: 'board-new', name: 'Fresh Board' })
      vi.mocked(http.post).mockResolvedValue({ data: newBoard })

      const result = await store.createBoard({ name: 'Fresh Board', description: 'test' })

      expect(result.id).toBe('board-new')
      expect(store.boards.some(b => b.id === 'board-new')).toBe(true)
      expect(http.post).toHaveBeenCalledWith('/boards', expect.objectContaining({ name: 'Fresh Board' }))
    })

    it('does not add a phantom board when the API rejects with 400', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })
      const store = useBoardStore()
      await store.fetchBoards()

      vi.mocked(http.post).mockRejectedValue({ response: { status: 400, data: { message: 'Bad request' } } })

      await expect(store.createBoard({ name: '' })).rejects.toBeDefined()
      expect(store.boards).toHaveLength(0)
    })
  })

  // ── updateBoard ───────────────────────────────────────────────────────────

  describe('updateBoard', () => {
    it('sends PUT /boards/:id and replaces the local record with the API response', async () => {
      const original = makeBoardPayload()
      vi.mocked(http.get).mockResolvedValue({ data: [original] })

      const store = useBoardStore()
      await store.fetchBoards()
      store.currentBoard = { ...original, columns: [] }

      const updated = makeBoardPayload({ name: 'Renamed', updatedAt: '2026-02-01T00:00:00Z' })
      vi.mocked(http.put).mockResolvedValue({ data: updated })

      await store.updateBoard('board-1', { name: 'Renamed', description: null, isArchived: null })

      expect(store.boards[0].name).toBe('Renamed')
      expect(store.currentBoard?.name).toBe('Renamed')
      expect(http.put).toHaveBeenCalledWith('/boards/board-1', expect.any(Object))
    })

    it('does not corrupt board name in the list when PUT /boards/:id fails', async () => {
      const original = makeBoardPayload()
      vi.mocked(http.get).mockResolvedValue({ data: [original] })

      const store = useBoardStore()
      await store.fetchBoards()

      vi.mocked(http.put).mockRejectedValue({ response: { status: 409, data: { message: 'Conflict' } } })
      await expect(store.updateBoard('board-1', { name: 'Will fail', description: null, isArchived: null })).rejects.toBeDefined()

      // The list must retain the original name (no optimistic mutation that isn't rolled back)
      expect(store.boards[0].name).toBe('My Board')
    })
  })

  // ── deleteBoard ───────────────────────────────────────────────────────────

  describe('deleteBoard', () => {
    it('removes the deleted board from the list and clears currentBoard', async () => {
      const boards = [makeBoardPayload(), makeBoardPayload({ id: 'board-2', name: 'B2' })]
      vi.mocked(http.get).mockResolvedValue({ data: boards })

      const store = useBoardStore()
      await store.fetchBoards()
      store.currentBoard = { ...boards[0], columns: [] }

      vi.mocked(http.delete).mockResolvedValue({ data: undefined })
      await store.deleteBoard('board-1')

      expect(store.boards.some(b => b.id === 'board-1')).toBe(false)
      expect(store.currentBoard).toBeNull()
    })

    it('falls back activeBoardId to remaining board after deletion', async () => {
      const boards = [makeBoardPayload(), makeBoardPayload({ id: 'board-2', name: 'B2' })]
      vi.mocked(http.get).mockResolvedValue({ data: boards })

      const store = useBoardStore()
      await store.fetchBoards()
      store.activeBoardId = 'board-1'

      vi.mocked(http.delete).mockResolvedValue({ data: undefined })
      await store.deleteBoard('board-1')

      expect(store.activeBoardId).toBe('board-2')
    })
  })

  // ── card CRUD ─────────────────────────────────────────────────────────────

  describe('createCard', () => {
    it('posts to /boards/:id/cards and appends the returned card', async () => {
      const store = useBoardStore()
      const card = makeCardPayload()
      vi.mocked(http.post).mockResolvedValue({ data: card })

      const result = await store.createCard('board-1', { columnId: 'col-1', title: 'Task', description: '' })

      expect(result.id).toBe('card-1')
      expect(store.currentBoardCards).toContainEqual(card)
      expect(http.post).toHaveBeenCalledWith(
        expect.stringContaining('/boards/board-1/cards'),
        expect.any(Object),
      )
    })

    it('does not append a phantom card when creation fails with 422', async () => {
      const store = useBoardStore()
      vi.mocked(http.post).mockRejectedValue({ response: { status: 422, data: { message: 'Validation failed' } } })

      await expect(store.createCard('board-1', { columnId: 'col-1', title: '', description: '' })).rejects.toBeDefined()
      expect(store.currentBoardCards).toHaveLength(0)
    })
  })

  describe('moveCard', () => {
    it('posts to move endpoint and updates card column and position in local state', async () => {
      const store = useBoardStore()
      const card = makeCardPayload({ columnId: 'col-1', position: 0 })
      store.currentBoardCards = [card]

      const moved = makeCardPayload({ columnId: 'col-2', position: 1 })
      // moveCard uses http.post (not put)
      vi.mocked(http.post).mockResolvedValue({ data: moved })

      await store.moveCard('board-1', 'card-1', 'col-2', 1)

      const stored = store.currentBoardCards.find(c => c.id === 'card-1')
      expect(stored?.columnId).toBe('col-2')
      expect(stored?.position).toBe(1)
      expect(http.post).toHaveBeenCalledWith(
        '/boards/board-1/cards/card-1/move',
        expect.objectContaining({ targetColumnId: 'col-2', targetPosition: 1 }),
      )
    })

    it('surfaces error state and clears loading when the move API fails', async () => {
      const store = useBoardStore()
      const card = makeCardPayload({ columnId: 'col-1', position: 0 })
      store.currentBoardCards = [card]

      vi.mocked(http.post).mockRejectedValue({ response: { status: 409, data: { message: 'Conflict' } } })

      await expect(store.moveCard('board-1', 'card-1', 'col-2', 1)).rejects.toBeDefined()

      expect(store.loading).toBe(false)
    })

    it('retains original column and position when move API rejects with 409 (snap-back)', async () => {
      const store = useBoardStore()
      const card = makeCardPayload({ id: 'card-snap', columnId: 'col-1', position: 0 })
      store.currentBoardCards = [card]

      vi.mocked(http.post).mockRejectedValue({ response: { status: 409, data: { message: 'Stale position' } } })

      await expect(store.moveCard('board-1', 'card-snap', 'col-2', 3)).rejects.toBeDefined()

      // The card should still be in the store (not removed) and in the original column
      const storedCard = store.currentBoardCards.find(c => c.id === 'card-snap')
      expect(storedCard).toBeDefined()
      expect(storedCard?.columnId).toBe('col-1')
      expect(storedCard?.position).toBe(0)
    })
  })

  describe('updateCard', () => {
    it('sends PATCH /boards/:id/cards/:id and updates the local card record', async () => {
      const store = useBoardStore()
      const original = makeCardPayload({ title: 'Original' })
      store.currentBoardCards = [original]

      const updated = makeCardPayload({ title: 'Updated', updatedAt: '2026-03-01T00:00:00Z' })
      vi.mocked(http.patch).mockResolvedValue({ data: updated })

      await store.updateCard('board-1', 'card-1', {
        title: 'Updated',
        description: null,
        dueDate: null,
        isBlocked: null,
        blockReason: null,
        labelIds: null,
      })

      expect(store.currentBoardCards[0].title).toBe('Updated')
      expect(http.patch).toHaveBeenCalledWith(
        '/boards/board-1/cards/card-1',
        expect.any(Object),
      )
    })
  })

  // ── deleteCard ─────────────────────────────────────────────────────────

  describe('deleteCard', () => {
    it('calls DELETE /boards/:id/cards/:id and removes the card from local state', async () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        makeCardPayload({ id: 'card-del', columnId: 'col-1' }),
        makeCardPayload({ id: 'card-keep', columnId: 'col-1' }),
      ]

      vi.mocked(http.delete).mockResolvedValue({ data: undefined })
      await store.deleteCard('board-1', 'card-del')

      expect(store.currentBoardCards.some(c => c.id === 'card-del')).toBe(false)
      expect(store.currentBoardCards.some(c => c.id === 'card-keep')).toBe(true)
      expect(http.delete).toHaveBeenCalledWith(expect.stringContaining('/boards/board-1/cards/card-del'))
    })

    it('does not remove a card when DELETE fails', async () => {
      const store = useBoardStore()
      store.currentBoardCards = [makeCardPayload({ id: 'card-fail' })]

      vi.mocked(http.delete).mockRejectedValue({ response: { status: 500, data: { message: 'Server error' } } })
      await expect(store.deleteCard('board-1', 'card-fail')).rejects.toBeDefined()

      expect(store.currentBoardCards.some(c => c.id === 'card-fail')).toBe(true)
    })
  })

  // ── column CRUD ───────────────────────────────────────────────────────────

  describe('createColumn', () => {
    it('posts to /boards/:id/columns and appends the returned column to currentBoard', async () => {
      const store = useBoardStore()
      store.currentBoard = {
        id: 'board-1',
        name: 'My Board',
        description: '',
        isArchived: false,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        columns: [],
      }

      const newColumn = { id: 'col-new', name: 'Done', position: 0, wipLimit: null, cardCount: 0 }
      vi.mocked(http.post).mockResolvedValue({ data: newColumn })

      await store.createColumn('board-1', { name: 'Done', position: 0 })

      expect(store.currentBoard?.columns).toHaveLength(1)
      expect(store.currentBoard?.columns[0].name).toBe('Done')
      expect(http.post).toHaveBeenCalledWith(
        expect.stringContaining('/boards/board-1/columns'),
        expect.objectContaining({ name: 'Done' }),
      )
    })
  })

  describe('deleteColumn', () => {
    it('removes the column and its cards from local state', async () => {
      const store = useBoardStore()
      store.currentBoard = {
        id: 'board-1',
        name: 'My Board',
        description: '',
        isArchived: false,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        columns: [
          { id: 'col-1', name: 'Todo', position: 0, wipLimit: null, cardCount: 2 },
          { id: 'col-2', name: 'Done', position: 1, wipLimit: null, cardCount: 0 },
        ],
      }
      store.currentBoardCards = [
        makeCardPayload({ id: 'card-a', columnId: 'col-1' }),
        makeCardPayload({ id: 'card-b', columnId: 'col-1' }),
        makeCardPayload({ id: 'card-c', columnId: 'col-2' }),
      ]

      vi.mocked(http.delete).mockResolvedValue({ data: undefined })
      await store.deleteColumn('board-1', 'col-1')

      // Column removed
      expect(store.currentBoard?.columns).toHaveLength(1)
      expect(store.currentBoard?.columns[0].id).toBe('col-2')
      // Cards in deleted column removed
      expect(store.currentBoardCards).toHaveLength(1)
      expect(store.currentBoardCards[0].id).toBe('card-c')
    })
  })

  // ── cardsByColumn getter ───────────────────────────────────────────────────

  describe('cardsByColumn getter', () => {
    it('groups loaded cards by column and sorts them by position', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        makeCardPayload({ id: 'c-a', columnId: 'col-1', position: 2 }),
        makeCardPayload({ id: 'c-b', columnId: 'col-1', position: 0 }),
        makeCardPayload({ id: 'c-c', columnId: 'col-2', position: 0 }),
      ]

      const grouped = store.cardsByColumn
      const col1 = grouped.get('col-1')!
      const col2 = grouped.get('col-2')!

      expect(col1[0].id).toBe('c-b')
      expect(col1[1].id).toBe('c-a')
      expect(col2[0].id).toBe('c-c')
    })
  })
})
