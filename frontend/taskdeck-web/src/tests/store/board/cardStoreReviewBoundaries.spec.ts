import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createBoardState } from '../../../store/board/boardState'
import { createBoardUiActions } from '../../../store/board/boardUiStore'
import type { BoardHelpers } from '../../../store/board/boardStoreHelpers'
import type { Card } from '../../../types/board'

const api = vi.hoisted(() => ({ moveCard: vi.fn(), deleteCard: vi.fn() }))
vi.mock('../../../api/cardsApi', () => ({ cardsApi: api }))
vi.mock('../../../utils/errorMessage', () => ({
  getErrorMessage: (_error: unknown, fallback: string) => fallback,
}))
import { createCardActions } from '../../../store/board/cardStore'

const stamp = '2026-09-25T10:00:00Z'
function card(columnId = 'todo', updatedAt = stamp, id = 'card'): Card {
  return {
    id, boardId: 'board', columnId, title: 'Release', description: '', dueDate: null,
    isBlocked: false, blockReason: null, position: 0, labels: [], createdAt: stamp, updatedAt,
  }
}
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
function setup() {
  const state = createBoardState()
  state.currentBoard.value = {
    id: 'board', name: 'Board', description: null, isArchived: false,
    createdAt: stamp, updatedAt: stamp,
    columns: ['todo', 'doing', 'done'].map((id, position) => ({
      id, boardId: 'board', name: id, position, wipLimit: null,
      cardCount: position === 0 ? 1 : 0, createdAt: stamp, updatedAt: stamp,
    })),
  }
  state.currentBoardCards.value = [card()]
  const helpers = {
    guardDemoMutation: vi.fn(), isDemoMode: false, isHttpConflict: vi.fn(),
    markBoardDetailMutation: vi.fn(), toast: { success: vi.fn(), error: vi.fn() },
    handleApiError: vi.fn((_error: unknown, fallback: string) => { state.error.value = fallback }),
    updateColumnCardCount: vi.fn((id: string, delta: number) => {
      const column = state.currentBoard.value!.columns.find(item => item.id === id)!
      column.cardCount += delta
    }),
  }
  const refresh = vi.fn().mockResolvedValue(true)
  const ui = createBoardUiActions(state)
  const visit = ui.beginBoardViewVisit('board')
  const actions = createCardActions(state, helpers as unknown as BoardHelpers, refresh)
  return { state, helpers, actions, ui, visit, refresh }
}

const departedModes = ['pending replacement', 'failed replacement', 'unmount', 'leave and reopen'] as const
function depart(context: ReturnType<typeof setup>, mode: typeof departedModes[number]) {
  const { state, ui, visit } = context
  // These are the actual UI actions used by BoardView's route watcher and
  // unmount hook. Deliberately retain the old payload, as a slow/failed GET does.
  if (mode === 'unmount') ui.endBoardViewVisit(visit)
  else {
    ui.beginBoardViewVisit('other-board')
    if (mode === 'leave and reopen') ui.beginBoardViewVisit('board')
    if (mode === 'failed replacement') state.error.value = 'New board could not load'
  }
}

describe('card move/delete review boundaries', () => {
  beforeEach(() => { api.moveCard.mockReset(); api.deleteCard.mockReset() })

  it.each([
    ['2026-09-25T10:00:00.1234567Z', '2026-09-25T10:00:00.1234999Z'],
    ['2026-09-25T10:00:00.123Z', '2026-09-25T10:00:00.1230001Z'],
    ['2026-09-25T11:00:00.1234567+01:00', '2026-09-25T10:00:00.1234999Z'],
    ['2026-09-25T05:00:00.1234567-05:00', '2026-09-25T10:00:00.1234999Z'],
    ['2026-09-25T10:00:00.9999999Z', '2026-09-25T10:00:01Z'],
    ['1969-12-31T23:59:59.1234567Z', '1969-12-31T23:59:59.1234999Z'],
  ])('does not replace a newer authoritative card: %s before %s', async (older, newer) => {
    const { state, helpers, actions } = setup()
    const response = deferred<Card>()
    api.moveCard.mockReturnValueOnce(response.promise)
    const pending = actions.moveCard('board', 'card', 'doing', 0)
    const authoritative = card('done', newer)
    state.currentBoardCards.value = [authoritative]
    state.currentBoard.value!.columns.forEach(column => { column.cardCount = column.id === 'done' ? 1 : 0 })
    response.resolve(card('doing', older))
    expect(await pending).toEqual(card('doing', older))
    expect(state.currentBoardCards.value).toEqual([authoritative])
    expect(state.currentBoard.value!.columns.map(column => column.cardCount)).toEqual([0, 0, 1])
    expect(helpers.updateColumnCardCount).not.toHaveBeenCalled()
    expect(helpers.toast.success).not.toHaveBeenCalled()
  })

  it.each([
    ['2026-09-25T10:00:00.1234999Z', '2026-09-25T10:00:00.1234567Z'],
    ['2026-09-25T11:00:00.123+01:00', '2026-09-25T10:00:00.1230000Z'],
    ['2026-09-25T05:00:00-05:00', '2026-09-25T10:00:00.0000000Z'],
    ['2026-09-25T10:00:01Z', '2026-09-25T10:00:00.9999999Z'],
  ])('preserves equal/newer timestamp behavior: %s against %s', async (returned, current) => {
    const { state, helpers, actions } = setup()
    state.currentBoardCards.value = [card('todo', current)]
    api.moveCard.mockResolvedValueOnce(card('doing', returned))
    await actions.moveCard('board', 'card', 'doing', 0)
    expect(state.currentBoardCards.value).toEqual([card('doing', returned)])
    expect(state.currentBoard.value!.columns.map(column => column.cardCount)).toEqual([0, 1, 0])
    expect(helpers.toast.success).toHaveBeenCalledTimes(1)
  })

  for (const operation of ['move', 'delete'] as const) {
    it.each(departedModes)(`${operation} settlement cannot patch a departed visit during %s`, async mode => {
      const context = setup()
      const { state, helpers, actions, refresh } = context
      const response = deferred<Card>()
      api.moveCard.mockReturnValueOnce(response.promise)
      api.deleteCard.mockReturnValueOnce(response.promise)
      const pending = operation === 'move'
        ? actions.moveCard('board', 'card', 'doing', 0)
        : actions.deleteCard('board', 'card')
      depart(context, mode)
      response.resolve(card('doing', '2026-09-25T10:00:01Z'))
      await pending
      expect(state.currentBoardCards.value).toEqual([card()])
      expect(helpers.updateColumnCardCount).not.toHaveBeenCalled()
      expect(helpers.toast.success).not.toHaveBeenCalled()
      expect(refresh).not.toHaveBeenCalled()
    })
  }

  it.each(departedModes)('retires queued transport during %s without pretending to cancel the sent move', async mode => {
    const context = setup()
    const { actions } = context
    const first = deferred<Card>()
    api.moveCard.mockReturnValueOnce(first.promise)
    api.deleteCard.mockResolvedValueOnce(undefined)
    const sent = actions.moveCard('board', 'card', 'doing', 0)
    const queued = actions.deleteCard('board', 'card').then(() => null, error => error as Error)
    expect(api.moveCard).toHaveBeenCalledTimes(1)
    expect(api.deleteCard).not.toHaveBeenCalled()
    depart(context, mode)
    first.resolve(card('doing'))
    expect(await sent).toEqual(card('doing'))
    expect((await queued)?.name).toBe('StaleBoardVisitError')
    expect(api.deleteCard).not.toHaveBeenCalled()
  })

  it('preserves the current route error when an old move fails', async () => {
    const context = setup()
    const response = deferred<Card>()
    api.moveCard.mockReturnValueOnce(response.promise)
    const pending = context.actions.moveCard('board', 'card', 'doing', 0).then(() => null, error => error)
    depart(context, 'failed replacement')
    const failure = new Error('Old move failed')
    response.reject(failure)
    expect(await pending).toBe(failure)
    expect(context.state.error.value).toBe('New board could not load')
    expect(context.helpers.handleApiError).not.toHaveBeenCalled()
  })

  it('suppresses old-session success, invalidation and finally after reset with retained payload', async () => {
    const { state, helpers, actions } = setup()
    const response = deferred<Card>()
    api.moveCard.mockReturnValueOnce(response.promise)
    const pending = actions.moveCard('board', 'card', 'doing', 0)
    state.boardMutationSessionGeneration.value++
    state.loading.value = true
    state.error.value = 'New session error'
    response.resolve(card('doing'))
    await pending
    expect(state.currentBoardCards.value).toEqual([card()])
    expect(state.loading.value).toBe(true)
    expect(state.error.value).toBe('New session error')
    expect(helpers.markBoardDetailMutation).not.toHaveBeenCalled()
    expect(helpers.toast.success).not.toHaveBeenCalled()
  })

  it('retires queued transport when only the session generation changes', async () => {
    const { state, actions } = setup()
    const response = deferred<Card>()
    api.moveCard.mockReturnValueOnce(response.promise)
    api.deleteCard.mockResolvedValueOnce(undefined)
    const sent = actions.moveCard('board', 'card', 'doing', 0)
    const queued = actions.deleteCard('board', 'card').then(() => null, error => error as Error)
    state.boardMutationSessionGeneration.value++
    response.resolve(card('doing'))
    await sent
    expect((await queued)?.name).toBe('StaleBoardVisitError')
    expect(api.deleteCard).not.toHaveBeenCalled()
  })

  it('allows same-board payload refresh without retiring the route or queued write', async () => {
    const { state, actions } = setup()
    const response = deferred<Card>()
    api.moveCard.mockReturnValueOnce(response.promise)
    api.deleteCard.mockResolvedValueOnce(undefined)
    const sent = actions.moveCard('board', 'card', 'doing', 0)
    const queued = actions.deleteCard('board', 'card')
    state.currentBoard.value = { ...state.currentBoard.value!, columns: state.currentBoard.value!.columns.map(column => ({ ...column })) }
    response.resolve(card('doing', '2026-09-25T10:00:01Z'))
    await sent
    await queued
    expect(api.deleteCard).toHaveBeenCalledTimes(1)
    expect(state.currentBoardCards.value).toEqual([])
  })

  it('an old view cleanup cannot retire the new visit', async () => {
    const { actions, ui, visit, state } = setup()
    ui.beginBoardViewVisit('board')
    ui.endBoardViewVisit(visit)
    api.moveCard.mockResolvedValueOnce(card('doing'))
    await actions.moveCard('board', 'card', 'doing', 0)
    expect(state.currentBoardCards.value).toEqual([card('doing')])
  })

  it('keeps different-card transports independent', async () => {
    const { state, actions } = setup()
    state.currentBoardCards.value.push(card('todo', stamp, 'other-card'))
    const first = deferred<Card>()
    const second = deferred<Card>()
    api.moveCard.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const one = actions.moveCard('board', 'card', 'doing', 0)
    const two = actions.moveCard('board', 'other-card', 'done', 0)
    expect(api.moveCard).toHaveBeenCalledTimes(2)
    second.resolve(card('done', stamp, 'other-card'))
    await two
    first.resolve(card('doing'))
    await one
    expect(state.currentBoardCards.value.map(item => item.columnId)).toEqual(['doing', 'done'])
  })
})
