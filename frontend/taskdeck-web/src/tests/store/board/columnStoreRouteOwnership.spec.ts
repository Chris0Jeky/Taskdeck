import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createBoardState } from '../../../store/board/boardState'
import { createBoardUiActions } from '../../../store/board/boardUiStore'
import type { BoardDetail, Card, Column } from '../../../types/board'

const { api } = vi.hoisted(() => ({ api: {
  createColumn: vi.fn(), updateColumn: vi.fn(), deleteColumn: vi.fn(),
  reorderColumns: vi.fn(), getColumns: vi.fn(),
} }))
vi.mock('../../../api/columnsApi', () => ({ columnsApi: api }))
import { createColumnActions } from '../../../store/board/columnStore'
import { createBoardCrudActions } from '../../../store/board/boardCrudStore'

const columns: Column[] = [
  { id: 'a', boardId: 'board-1', name: 'Todo', position: 0, wipLimit: null, cardCount: 1,
    createdAt: '2026-09-22T00:00:00Z', updatedAt: '2026-09-22T00:00:00Z' },
  { id: 'b', boardId: 'board-1', name: 'Done', position: 1, wipLimit: null, cardCount: 0,
    createdAt: '2026-09-22T00:00:00Z', updatedAt: '2026-09-22T00:00:00Z' },
]

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail })
  return { promise, resolve, reject }
}

async function settle() {
  await Promise.resolve()
  await Promise.resolve()
}

function setup() {
  const state = createBoardState()
  state.currentBoard.value = { id: 'board-1', columns: columns.map(c => ({ ...c })) } as BoardDetail
  state.currentBoardCards.value = [{ id: 'card', boardId: 'board-1', columnId: 'a' }] as Card[]
  const helpers = {
    guardDemoMutation: vi.fn(), handleApiError: vi.fn(), markBoardDetailMutation: vi.fn(),
    toast: { success: vi.fn(), warning: vi.fn(), error: vi.fn() },
  }
  const ui = createBoardUiActions(state)
  const visit = ui.beginBoardViewVisit('board-1')
  const actions = createColumnActions(state, helpers as never)
  return { state, helpers, ui, visit, actions }
}

describe('column mutations follow the board screen lifetime', () => {
  beforeEach(() => vi.resetAllMocks())

  it.each(['create', 'update', 'delete', 'reorder'] as const)(
    'does not publish a late %s after departure with cached detail retained', async (kind) => {
      const { state, helpers, ui, visit, actions } = setup()
      const response = deferred<never>()
      api.createColumn.mockReturnValue(response.promise)
      api.updateColumn.mockReturnValue(response.promise)
      api.deleteColumn.mockReturnValue(response.promise)
      api.reorderColumns.mockReturnValue(response.promise)
      const cachedColumns = state.currentBoard.value!.columns
      const cachedCards = state.currentBoardCards.value
      const pending = kind === 'create' ? actions.createColumn('board-1', { name: 'New' })
        : kind === 'update' ? actions.updateColumn('board-1', 'a', { name: 'Changed' })
          : kind === 'delete' ? actions.deleteColumn('board-1', 'a')
            : actions.reorderColumns('board-1', ['b', 'a'])

      ui.endBoardViewVisit(visit)
      state.error.value = 'New screen error'
      state.loading.value = true
      const result = kind === 'delete' ? undefined : kind === 'reorder' ? [...columns].reverse()
        : { ...columns[0], id: kind === 'create' ? 'new' : 'a', name: 'Changed' }
      response.resolve(result as never)
      await pending

      expect(state.currentBoard.value!.columns).toBe(cachedColumns)
      expect(state.currentBoard.value!.columns).toEqual(columns)
      expect(state.currentBoardCards.value).toBe(cachedCards)
      expect(state.currentBoardCards.value).toEqual([{ id: 'card', boardId: 'board-1', columnId: 'a' }])
      expect(api.getColumns).not.toHaveBeenCalled()
      expect(helpers.toast.success).not.toHaveBeenCalled()
      expect(helpers.handleApiError).not.toHaveBeenCalled()
      expect(state.error.value).toBe('New screen error')
      expect(state.loading.value).toBe(true)
    },
  )

  it('drops a queued intent and rejects a new stale-view call after departure', async () => {
    const { state, helpers, ui, visit, actions } = setup()
    const response = deferred<Column[]>()
    api.reorderColumns.mockReturnValue(response.promise)
    const first = actions.reorderColumns('board-1', ['b', 'a'])
    const queued = actions.reorderColumns('board-1', ['a', 'b'])
    const queuedFailure = expect(queued).rejects.toThrow('board visit')
    const cached = state.currentBoard.value!.columns
    ui.endBoardViewVisit(visit)
    const afterDeparture = actions.updateColumn('board-1', 'a', { name: 'Too late' })
    const departureFailure = expect(afterDeparture).rejects.toThrow('board visit')
    response.resolve([...columns].reverse())
    await Promise.all([first, queuedFailure, departureFailure])
    expect(api.reorderColumns).toHaveBeenCalledTimes(1)
    expect(api.updateColumn).not.toHaveBeenCalled()
    expect(api.getColumns).not.toHaveBeenCalled()
    expect(state.currentBoard.value!.columns).toBe(cached)
    expect(state.currentBoard.value!.columns).toEqual(columns)
    expect(helpers.toast.success).not.toHaveBeenCalled()
  })

  it.each(['departure', 'logout'])('does not publish an old failure after %s', async (boundary) => {
    const { state, helpers, ui, visit, actions } = setup()
    const response = deferred<Column>()
    api.updateColumn.mockReturnValue(response.promise)
    const pending = actions.updateColumn('board-1', 'a', { name: 'Changed' })
    if (boundary === 'logout') createBoardCrudActions(state, helpers as never).resetForLogout()
    else ui.endBoardViewVisit(visit)
    state.error.value = 'New screen error'
    state.loading.value = true
    const failure = new Error('Late failure')
    response.reject(failure)
    await expect(pending).rejects.toBe(failure)
    expect(helpers.handleApiError).not.toHaveBeenCalled()
    expect(state.error.value).toBe('New screen error')
    expect(state.loading.value).toBe(true)
  })

  it('drops pre-logout queued work even when a new login opens the same board', async () => {
    const { state, helpers, ui, actions } = setup()
    const response = deferred<Column[]>()
    api.reorderColumns.mockReturnValue(response.promise)
    const first = actions.reorderColumns('board-1', ['b', 'a'])
    const queued = actions.reorderColumns('board-1', ['a', 'b'])
    const queuedFailure = expect(queued).rejects.toThrow('board visit')
    createBoardCrudActions(state, helpers as never).resetForLogout()
    ui.beginBoardViewVisit('board-1')
    state.currentBoard.value = { id: 'board-1', columns: columns.map(c => ({ ...c })) } as BoardDetail
    const newAccountColumns = state.currentBoard.value.columns
    response.resolve([...columns].reverse())
    await Promise.all([first, queuedFailure])
    expect(api.reorderColumns).toHaveBeenCalledTimes(1)
    expect(api.getColumns).not.toHaveBeenCalled()
    expect(state.currentBoard.value.columns).toBe(newAccountColumns)
    expect(state.currentBoard.value.columns).toEqual(columns)
    expect(helpers.toast.success).not.toHaveBeenCalled()
  })

  it('retires the previous visit before the next route payload arrives', async () => {
    const { state, helpers, ui, actions } = setup()
    const response = deferred<Column>()
    api.updateColumn.mockReturnValue(response.promise)
    const pending = actions.updateColumn('board-1', 'a', { name: 'Changed' })
    ui.beginBoardViewVisit('board-2')
    response.resolve({ ...columns[0]!, name: 'Changed' })
    await pending
    expect(state.currentBoard.value!.columns).toEqual(columns)
    expect(api.getColumns).not.toHaveBeenCalled()
    expect(helpers.toast.success).not.toHaveBeenCalled()
    await expect(actions.deleteColumn('board-1', 'a')).rejects.toThrow('board visit')
    expect(api.deleteColumn).not.toHaveBeenCalled()
  })

  it.each([false, true])('reconciles once on reopening and guards departure during refresh: %s', async (departAgain) => {
    const { state, helpers, ui, visit, actions } = setup()
    const response = deferred<Column>()
    const refresh = deferred<Column[]>()
    api.updateColumn.mockReturnValue(response.promise)
    api.getColumns.mockReturnValue(refresh.promise)
    const pending = actions.updateColumn('board-1', 'a', { name: 'Changed' })
    ui.endBoardViewVisit(visit)
    const reopened = ui.beginBoardViewVisit('board-1')
    // Cleanup from an older component cannot retire the new component's visit.
    ui.endBoardViewVisit(visit)
    response.resolve({ ...columns[0]!, name: 'Changed' })
    await settle()
    expect(api.getColumns).toHaveBeenCalledTimes(1)
    if (departAgain) ui.endBoardViewVisit(reopened)
    const refreshed = columns.map(c => ({ ...c, name: `${c.name} refreshed` }))
    refresh.resolve(refreshed)
    await pending
    expect(state.currentBoard.value!.columns).toEqual(departAgain ? columns : refreshed)
    expect(api.getColumns).toHaveBeenCalledTimes(1)
    expect(helpers.toast.success).not.toHaveBeenCalled()
  })

  it('keeps a reopened visit write behind the single recovery read and preserves its later result', async () => {
    const { state, helpers, ui, visit, actions } = setup()
    const oldResponse = deferred<Column>()
    const nextResponse = deferred<Column>()
    const refresh = deferred<Column[]>()
    api.updateColumn.mockReturnValueOnce(oldResponse.promise).mockReturnValueOnce(nextResponse.promise)
    api.getColumns.mockReturnValue(refresh.promise)
    const oldWrite = actions.updateColumn('board-1', 'a', { name: 'Old visit' })
    ui.endBoardViewVisit(visit)
    ui.beginBoardViewVisit('board-1')
    const nextWrite = actions.updateColumn('board-1', 'a', { name: 'New visit' })
    oldResponse.resolve({ ...columns[0]!, name: 'Old visit' })
    await settle()
    expect(api.getColumns).toHaveBeenCalledTimes(1)
    expect(api.updateColumn).toHaveBeenCalledTimes(1)
    refresh.resolve([{ ...columns[0]!, name: 'Old visit' }, columns[1]!])
    await oldWrite
    await settle()
    expect(api.updateColumn).toHaveBeenCalledTimes(2)
    nextResponse.resolve({ ...columns[0]!, name: 'New visit' })
    await nextWrite
    expect(state.currentBoard.value!.columns).toEqual([{ ...columns[0]!, name: 'New visit' }, columns[1]!])
    expect(api.getColumns).toHaveBeenCalledTimes(1)
    expect(helpers.toast.success).toHaveBeenCalledTimes(1)
  })

  it('does not warn after a recovery read fails outside its reopened visit', async () => {
    const { state, helpers, ui, visit, actions } = setup()
    const response = deferred<Column>()
    const refresh = deferred<Column[]>()
    api.updateColumn.mockReturnValue(response.promise)
    api.getColumns.mockReturnValue(refresh.promise)
    const pending = actions.updateColumn('board-1', 'a', { name: 'Changed' })
    ui.endBoardViewVisit(visit)
    const reopened = ui.beginBoardViewVisit('board-1')
    response.resolve({ ...columns[0]!, name: 'Changed' })
    await settle()
    expect(api.getColumns).toHaveBeenCalledTimes(1)
    ui.endBoardViewVisit(reopened)
    refresh.reject(new Error('Late recovery failure'))
    await pending
    expect(state.currentBoard.value!.columns).toEqual(columns)
    expect(helpers.toast.warning).not.toHaveBeenCalled()
    expect(helpers.handleApiError).not.toHaveBeenCalled()
  })

  it('does not reconcile an old session response into a new login on the same board', async () => {
    const { state, helpers, ui, actions } = setup()
    const response = deferred<Column>()
    api.updateColumn.mockReturnValue(response.promise)
    const pending = actions.updateColumn('board-1', 'a', { name: 'Old account' })
    createBoardCrudActions(state, helpers as never).resetForLogout()
    ui.beginBoardViewVisit('board-1')
    state.currentBoard.value = { id: 'board-1', columns: columns.map(c => ({ ...c })) } as BoardDetail
    response.resolve({ ...columns[0]!, name: 'Old account' })
    await pending
    expect(api.getColumns).not.toHaveBeenCalled()
    expect(helpers.markBoardDetailMutation).not.toHaveBeenCalled()
    expect(state.currentBoard.value!.columns).toEqual(columns)
    expect(helpers.toast.success).not.toHaveBeenCalled()
  })
})
