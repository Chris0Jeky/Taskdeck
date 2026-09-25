import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import BoardView from '../../views/BoardView.vue'
import { useBoardStore } from '../../store/boardStore'
import { usePaperThemeStore } from '../../store/paperThemeStore'
import { clearAll, setSession, setToken } from '../../utils/tokenStorage'
import type { BoardDetail } from '../../types/board'

const api = vi.hoisted(() => ({ getBoard: vi.fn(), getCards: vi.fn(), getLabels: vi.fn() }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: api }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: api }))
vi.mock('../../api/labelsApi', () => ({ labelsApi: api }))
const route = reactive({ params: { id: 'board-a' }, query: {} })
const router = { push: vi.fn(), replace: vi.fn().mockResolvedValue(undefined) }
const session = reactive({ userId: 'reader', username: 'reader', isDemo: false })
const realtime = {
  start: vi.fn().mockResolvedValue(undefined), switchBoard: vi.fn().mockResolvedValue(undefined),
  stop: vi.fn().mockResolvedValue(undefined), setEditingCard: vi.fn().mockResolvedValue(undefined), notifyAccessRevoked: vi.fn(),
}
vi.mock('vue-router', () => ({ useRoute: () => route, useRouter: () => router, onBeforeRouteLeave: vi.fn(), onBeforeRouteUpdate: vi.fn() }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../utils/demoMode', () => ({ isDemoMode: false }))
vi.mock('../../composables/useKeyboardShortcuts', () => ({ useKeyboardShortcuts: vi.fn() }))
vi.mock('../../composables/useBoardRealtime', () => ({ createBoardRealtimeController: vi.fn(() => realtime) }))

const token = (version: number) => `eyJhbGciOiJIUzI1NiJ9.${btoa(JSON.stringify({ sub: 'reader', version })).replace(/=/g, '')}.synthetic`
function signIn(id = 'reader', version = 1) {
  expect(setToken(token(version))).toBe(true)
  expect(setSession({ userId: id, username: id, email: `${id}@example.test` })).toBe(true)
  session.userId = id
}
const stamp = '2026-09-25T10:00:00Z'
function board(id: string, name = id): BoardDetail {
  return { id, name, description: '', canWrite: true, isArchived: false, columns: [], createdAt: stamp, updatedAt: stamp }
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
async function pendingLoad(flow: 'mount' | 'switch' | 'retry') {
  const pending = deferred<BoardDetail>()
  let wrapper: ReturnType<typeof mountView>
  if (flow === 'mount') {
    api.getBoard.mockReturnValueOnce(pending.promise)
    wrapper = mountView()
  } else {
    if (flow === 'retry') api.getBoard.mockRejectedValueOnce(new Error('Initial network failure'))
    wrapper = mountView()
    await flushPromises()
    api.getBoard.mockReturnValueOnce(pending.promise)
    if (flow === 'switch') route.params.id = 'board-b'
    else await wrapper.get('[data-testid="board-load-retry"]').trigger('click')
  }
  await flushPromises()
  return { wrapper, pending, id: flow === 'switch' ? 'board-b' : 'board-a', requests: flow === 'mount' ? 1 : 2 }
}

describe('BoardView current-credential continuation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    usePaperThemeStore().disable()
    route.params.id = 'board-a'
    signIn()
    api.getBoard.mockReset().mockImplementation(async (id: string) => board(id, 'Current credentials'))
    api.getCards.mockReset().mockResolvedValue([])
    api.getLabels.mockReset().mockResolvedValue([])
  })
  afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()) })

  it.each(['mount', 'switch', 'retry'] as const)('rebinds %s after one bounded current-credential read', async flow => {
    const { pending, id, requests, wrapper } = await pendingLoad(flow)
    signIn('reader', 2)
    pending.resolve(board(id, 'Old credentials'))
    await flushPromises()
    expect(api.getBoard).toHaveBeenCalledTimes(requests + 1)
    expect(useBoardStore().currentBoard?.name).toBe('Current credentials')
    expect(realtime[flow === 'switch' ? 'switchBoard' : 'start']).toHaveBeenCalledExactlyOnceWith(id)
    expect(wrapper.find('[data-testid="board-load-error"]').exists()).toBe(false)
    const options = api.getBoard.mock.calls.at(-1)![1]
    expect(options).toMatchObject({ timeout: 10000, skipRetry: true })
  })

  it.each(['logout', 'new user', 'same-user re-login', 'cross-tab break', 'unmount', 'navigation'] as const)(
    'does not continue the old credential attempt after %s', async mode => {
      const { pending, id } = await pendingLoad('mount')
      if (mode === 'logout') clearAll()
      if (mode === 'new user') signIn('another', 2)
      if (mode === 'same-user re-login') { clearAll(); signIn('reader', 2) }
      if (mode === 'cross-tab break') {
        localStorage.setItem('taskdeck_session_break', 'other-tab-replaced-session')
        localStorage.setItem('taskdeck_token', token(2))
      }
      if (mode === 'unmount') wrappers.pop()!.unmount()
      if (mode === 'navigation') { route.params.id = 'board-c'; await flushPromises() }
      pending.resolve(board(id, 'Old credentials'))
      await flushPromises()
      expect(api.getBoard).toHaveBeenCalledTimes(mode === 'navigation' ? 2 : 1)
      expect(realtime.start).not.toHaveBeenCalled()
      if (mode === 'navigation') expect(realtime.switchBoard).toHaveBeenCalledExactlyOnceWith('board-c')
    },
  )

  it('keeps Retry available when the current-credential replacement fails', async () => {
    const { pending, wrapper } = await pendingLoad('mount')
    api.getBoard.mockRejectedValueOnce(new Error('Replacement failed'))
    signIn('reader', 2)
    pending.resolve(board('board-a', 'Old credentials'))
    await flushPromises()
    expect(api.getBoard).toHaveBeenCalledTimes(2)
    expect(realtime.start).not.toHaveBeenCalled()
    expect(wrapper.get('[data-testid="board-load-error"]').text()).toContain('Replacement failed')
    expect(wrapper.get('[data-testid="board-load-retry"]').attributes('disabled')).toBeUndefined()
  })

  it('bounds repeated refresh to one replacement and exposes an explicit Retry', async () => {
    const { pending, wrapper } = await pendingLoad('mount')
    const replacement = deferred<BoardDetail>()
    api.getBoard.mockReturnValueOnce(replacement.promise)
    signIn('reader', 2)
    pending.resolve(board('board-a', 'Old credentials'))
    await flushPromises()
    expect(api.getBoard).toHaveBeenCalledTimes(2)
    signIn('reader', 3)
    replacement.resolve(board('board-a', 'Superseded replacement'))
    await flushPromises()
    expect(api.getBoard).toHaveBeenCalledTimes(2)
    expect(realtime.start).not.toHaveBeenCalled()
    expect(wrapper.get('[data-testid="board-load-retry"]').attributes('disabled')).toBeUndefined()
  })
})
