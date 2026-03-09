import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import BoardAccessView from '../../views/BoardAccessView.vue'

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  const promise = new Promise<T>((innerResolve) => {
    resolve = innerResolve
  })

  return { promise, resolve }
}

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const routeMock = reactive({
  query: {} as Record<string, string | string[]>,
})

const boardsApiMocks = vi.hoisted(() => ({
  getBoards: vi.fn(),
}))

const permissionsStore = reactive({
  loading: false,
  boardAccess: new Map<string, Array<{ id: string; userId: string; role: string }>>(),
  fetchBoardAccess: vi.fn<(boardId: string) => Promise<void>>(),
  grantAccess: vi.fn<(...args: unknown[]) => Promise<void>>(),
  updateAccess: vi.fn<(...args: unknown[]) => Promise<void>>(),
  revokeAccess: vi.fn<(...args: unknown[]) => Promise<void>>(),
})

const sessionStore = reactive({
  userId: 'user-1',
})

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => routeMock,
  useRouter: () => ({
    push: routerMocks.push,
  }),
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: {
    getBoards: boardsApiMocks.getBoards,
  },
}))

vi.mock('../../store/permissionsStore', () => ({
  usePermissionsStore: () => permissionsStore,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionStore,
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    error: toastMocks.error,
    warning: toastMocks.warning,
  }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (error: unknown, fallback: string) => ({
    message: `${fallback} ${error instanceof Error ? error.message : ''}`.trim(),
  }),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
  await Promise.resolve()
}

function seedBoards() {
  return [
    {
      id: 'board-1',
      name: 'Alpha Board',
      description: 'First board',
      isArchived: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    },
    {
      id: 'board-2',
      name: 'Beta Board',
      description: 'Second board',
      isArchived: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    },
  ]
}

let mountedWrapper: ReturnType<typeof mount> | null = null

describe('BoardAccessView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    routeMock.query = {}
    permissionsStore.loading = false
    permissionsStore.boardAccess = new Map([
      ['board-1', [{ id: 'access-1', userId: 'user-1', role: 'Owner' }]],
      ['board-2', [{ id: 'access-2', userId: 'user-2', role: 'Viewer' }]],
    ])
    permissionsStore.fetchBoardAccess.mockResolvedValue(undefined)
    permissionsStore.grantAccess.mockResolvedValue(undefined)
    permissionsStore.updateAccess.mockResolvedValue(undefined)
    permissionsStore.revokeAccess.mockResolvedValue(undefined)
    boardsApiMocks.getBoards.mockResolvedValue(seedBoards())
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
  })

  it('defaults to the first board from the selector and avoids a raw board-id input', async () => {
    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    expect(boardsApiMocks.getBoards).toHaveBeenCalledTimes(1)
    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledTimes(1)
    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledWith('board-1')
    expect(wrapper.find('#board-selector').exists()).toBe(true)
    expect(wrapper.find('input[placeholder="Enter board ID"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Normal flows should not depend on memorized board IDs')
    expect(wrapper.text()).toContain('Why use the board selector here?')
  })

  it('fetches the selected board access list when the selector changes', async () => {
    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    const selector = wrapper.get('#board-selector')
    await selector.setValue('board-2')
    await waitForUi()

    expect(permissionsStore.fetchBoardAccess).toHaveBeenLastCalledWith('board-2')
    expect(wrapper.text()).toContain('user-2')
  })

  it('fetches access once when the boardId prop changes', async () => {
    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()
    permissionsStore.fetchBoardAccess.mockClear()

    await wrapper.setProps({ boardId: 'board-2' })
    await waitForUi()

    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledTimes(1)
    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledWith('board-2')
  })

  it('fetches access once when the route query changes', async () => {
    mountedWrapper = mount(BoardAccessView)
    await waitForUi()
    permissionsStore.fetchBoardAccess.mockClear()

    routeMock.query = { boardId: 'board-2' }
    await waitForUi()

    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledTimes(1)
    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledWith('board-2')
  })

  it('uses the first board id when the route query provides multiple values', async () => {
    routeMock.query = { boardId: ['board-2', 'board-1'] }

    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledWith('board-2')
    expect((wrapper.get('#board-selector').element as HTMLSelectElement).value).toBe('board-2')
  })

  it('disables refresh while access is already loading', async () => {
    permissionsStore.loading = true

    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    const refreshButton = wrapper.findAll('button').find((node) => node.text().includes('Refreshing...'))
    expect(refreshButton?.attributes('disabled')).toBeDefined()
  })

  it('disables refresh while boards are loading', async () => {
    const deferredBoards = createDeferred<ReturnType<typeof seedBoards>>()
    boardsApiMocks.getBoards.mockReturnValueOnce(deferredBoards.promise)

    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await Promise.resolve()

    const refreshButton = wrapper.findAll('button').find((node) => node.text().includes('Loading boards...'))
    expect(refreshButton?.attributes('disabled')).toBeDefined()

    deferredBoards.resolve(seedBoards())
    await waitForUi()
  })

  it('shows the guided empty state when there are no boards to manage yet', async () => {
    boardsApiMocks.getBoards.mockResolvedValue([])

    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    expect(wrapper.text()).toContain('No boards available yet')
    expect(wrapper.text()).toContain('Create a board first')
    expect(wrapper.findAll('button').some((node) => node.text() === 'Create or Open Boards')).toBe(true)
  })

  it('surfaces the mapped board-load error details', async () => {
    boardsApiMocks.getBoards.mockRejectedValueOnce(new Error('boom'))

    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    expect(toastMocks.error).toHaveBeenCalledWith('Failed to load boards for access management. boom')
  })
})
