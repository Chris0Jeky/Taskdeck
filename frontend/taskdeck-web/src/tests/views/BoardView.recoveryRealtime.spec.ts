import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import BoardView from '../../views/BoardView.vue'
import { usePaperThemeStore } from '../../store/paperThemeStore'
import type { BoardDetail, Card } from '../../types/board'

const route = reactive({ params: { id: 'board-a' }, query: {} })
const router = { push: vi.fn(), replace: vi.fn().mockResolvedValue(undefined) }
const session = reactive({ userId: 'reader', username: 'reader', isDemo: false })
const realtime = {
  start: vi.fn().mockResolvedValue(undefined),
  switchBoard: vi.fn().mockResolvedValue(undefined),
  stop: vi.fn().mockResolvedValue(undefined),
  setEditingCard: vi.fn().mockResolvedValue(undefined),
  notifyAccessRevoked: vi.fn(),
}
let revoke: ((id: string) => void) | undefined
const stamp = '2026-09-25T10:00:00Z'
function board(id: string): BoardDetail {
  return { id, name: id, description: '', canWrite: true, isArchived: false, columns: [], createdAt: stamp, updatedAt: stamp }
}
const store = reactive({
  currentBoard: board('board-a'),
  currentBoardRequestGeneration: 0,
  currentBoardPayloadGeneration: 0,
  currentBoardLabels: [], currentBoardCards: [] as Card[], cardsByColumn: new Map<string, Card[]>(),
  boardPresenceMembers: [], editingCardId: null, loading: false, error: null as string | null,
  filters: { search: '', labelIds: [], onlyBlocked: false, dueBefore: '', dueAfter: '' },
  filteredCardCount: 0, totalCardCount: 0,
  fetchBoard: vi.fn(), cancelBackgroundBoardFetch: vi.fn(),
  beginBoardViewVisit: vi.fn((boardId: string) => ({ boardId })), endBoardViewVisit: vi.fn(),
  setBoardPresenceMembers: vi.fn(), setEditingCard: vi.fn(), createColumn: vi.fn(),
  reorderColumns: vi.fn(), updateFilters: vi.fn(),
})
const pendingLoads: Array<Promise<boolean>> = []
const wrappers: Array<{ unmount: () => void }> = []

vi.mock('vue-router', () => ({
  useRoute: () => route, useRouter: () => router,
  onBeforeRouteLeave: vi.fn(), onBeforeRouteUpdate: vi.fn(),
}))
vi.mock('../../store/boardStore', () => ({ useBoardStore: () => store }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../utils/demoMode', () => ({ isDemoMode: false }))
vi.mock('../../composables/useKeyboardShortcuts', () => ({ useKeyboardShortcuts: vi.fn() }))
vi.mock('../../composables/useBoardRealtime', () => ({
  createBoardRealtimeController: vi.fn((options) => {
    revoke = options.onAccessRevoked
    return realtime
  }),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
function mountView() {
  const wrapper = mount(BoardView, {
    global: { stubs: {
      ColumnLane: true, BoardSettingsModal: true, LabelManagerModal: true,
      StarterPackCatalogModal: true, FilterPanel: true, CaptureModal: true,
    } },
  })
  wrappers.push(wrapper)
  return wrapper
}
async function publishRecovery(id: string) {
  store.currentBoard = board(id)
  store.currentBoardPayloadGeneration = ++store.currentBoardRequestGeneration
  await flushPromises()
}
async function returnToAWithPendingLoad() {
  mountView()
  await flushPromises()
  expect(realtime.start).toHaveBeenCalledWith('board-a')
  route.params.id = 'board-b'
  await flushPromises()
  expect(realtime.switchBoard).toHaveBeenLastCalledWith('board-b')
  const load = deferred<boolean>()
  pendingLoads.push(load.promise)
  route.params.id = 'board-a'
  await flushPromises()
  expect(store.currentBoard.id).toBe('board-b')
  return load
}

describe('BoardView realtime after invalidated explicit loads', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    usePaperThemeStore().disable()
    route.params.id = 'board-a'
    pendingLoads.length = 0
    revoke = undefined
    store.currentBoard = board('board-a')
    store.currentBoardRequestGeneration = 0
    store.currentBoardPayloadGeneration = 0
    store.error = null
    store.fetchBoard.mockReset().mockImplementation((id: string) => {
      const generation = ++store.currentBoardRequestGeneration
      const pending = pendingLoads.shift()
      if (pending) return pending
      store.currentBoard = board(id)
      store.currentBoardPayloadGeneration = generation
      return Promise.resolve(true)
    })
  })
  afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()) })

  it('starts realtime after an invalidated initial load is recovered, not from its cached payload', async () => {
    const load = deferred<boolean>()
    pendingLoads.push(load.promise)
    mountView()
    load.resolve(false)
    await flushPromises()
    expect(realtime.start).not.toHaveBeenCalled()
    await publishRecovery('board-a')
    expect(realtime.start).toHaveBeenCalledExactlyOnceWith('board-a')
    expect(store.fetchBoard).toHaveBeenCalledTimes(1)
  })

  it('switches realtime to reopened A after its background recovery commits', async () => {
    const load = await returnToAWithPendingLoad()
    load.resolve(false)
    await flushPromises()
    await publishRecovery('board-a')
    expect(realtime.switchBoard).toHaveBeenLastCalledWith('board-a')
    expect(realtime.switchBoard).toHaveBeenCalledTimes(2)
    expect(store.fetchBoard).toHaveBeenCalledTimes(3)
  })

  it('handles recovery committing before the invalidated explicit promise resolves', async () => {
    const load = await returnToAWithPendingLoad()
    await publishRecovery('board-a')
    load.resolve(false)
    await flushPromises()
    expect(realtime.switchBoard).toHaveBeenLastCalledWith('board-a')
    expect(realtime.switchBoard).toHaveBeenCalledTimes(2)
  })

  it('does not switch for an off-route payload and consumes recovery only once', async () => {
    const load = await returnToAWithPendingLoad()
    load.resolve(false)
    await flushPromises()
    await publishRecovery('board-b')
    expect(realtime.switchBoard).toHaveBeenCalledTimes(1)
    await publishRecovery('board-a')
    await publishRecovery('board-a')
    expect(realtime.switchBoard).toHaveBeenLastCalledWith('board-a')
    expect(realtime.switchBoard).toHaveBeenCalledTimes(2)
  })

  it('does not start realtime from recovery after unmount', async () => {
    const load = deferred<boolean>()
    pendingLoads.push(load.promise)
    mountView()
    load.resolve(false)
    await flushPromises()
    wrappers.pop()!.unmount()
    await publishRecovery('board-a')
    expect(realtime.start).not.toHaveBeenCalled()
    expect(realtime.stop).toHaveBeenCalledTimes(1)
  })

  it('does not rejoin a board revoked before its recovery commits', async () => {
    const load = await returnToAWithPendingLoad()
    load.resolve(false)
    await flushPromises()
    revoke!('board-a')
    await publishRecovery('board-a')
    expect(realtime.switchBoard).toHaveBeenCalledTimes(1)
    expect(router.replace).toHaveBeenCalledWith('/workspace/boards')
  })

  it('does not let a stale A load reclaim realtime after navigation to C', async () => {
    const load = await returnToAWithPendingLoad()
    route.params.id = 'board-c'
    await flushPromises()
    load.resolve(false)
    await flushPromises()
    await publishRecovery('board-a')
    expect(realtime.switchBoard).toHaveBeenLastCalledWith('board-c')
    expect(realtime.switchBoard).toHaveBeenCalledTimes(2)
  })

  it('does not duplicate a successful explicit connection on ordinary background commits', async () => {
    mountView()
    await flushPromises()
    await publishRecovery('board-a')
    await publishRecovery('board-a')
    expect(realtime.start).toHaveBeenCalledExactlyOnceWith('board-a')
    expect(realtime.switchBoard).not.toHaveBeenCalled()
  })

  it('keeps a failed initial load explicit rather than treating any later payload as its recovery', async () => {
    const load = deferred<boolean>()
    pendingLoads.push(load.promise)
    mountView()
    load.reject(new Error('Load failed'))
    await flushPromises()
    await publishRecovery('board-a')
    expect(realtime.start).not.toHaveBeenCalled()
  })
})
