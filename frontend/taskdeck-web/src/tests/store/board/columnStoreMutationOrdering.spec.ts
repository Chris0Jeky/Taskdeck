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

interface TestColumn {
  id: string
  boardId: string
  name: string
  position: number
  wipLimit: number | null
  cardCount: number
  createdAt: string
  updatedAt: string
}

interface TestCard {
  id: string
  boardId: string
  columnId: string
}

const columnA: TestColumn = {
  id: 'col-a',
  boardId: 'board-1',
  name: 'Todo',
  position: 0,
  wipLimit: null,
  cardCount: 0,
  createdAt: '2026-09-20T10:00:00Z',
  updatedAt: '2026-09-20T10:00:00Z',
}

const columnB: TestColumn = {
  ...columnA,
  id: 'col-b',
  name: 'Done',
  position: 1,
}

function createState() {
  return {
    currentBoard: ref<{
      id: string
      columns: TestColumn[]
    } | null>({
      id: 'board-1',
      columns: [{ ...columnA }, { ...columnB }],
    }),
    currentBoardCards: ref<TestCard[]>([
      { id: 'card-a', boardId: 'board-1', columnId: 'col-a' },
    ]),
    loading: ref(false),
    error: ref<string | null>(null),
  }
}

function createHelpers() {
  return {
    guardDemoMutation: vi.fn(),
    handleApiError: vi.fn(),
    toast: {
      success: vi.fn(),
      error: vi.fn(),
      warning: vi.fn(),
    },
    markBoardDetailMutation: vi.fn(),
  }
}

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

describe('columnStore mutation ordering and board ownership', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockColumnsApi.getColumns.mockReset()
    mockColumnsApi.createColumn.mockReset()
    mockColumnsApi.updateColumn.mockReset()
    mockColumnsApi.deleteColumn.mockReset()
    mockColumnsApi.reorderColumns.mockReset()
  })

  it('serializes overlapping reorders so the later intent owns server and cache order', async () => {
    const state = createState()
    const helpers = createHelpers()
    const firstResponse = deferred<TestColumn[]>()
    const secondResponse = deferred<TestColumn[]>()
    mockColumnsApi.reorderColumns
      .mockReturnValueOnce(firstResponse.promise)
      .mockReturnValueOnce(secondResponse.promise)
    const { reorderColumns } = createColumnActions(state as never, helpers as never)

    const first = reorderColumns('board-1', ['col-b', 'col-a'])
    const second = reorderColumns('board-1', ['col-a', 'col-b'])

    expect(mockColumnsApi.reorderColumns).toHaveBeenCalledTimes(1)

    const firstOrder = [
      { ...columnB, position: 0, updatedAt: '2026-09-20T10:01:00Z' },
      { ...columnA, position: 1, updatedAt: '2026-09-20T10:01:00Z' },
    ]
    firstResponse.resolve(firstOrder)
    await first
    await flushPromises()

    expect(mockColumnsApi.reorderColumns).toHaveBeenCalledTimes(2)
    expect(state.currentBoard.value!.columns).toEqual(firstOrder)

    const secondOrder = [
      { ...columnA, position: 0, updatedAt: '2026-09-20T10:02:00Z' },
      { ...columnB, position: 1, updatedAt: '2026-09-20T10:02:00Z' },
    ]
    secondResponse.resolve(secondOrder)
    await second

    expect(state.currentBoard.value!.columns).toEqual(secondOrder)
    expect(helpers.toast.success).toHaveBeenCalledTimes(2)
  })

  it('does not start a queued pre-navigation mutation under the next board session', async () => {
    const state = createState()
    const helpers = createHelpers()
    const firstResponse = deferred<TestColumn[]>()
    mockColumnsApi.reorderColumns.mockReturnValueOnce(firstResponse.promise)
    const { reorderColumns } = createColumnActions(state as never, helpers as never)

    const first = reorderColumns('board-1', ['col-b', 'col-a'])
    const queued = reorderColumns('board-1', ['col-a', 'col-b'])

    const nextColumns = [{ ...columnA, id: 'col-next', boardId: 'board-2' }]
    state.currentBoard.value = { id: 'board-2', columns: nextColumns }
    state.currentBoardCards.value = [
      { id: 'card-next', boardId: 'board-2', columnId: 'col-next' },
    ]
    firstResponse.resolve([{ ...columnB }, { ...columnA }])

    await first
    await expect(queued).rejects.toThrow('board visit')

    expect(mockColumnsApi.reorderColumns).toHaveBeenCalledTimes(1)
    expect(state.currentBoard.value.columns).toBe(nextColumns)
    expect(state.currentBoardCards.value).toEqual([
      { id: 'card-next', boardId: 'board-2', columnId: 'col-next' },
    ])
    expect(helpers.handleApiError).not.toHaveBeenCalled()
    expect(helpers.toast.success).not.toHaveBeenCalled()
  })

  it('does not let a late delete response strip the newly selected board state', async () => {
    const state = createState()
    const helpers = createHelpers()
    const response = deferred<void>()
    mockColumnsApi.deleteColumn.mockReturnValueOnce(response.promise)
    const { deleteColumn } = createColumnActions(state as never, helpers as never)

    const pendingDelete = deleteColumn('board-1', 'col-a')

    const nextColumns = [{ ...columnA, id: 'col-next', boardId: 'board-2' }]
    const nextCards = [{ id: 'card-next', boardId: 'board-2', columnId: 'col-next' }]
    state.currentBoard.value = { id: 'board-2', columns: nextColumns }
    state.currentBoardCards.value = nextCards
    response.resolve(undefined)
    await pendingDelete

    expect(state.currentBoard.value.columns).toBe(nextColumns)
    expect(state.currentBoardCards.value).toBe(nextCards)
    expect(helpers.toast.success).not.toHaveBeenCalled()
  })

  it('patches the currently installed same-board detail after an update settles', async () => {
    const state = createState()
    const helpers = createHelpers()
    const response = deferred<TestColumn>()
    mockColumnsApi.updateColumn.mockReturnValueOnce(response.promise)
    const { updateColumn } = createColumnActions(state as never, helpers as never)

    const pendingUpdate = updateColumn('board-1', 'col-a', { name: 'Backlog' })

    const refreshedColumns = [
      { ...columnA, name: 'Pre-write refresh' },
      { ...columnB },
    ]
    state.currentBoard.value = { id: 'board-1', columns: refreshedColumns }
    const updated = {
      ...columnA,
      name: 'Backlog',
      updatedAt: '2026-09-20T10:01:00Z',
    }
    response.resolve(updated)
    await pendingUpdate

    expect(state.currentBoard.value.columns).toBe(refreshedColumns)
    expect(state.currentBoard.value.columns[0]).toEqual(updated)
    expect(helpers.toast.success).toHaveBeenCalledWith('Column updated successfully')
  })

  it('reconciles an already-started successful write when the same board is reopened', async () => {
    const state = createState()
    const helpers = createHelpers()
    const updateResponse = deferred<TestColumn>()
    const reconcileResponse = deferred<TestColumn[]>()
    mockColumnsApi.updateColumn.mockReturnValueOnce(updateResponse.promise)
    mockColumnsApi.getColumns.mockReturnValueOnce(reconcileResponse.promise)
    const { updateColumn } = createColumnActions(state as never, helpers as never)

    const pendingUpdate = updateColumn('board-1', 'col-a', { name: 'Backlog' })

    state.currentBoard.value = { id: 'board-2', columns: [] }
    state.currentBoard.value = {
      id: 'board-1',
      columns: [{ ...columnA, name: 'Reopened pre-write value' }, { ...columnB }],
    }
    updateResponse.resolve({
      ...columnA,
      name: 'Backlog',
      updatedAt: '2026-09-20T10:01:00Z',
    })
    await flushPromises()

    expect(mockColumnsApi.getColumns).toHaveBeenCalledOnce()
    const authoritative = [
      { ...columnA, name: 'Backlog', updatedAt: '2026-09-20T10:01:00Z' },
      { ...columnB },
    ]
    reconcileResponse.resolve(authoritative)
    await pendingUpdate

    expect(state.currentBoard.value!.columns).toEqual(authoritative)
    expect(helpers.toast.success).not.toHaveBeenCalled()
    expect(helpers.toast.warning).not.toHaveBeenCalled()
  })
})
