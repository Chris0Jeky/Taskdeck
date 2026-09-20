import { describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

const { mockColumnsApi } = vi.hoisted(() => ({
  mockColumnsApi: {
    getColumns: vi.fn(),
    createColumn: vi.fn(),
    updateColumn: vi.fn(),
    deleteColumn: vi.fn(),
    reorderColumns: vi.fn(),
  },
}))

vi.mock('../../../api/columnsApi', () => ({ columnsApi: mockColumnsApi }))

import { createColumnActions } from '../../../store/board/columnStore'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((settle) => {
    resolve = settle
  })
  return { promise, resolve }
}

describe('columnStore stale-visit delete reconciliation', () => {
  it('removes cards for the deleted column after authoritative A to B to A reconciliation', async () => {
    const deleteResponse = deferred<void>()
    const reconcileResponse = deferred<Array<Record<string, unknown>>>()
    mockColumnsApi.deleteColumn.mockReturnValueOnce(deleteResponse.promise)
    mockColumnsApi.getColumns.mockReturnValueOnce(reconcileResponse.promise)

    const deletedColumn = { id: 'col-a', boardId: 'board-1', name: 'Todo' }
    const survivingColumn = { id: 'col-b', boardId: 'board-1', name: 'Done' }
    const state = {
      currentBoard: ref({
        id: 'board-1',
        columns: [deletedColumn, survivingColumn],
      }),
      currentBoardCards: ref([
        { id: 'card-deleted', boardId: 'board-1', columnId: 'col-a' },
        { id: 'card-surviving', boardId: 'board-1', columnId: 'col-b' },
      ]),
      loading: ref(false),
      error: ref<string | null>(null),
    }
    const helpers = {
      guardDemoMutation: vi.fn(),
      handleApiError: vi.fn(),
      toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
      markBoardDetailMutation: vi.fn(),
    }
    const { deleteColumn } = createColumnActions(state as never, helpers as never)

    const pendingDelete = deleteColumn('board-1', 'col-a')

    state.currentBoard.value = { id: 'board-2', columns: [] }
    state.currentBoardCards.value = []
    state.currentBoard.value = {
      id: 'board-1',
      columns: [deletedColumn, survivingColumn],
    }
    state.currentBoardCards.value = [
      { id: 'card-deleted', boardId: 'board-1', columnId: 'col-a' },
      { id: 'card-surviving', boardId: 'board-1', columnId: 'col-b' },
    ]

    deleteResponse.resolve(undefined)
    await Promise.resolve()
    await Promise.resolve()
    expect(mockColumnsApi.getColumns).toHaveBeenCalledWith('board-1')

    reconcileResponse.resolve([survivingColumn])
    await pendingDelete

    expect(state.currentBoard.value.columns).toEqual([survivingColumn])
    expect(state.currentBoardCards.value).toEqual([
      { id: 'card-surviving', boardId: 'board-1', columnId: 'col-b' },
    ])
    expect(helpers.toast.success).not.toHaveBeenCalled()
  })
})
