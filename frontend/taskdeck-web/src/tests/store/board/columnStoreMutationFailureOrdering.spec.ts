import { beforeEach, describe, expect, it, vi } from 'vitest'
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

describe('columnStore failed predecessor ordering', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('starts the later same-board intent after the first request fails', async () => {
    const firstResponse = deferred<Array<{ id: string; name: string }>>()
    const secondResponse = deferred<Array<{ id: string; name: string }>>()
    mockColumnsApi.reorderColumns
      .mockReturnValueOnce(firstResponse.promise)
      .mockReturnValueOnce(secondResponse.promise)

    const state = {
      currentBoard: ref({
        id: 'board-1',
        columns: [
          { id: 'col-a', name: 'Todo' },
          { id: 'col-b', name: 'Done' },
        ],
      }),
      currentBoardCards: ref([]),
      loading: ref(false),
      error: ref<string | null>(null),
    }
    const helpers = {
      guardDemoMutation: vi.fn(),
      handleApiError: vi.fn(),
      toast: {
        success: vi.fn(),
        error: vi.fn(),
        warning: vi.fn(),
      },
      markBoardDetailMutation: vi.fn(),
    }
    const { reorderColumns } = createColumnActions(state as never, helpers as never)

    const first = reorderColumns('board-1', ['col-b', 'col-a'])
    const second = reorderColumns('board-1', ['col-a', 'col-b'])

    expect(mockColumnsApi.reorderColumns).toHaveBeenCalledTimes(1)
    firstResponse.reject(new Error('first reorder failed'))
    await expect(first).rejects.toThrow('first reorder failed')
    await flushPromises()

    expect(mockColumnsApi.reorderColumns).toHaveBeenCalledTimes(2)
    const confirmedOrder = [
      { id: 'col-a', name: 'Todo' },
      { id: 'col-b', name: 'Done' },
    ]
    secondResponse.resolve(confirmedOrder)
    await expect(second).resolves.toEqual(confirmedOrder)

    expect(state.currentBoard.value.columns).toEqual(confirmedOrder)
    expect(helpers.handleApiError).toHaveBeenCalledOnce()
    expect(helpers.toast.success).toHaveBeenCalledOnce()
  })
})
