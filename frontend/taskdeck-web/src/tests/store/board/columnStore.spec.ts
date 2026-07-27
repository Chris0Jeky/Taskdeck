import { describe, expect, it, vi, beforeEach } from 'vitest'
import { ref } from 'vue'

const { mockColumnsApi } = vi.hoisted(() => ({
  mockColumnsApi: {
    createColumn: vi.fn(),
    updateColumn: vi.fn(),
    deleteColumn: vi.fn(),
    reorderColumns: vi.fn(),
  },
}))

vi.mock('../../../api/columnsApi', () => ({
  columnsApi: mockColumnsApi,
}))

import { createColumnActions } from '../../../store/board/columnStore'

function createMockState() {
  return {
    currentBoard: ref<{ id: string; columns: Array<{ id: string; name: string }> } | null>({
      id: 'board-1',
      columns: [
        { id: 'col-1', name: 'Todo' },
        { id: 'col-2', name: 'Done' },
      ],
    }),
    currentBoardCards: ref([
      { id: 'card-1', columnId: 'col-1' },
      { id: 'card-2', columnId: 'col-2' },
      { id: 'card-3', columnId: 'col-1' },
    ]),
    loading: ref(false),
    error: ref<string | null>(null),
  }
}

function createMockHelpers() {
  return {
    guardDemoMutation: vi.fn(),
    handleApiError: vi.fn(),
    toast: { success: vi.fn(), error: vi.fn() },
  }
}

describe('columnStore', () => {
  let state: ReturnType<typeof createMockState>
  let helpers: ReturnType<typeof createMockHelpers>

  beforeEach(() => {
    vi.clearAllMocks()
    state = createMockState()
    helpers = createMockHelpers()
  })

  describe('createColumn', () => {
    it('creates column and appends to current board', async () => {
      const newCol = { id: 'col-3', name: 'In Progress' }
      mockColumnsApi.createColumn.mockResolvedValueOnce(newCol)
      const { createColumn } = createColumnActions(state as any, helpers as any)
      const result = await createColumn('board-1', { name: 'In Progress' } as any)
      expect(result).toEqual(newCol)
      expect(state.currentBoard.value!.columns).toHaveLength(3)
      expect(state.currentBoard.value!.columns[2]).toEqual(newCol)
      expect(helpers.toast.success).toHaveBeenCalled()
      expect(state.loading.value).toBe(false)
    })

    it('preserves a fresher realtime column when the API response resolves later', async () => {
      const apiColumn = {
        id: 'col-3',
        boardId: 'board-1',
        name: 'In Progress',
        position: 2,
        wipLimit: null,
        cardCount: 0,
        createdAt: '2026-07-27T10:00:00Z',
        updatedAt: '2026-07-27T10:00:00Z',
      }
      const realtimeColumn = {
        ...apiColumn,
        name: 'In Progress (realtime)',
        position: 4,
        wipLimit: 3,
        cardCount: 2,
        updatedAt: '2026-07-27T10:00:01Z',
      }
      let resolveCreate!: (column: typeof apiColumn) => void
      mockColumnsApi.createColumn.mockReturnValueOnce(
        new Promise<typeof apiColumn>((resolve) => {
          resolveCreate = resolve
        }),
      )
      state.error.value = 'Previous error'
      const { createColumn } = createColumnActions(state as any, helpers as any)

      const createPromise = createColumn('board-1', { name: 'In Progress' } as any)

      expect(state.loading.value).toBe(true)
      expect(state.error.value).toBeNull()

      state.currentBoard.value!.columns.push(realtimeColumn)
      const installedRealtimeColumn = state.currentBoard.value!.columns[2]
      resolveCreate(apiColumn)

      await expect(createPromise).resolves.toEqual(apiColumn)
      const matchingColumns = state.currentBoard.value!.columns.filter(
        (existingColumn) => existingColumn.id === apiColumn.id,
      )
      expect(matchingColumns).toHaveLength(1)
      const preservedColumn = matchingColumns[0] as typeof realtimeColumn
      expect(preservedColumn).toBe(installedRealtimeColumn)
      expect(preservedColumn.name).toBe(realtimeColumn.name)
      expect(preservedColumn.position).toBe(realtimeColumn.position)
      expect(preservedColumn.wipLimit).toBe(realtimeColumn.wipLimit)
      expect(preservedColumn.cardCount).toBe(realtimeColumn.cardCount)
      expect(preservedColumn.updatedAt).toBe(realtimeColumn.updatedAt)
      expect(state.currentBoard.value!.columns).toHaveLength(3)
      expect(helpers.toast.success).toHaveBeenCalledOnce()
      expect(helpers.toast.success).toHaveBeenCalledWith(
        'Column "In Progress" created successfully',
      )
      expect(helpers.toast.error).not.toHaveBeenCalled()
      expect(helpers.handleApiError).not.toHaveBeenCalled()
      expect(state.error.value).toBeNull()
      expect(state.loading.value).toBe(false)
    })

    it('does not modify board if boardId does not match', async () => {
      const newCol = { id: 'col-3', name: 'X' }
      mockColumnsApi.createColumn.mockResolvedValueOnce(newCol)
      const { createColumn } = createColumnActions(state as any, helpers as any)
      await createColumn('other-board', { name: 'X' } as any)
      expect(state.currentBoard.value!.columns).toHaveLength(2)
    })

    it('calls handleApiError and rethrows on failure', async () => {
      mockColumnsApi.createColumn.mockRejectedValueOnce(new Error('fail'))
      const { createColumn } = createColumnActions(state as any, helpers as any)
      await expect(createColumn('board-1', { name: 'X' } as any)).rejects.toThrow('fail')
      expect(helpers.handleApiError).toHaveBeenCalledWith(expect.any(Error), 'Failed to create column')
      expect(state.loading.value).toBe(false)
    })

    it('guards demo mutation', async () => {
      helpers.guardDemoMutation.mockImplementation(() => { throw new Error('demo') })
      const { createColumn } = createColumnActions(state as any, helpers as any)
      await expect(createColumn('board-1', { name: 'X' } as any)).rejects.toThrow('demo')
    })
  })

  describe('updateColumn', () => {
    it('updates column in current board', async () => {
      const updated = { id: 'col-1', name: 'Backlog' }
      mockColumnsApi.updateColumn.mockResolvedValueOnce(updated)
      const { updateColumn } = createColumnActions(state as any, helpers as any)
      const result = await updateColumn('board-1', 'col-1', { name: 'Backlog' } as any)
      expect(result).toEqual(updated)
      expect(state.currentBoard.value!.columns[0]).toEqual(updated)
      expect(helpers.toast.success).toHaveBeenCalled()
    })

    it('does not modify if column not found', async () => {
      const updated = { id: 'col-99', name: 'Ghost' }
      mockColumnsApi.updateColumn.mockResolvedValueOnce(updated)
      const { updateColumn } = createColumnActions(state as any, helpers as any)
      await updateColumn('board-1', 'col-99', { name: 'Ghost' } as any)
      expect(state.currentBoard.value!.columns).toHaveLength(2)
      expect(state.currentBoard.value!.columns[0].name).toBe('Todo')
    })

    it('calls handleApiError on failure', async () => {
      mockColumnsApi.updateColumn.mockRejectedValueOnce(new Error('err'))
      const { updateColumn } = createColumnActions(state as any, helpers as any)
      await expect(updateColumn('board-1', 'col-1', {} as any)).rejects.toThrow('err')
      expect(helpers.handleApiError).toHaveBeenCalled()
    })
  })

  describe('deleteColumn', () => {
    it('removes column and its cards', async () => {
      mockColumnsApi.deleteColumn.mockResolvedValueOnce(undefined)
      const { deleteColumn } = createColumnActions(state as any, helpers as any)
      await deleteColumn('board-1', 'col-1')
      expect(state.currentBoard.value!.columns).toHaveLength(1)
      expect(state.currentBoard.value!.columns[0].id).toBe('col-2')
      expect(state.currentBoardCards.value).toHaveLength(1)
      expect(state.currentBoardCards.value[0].id).toBe('card-2')
      expect(helpers.toast.success).toHaveBeenCalled()
    })

    it('calls handleApiError on failure', async () => {
      mockColumnsApi.deleteColumn.mockRejectedValueOnce(new Error('del'))
      const { deleteColumn } = createColumnActions(state as any, helpers as any)
      await expect(deleteColumn('board-1', 'col-1')).rejects.toThrow('del')
      expect(helpers.handleApiError).toHaveBeenCalled()
    })
  })

  describe('reorderColumns', () => {
    it('replaces columns with reordered list', async () => {
      const reordered = [
        { id: 'col-2', name: 'Done' },
        { id: 'col-1', name: 'Todo' },
      ]
      mockColumnsApi.reorderColumns.mockResolvedValueOnce(reordered)
      const { reorderColumns } = createColumnActions(state as any, helpers as any)
      const result = await reorderColumns('board-1', ['col-2', 'col-1'])
      expect(result).toEqual(reordered)
      expect(state.currentBoard.value!.columns).toEqual(reordered)
      expect(helpers.toast.success).toHaveBeenCalled()
    })

    it('does not modify when boardId mismatch', async () => {
      const reordered = [{ id: 'col-2', name: 'Done' }]
      mockColumnsApi.reorderColumns.mockResolvedValueOnce(reordered)
      const { reorderColumns } = createColumnActions(state as any, helpers as any)
      await reorderColumns('other-board', ['col-2'])
      expect(state.currentBoard.value!.columns).toHaveLength(2)
    })

    it('calls handleApiError on failure', async () => {
      mockColumnsApi.reorderColumns.mockRejectedValueOnce(new Error('reorder'))
      const { reorderColumns } = createColumnActions(state as any, helpers as any)
      await expect(reorderColumns('board-1', [])).rejects.toThrow('reorder')
      expect(helpers.handleApiError).toHaveBeenCalled()
    })
  })
})
