import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import BoardView from '../../views/BoardView.vue'
import { useBoardStore } from '../../store/boardStore'
import { usePaperThemeStore } from '../../store/paperThemeStore'
import { useToastStore } from '../../store/toastStore'
import { i18n } from '../../i18n'
import type { BoardDetail, Card } from '../../types/board'

const api = vi.hoisted(() => ({ getBoard: vi.fn(), getCards: vi.fn(), getLabels: vi.fn(), moveCard: vi.fn(), deleteCard: vi.fn() }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: api }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: api }))
vi.mock('../../api/labelsApi', () => ({ labelsApi: api }))
const route = reactive({ params: { id: 'board-a' }, query: {} })
const router = { push: vi.fn(), replace: vi.fn().mockResolvedValue(undefined) }
const session = reactive({ userId: 'reader', username: 'reader', isDemo: false })
let requestedBoard: string | null = null
let onRevoked: ((id: string) => void) | undefined
const realtime = {
  start: vi.fn(async (id: string) => { requestedBoard = id }),
  switchBoard: vi.fn(async (id: string) => { requestedBoard = id }),
  stop: vi.fn(async () => { requestedBoard = null }),
  setEditingCard: vi.fn().mockResolvedValue(undefined),
  // Model the real controller: a denial for A is ignored while it owns B/null.
  notifyAccessRevoked: vi.fn((id: string) => { if (requestedBoard === id) onRevoked?.(id) }),
}
vi.mock('vue-router', () => ({ useRoute: () => route, useRouter: () => router, onBeforeRouteLeave: vi.fn(), onBeforeRouteUpdate: vi.fn() }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../utils/demoMode', () => ({ isDemoMode: false }))
vi.mock('../../composables/useKeyboardShortcuts', () => ({ useKeyboardShortcuts: vi.fn() }))
vi.mock('../../composables/useBoardRealtime', () => ({ createBoardRealtimeController: vi.fn(options => { onRevoked = options.onAccessRevoked; return realtime }) }))

const stamp = '2026-09-25T10:00:00Z'
function board(id: string): BoardDetail {
  return { id, name: id, description: '', canWrite: true, isArchived: false, columns: [], createdAt: stamp, updatedAt: stamp }
}
function card(): Card {
  return { id: 'card', boardId: 'board-a', columnId: 'todo', title: 'Retained card', description: '', dueDate: null,
    isBlocked: false, blockReason: null, position: 0, labels: [], createdAt: stamp, updatedAt: stamp }
}
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
const wrappers: Array<{ unmount: () => void }> = []
function mountView() {
  const wrapper = mount(BoardView, { global: { stubs: {
    BoardToolbar: true, BoardCanvas: true, BoardDialogHost: true, BoardActionRail: true,
    BoardCardArchive: true, BoardEstimateRollups: true, FilterPanel: true,
  } } })
  wrappers.push(wrapper)
  return wrapper
}
const forbidden = { response: { status: 403, data: { message: 'Forbidden' } } }

async function startRecovery(kind: 'initial' | 'reopened', operation: 'move' | 'delete') {
  const store = useBoardStore()
  store.currentBoard = board('board-a')
  store.currentBoardCards = [card()]
  const mutation = deferred<Card>()
  api.moveCard.mockReturnValueOnce(mutation.promise)
  api.deleteCard.mockReturnValueOnce(mutation.promise)
  let wrapper: ReturnType<typeof mountView>
  if (kind === 'reopened') { wrapper = mountView(); await flushPromises() }
  const pendingMutation = operation === 'move'
    ? store.moveCard('board-a', 'card', 'done', 0)
    : store.deleteCard('board-a', 'card')
  if (kind === 'reopened') {
    route.params.id = 'board-b'
    await flushPromises()
    expect(requestedBoard).toBe('board-b')
  }
  const foreground = deferred<BoardDetail>()
  const recovery = deferred<BoardDetail>()
  api.getBoard.mockReturnValueOnce(foreground.promise).mockReturnValueOnce(recovery.promise)
  if (kind === 'initial') wrapper = mountView()
  else route.params.id = 'board-a'
  await flushPromises()
  mutation.resolve({ ...card(), columnId: 'done' })
  await flushPromises()
  foreground.resolve(board('board-a'))
  await flushPromises()
  // The recovery is genuinely running behind the invalidated explicit read.
  expect(api.getBoard).toHaveBeenCalledTimes(kind === 'initial' ? 2 : 4)
  return { store, wrapper: wrapper!, recovery, pendingMutation }
}

describe('BoardView ownership of forbidden card-mutation recovery', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    usePaperThemeStore().disable()
    route.params.id = 'board-a'
    requestedBoard = null
    onRevoked = undefined
    api.getBoard.mockReset().mockImplementation(async (id: string) => board(id))
    api.getCards.mockReset().mockImplementation(async (id: string) => id === 'board-a' ? [card()] : [])
    api.getLabels.mockReset().mockResolvedValue([])
    api.moveCard.mockReset(); api.deleteCard.mockReset()
  })
  afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()) })

  for (const operation of ['move', 'delete'] as const) {
    it.each(['initial', 'reopened'] as const)(`${operation}: retires a %s route when its only recovery returns 403`, async kind => {
      const error = vi.spyOn(useToastStore(), 'error')
      const { wrapper, recovery, pendingMutation } = await startRecovery(kind, operation)
      recovery.reject(forbidden)
      await pendingMutation
      await flushPromises()
      expect(router.replace).toHaveBeenCalledExactlyOnceWith('/workspace/boards')
      expect(wrapper.find('board-toolbar-stub').exists()).toBe(false)
      expect(wrapper.find('board-canvas-stub').exists()).toBe(false)
      expect(error.mock.calls.filter(([message]) => message === i18n.global.t('boardDetail.accessRevoked'))).toHaveLength(1)
      expect(realtime.notifyAccessRevoked).not.toHaveBeenCalled()
      // Duplicate controller delivery must not duplicate navigation or notice.
      onRevoked?.('board-a')
      await flushPromises()
      expect(router.replace).toHaveBeenCalledTimes(1)
    })
  }

  it.each(['navigation', 'unmount', 'session reset'] as const)('ignores an old recovery denial after %s', async mode => {
    const { store, recovery, pendingMutation } = await startRecovery('reopened', 'move')
    if (mode === 'navigation') { route.params.id = 'board-c'; await flushPromises() }
    if (mode === 'unmount') wrappers.pop()!.unmount()
    if (mode === 'session reset') store.resetForLogout()
    recovery.reject(forbidden)
    await pendingMutation
    await flushPromises()
    expect(router.replace).not.toHaveBeenCalled()
    if (mode === 'navigation') expect(store.currentBoard?.id).toBe('board-c')
  })

  it('keeps transient recovery failure non-revoking and preserves the acknowledged write result', async () => {
    const { recovery, pendingMutation } = await startRecovery('reopened', 'move')
    recovery.reject({ response: { status: 503 } })
    expect(await pendingMutation).toMatchObject({ id: 'card', columnId: 'done' })
    await flushPromises()
    expect(router.replace).not.toHaveBeenCalled()
  })
})
