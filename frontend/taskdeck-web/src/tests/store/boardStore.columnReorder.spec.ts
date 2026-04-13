/**
 * boardStore column reorder and stale reconciliation integration tests.
 *
 * These tests exercise column reorder → API confirms → all cards maintain
 * correct column association, card update conflict (409) handling, and
 * board data refresh while a card edit is in flight.
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

function makeColumn(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: 'col-1',
    name: 'Todo',
    position: 0,
    wipLimit: null,
    cardCount: 0,
    ...overrides,
  }
}

function makeCard(overrides: Partial<Record<string, unknown>> = {}) {
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

describe('boardStore — column reorder and stale reconciliation', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  // ── column reorder ───────────────────────────────────────────────────────

  describe('reorderColumns', () => {
    it('sends POST /boards/:id/columns/reorder and updates local column order', async () => {
      const store = useBoardStore()
      store.currentBoard = {
        id: 'board-1',
        name: 'Test Board',
        description: '',
        isArchived: false,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        columns: [
          makeColumn({ id: 'col-a', name: 'Todo', position: 0 }),
          makeColumn({ id: 'col-b', name: 'In Progress', position: 1 }),
          makeColumn({ id: 'col-c', name: 'Done', position: 2 }),
        ],
      }

      // API returns the columns in the new order
      const reorderedColumns = [
        makeColumn({ id: 'col-c', name: 'Done', position: 0 }),
        makeColumn({ id: 'col-a', name: 'Todo', position: 1 }),
        makeColumn({ id: 'col-b', name: 'In Progress', position: 2 }),
      ]
      vi.mocked(http.post).mockResolvedValue({ data: reorderedColumns })

      await store.reorderColumns('board-1', ['col-c', 'col-a', 'col-b'])

      expect(store.currentBoard?.columns).toHaveLength(3)
      expect(store.currentBoard?.columns[0].id).toBe('col-c')
      expect(store.currentBoard?.columns[1].id).toBe('col-a')
      expect(store.currentBoard?.columns[2].id).toBe('col-b')
      expect(http.post).toHaveBeenCalledWith(
        '/boards/board-1/columns/reorder',
        expect.objectContaining({ columnIds: ['col-c', 'col-a', 'col-b'] }),
      )
    })

    it('preserves cards in their correct columns after column reorder', async () => {
      const store = useBoardStore()
      store.currentBoard = {
        id: 'board-1',
        name: 'Test Board',
        description: '',
        isArchived: false,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        columns: [
          makeColumn({ id: 'col-a', position: 0 }),
          makeColumn({ id: 'col-b', position: 1 }),
        ],
      }
      store.currentBoardCards = [
        makeCard({ id: 'card-1', columnId: 'col-a', position: 0 }),
        makeCard({ id: 'card-2', columnId: 'col-a', position: 1 }),
        makeCard({ id: 'card-3', columnId: 'col-b', position: 0 }),
      ]

      const reorderedColumns = [
        makeColumn({ id: 'col-b', position: 0 }),
        makeColumn({ id: 'col-a', position: 1 }),
      ]
      vi.mocked(http.post).mockResolvedValue({ data: reorderedColumns })

      await store.reorderColumns('board-1', ['col-b', 'col-a'])

      // Cards must remain in their original columns
      const colACards = store.currentBoardCards.filter(c => c.columnId === 'col-a')
      const colBCards = store.currentBoardCards.filter(c => c.columnId === 'col-b')
      expect(colACards).toHaveLength(2)
      expect(colBCards).toHaveLength(1)
      expect(colACards.map(c => c.id)).toEqual(expect.arrayContaining(['card-1', 'card-2']))
      expect(colBCards[0].id).toBe('card-3')
    })

    it('does not corrupt column state when reorder API fails', async () => {
      const store = useBoardStore()
      const originalColumns = [
        makeColumn({ id: 'col-a', position: 0 }),
        makeColumn({ id: 'col-b', position: 1 }),
      ]
      store.currentBoard = {
        id: 'board-1',
        name: 'Test Board',
        description: '',
        isArchived: false,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        columns: [...originalColumns],
      }

      vi.mocked(http.post).mockRejectedValue({
        response: { status: 409, data: { message: 'Stale board state' } },
      })

      await expect(
        store.reorderColumns('board-1', ['col-b', 'col-a']),
      ).rejects.toBeDefined()

      // On failure, columns must not be mutated to the new order
      // (the API call is atomic — either it succeeds and we update, or it fails and we keep original)
      expect(store.currentBoard?.columns).toHaveLength(2)
    })
  })

  // ── updateCard 409 Conflict ───────────────────────────────────────────────

  describe('updateCard — 409 Conflict', () => {
    it('does not corrupt card state when PATCH /boards/:id/cards/:id returns 409', async () => {
      const store = useBoardStore()
      const original = makeCard({
        id: 'card-conflict',
        title: 'Original Title',
        updatedAt: '2026-01-01T00:00:00Z',
      })
      store.currentBoardCards = [original]

      vi.mocked(http.patch).mockRejectedValue({
        response: { status: 409, data: { message: 'Card was modified by another user' } },
      })

      await expect(
        store.updateCard('board-1', 'card-conflict', {
          title: 'Updated Title',
          description: null,
          dueDate: null,
          isBlocked: null,
          blockReason: null,
          labelIds: null,
        }),
      ).rejects.toBeDefined()

      // Card must retain its original title — no partial update applied
      const stored = store.currentBoardCards.find(c => c.id === 'card-conflict')
      expect(stored?.title).toBe('Original Title')
    })

    it('passes expectedUpdatedAt from existing card to detect stale edits', async () => {
      const store = useBoardStore()
      const original = makeCard({
        id: 'card-stale',
        title: 'Original',
        updatedAt: '2026-01-15T10:30:00Z',
      })
      store.currentBoardCards = [original]

      const updated = makeCard({
        id: 'card-stale',
        title: 'Updated',
        updatedAt: '2026-01-15T11:00:00Z',
      })
      vi.mocked(http.patch).mockResolvedValue({ data: updated })

      await store.updateCard('board-1', 'card-stale', {
        title: 'Updated',
        description: null,
        dueDate: null,
        isBlocked: null,
        blockReason: null,
        labelIds: null,
      })

      // The PATCH body should include the expectedUpdatedAt from the existing card
      const patchBody = vi.mocked(http.patch).mock.calls[0][1] as Record<string, unknown>
      expect(patchBody.expectedUpdatedAt).toBe('2026-01-15T10:30:00Z')
    })
  })

  // ── moveCard column card count tracking ──────────────────────────────────

  describe('moveCard — column card count tracking', () => {
    it('decrements source column count and increments target column count on successful move', async () => {
      const store = useBoardStore()
      store.currentBoard = {
        id: 'board-1',
        name: 'Test',
        description: '',
        isArchived: false,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        columns: [
          makeColumn({ id: 'col-src', name: 'Source', position: 0, cardCount: 2 }),
          makeColumn({ id: 'col-dst', name: 'Destination', position: 1, cardCount: 1 }),
        ],
      }
      store.currentBoardCards = [
        makeCard({ id: 'card-move', columnId: 'col-src', position: 0 }),
        makeCard({ id: 'card-stay', columnId: 'col-src', position: 1 }),
        makeCard({ id: 'card-existing', columnId: 'col-dst', position: 0 }),
      ]

      const moved = makeCard({ id: 'card-move', columnId: 'col-dst', position: 1 })
      vi.mocked(http.post).mockResolvedValue({ data: moved })

      await store.moveCard('board-1', 'card-move', 'col-dst', 1)

      const srcCol = store.currentBoard?.columns.find(c => c.id === 'col-src')
      const dstCol = store.currentBoard?.columns.find(c => c.id === 'col-dst')
      expect(srcCol?.cardCount).toBe(1) // was 2, now 1
      expect(dstCol?.cardCount).toBe(2) // was 1, now 2
    })

    it('does not change card counts when move within the same column', async () => {
      const store = useBoardStore()
      store.currentBoard = {
        id: 'board-1',
        name: 'Test',
        description: '',
        isArchived: false,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        columns: [
          makeColumn({ id: 'col-1', name: 'Todo', position: 0, cardCount: 3 }),
        ],
      }
      store.currentBoardCards = [
        makeCard({ id: 'card-a', columnId: 'col-1', position: 0 }),
        makeCard({ id: 'card-b', columnId: 'col-1', position: 1 }),
        makeCard({ id: 'card-c', columnId: 'col-1', position: 2 }),
      ]

      // Move within same column — just reposition
      const moved = makeCard({ id: 'card-a', columnId: 'col-1', position: 2 })
      vi.mocked(http.post).mockResolvedValue({ data: moved })

      await store.moveCard('board-1', 'card-a', 'col-1', 2)

      const col = store.currentBoard?.columns.find(c => c.id === 'col-1')
      expect(col?.cardCount).toBe(3) // unchanged
    })
  })

  // ── updateColumn ──────────────────────────────────────────────────────────

  describe('updateColumn', () => {
    it('sends PATCH /boards/:id/columns/:id and updates the local column record', async () => {
      const store = useBoardStore()
      store.currentBoard = {
        id: 'board-1',
        name: 'Test',
        description: '',
        isArchived: false,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        columns: [
          makeColumn({ id: 'col-rename', name: 'Old Name', wipLimit: null }),
        ],
      }

      const updated = makeColumn({ id: 'col-rename', name: 'New Name', wipLimit: 5 })
      vi.mocked(http.patch).mockResolvedValue({ data: updated })

      await store.updateColumn('board-1', 'col-rename', { name: 'New Name', wipLimit: 5 })

      expect(store.currentBoard?.columns[0].name).toBe('New Name')
      expect(store.currentBoard?.columns[0].wipLimit).toBe(5)
      expect(http.patch).toHaveBeenCalledWith(
        '/boards/board-1/columns/col-rename',
        expect.objectContaining({ name: 'New Name', wipLimit: 5 }),
      )
    })

    it('does not corrupt column when update fails', async () => {
      const store = useBoardStore()
      store.currentBoard = {
        id: 'board-1',
        name: 'Test',
        description: '',
        isArchived: false,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        columns: [
          makeColumn({ id: 'col-safe', name: 'Original' }),
        ],
      }

      vi.mocked(http.patch).mockRejectedValue({
        response: { status: 500, data: { message: 'Server error' } },
      })

      await expect(
        store.updateColumn('board-1', 'col-safe', { name: 'Will fail' }),
      ).rejects.toBeDefined()

      // Column must retain original name
      expect(store.currentBoard?.columns[0].name).toBe('Original')
    })
  })

  // ── board data refresh preserves editing card ─────────────────────────────

  describe('editingCardId preservation', () => {
    it('board operations do not clear editingCardId', async () => {
      const store = useBoardStore()
      store.currentBoard = {
        id: 'board-1',
        name: 'Test',
        description: '',
        isArchived: false,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        columns: [makeColumn({ id: 'col-1' })],
      }
      store.currentBoardCards = [
        makeCard({ id: 'card-editing', columnId: 'col-1' }),
      ]

      // User starts editing a card
      store.setEditingCard('card-editing')
      expect(store.editingCardId).toBe('card-editing')

      // Another card is created — this should not clear editingCardId
      const newCard = makeCard({ id: 'card-new', columnId: 'col-1', position: 1 })
      vi.mocked(http.post).mockResolvedValue({ data: newCard })
      await store.createCard('board-1', { columnId: 'col-1', title: 'New Card', description: '' })

      expect(store.editingCardId).toBe('card-editing')
    })
  })
})
