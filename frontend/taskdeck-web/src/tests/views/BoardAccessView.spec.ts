import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import BoardAccessView from '../../views/BoardAccessView.vue'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const routeMock = reactive({
  query: {} as Record<string, string>,
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

describe('BoardAccessView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
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

  it('defaults to the first board from the selector and avoids a raw board-id input', async () => {
    const wrapper = mount(BoardAccessView)
    await waitForUi()

    expect(boardsApiMocks.getBoards).toHaveBeenCalledTimes(1)
    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledWith('board-1')
    expect(wrapper.find('#board-selector').exists()).toBe(true)
    expect(wrapper.find('input[placeholder="Enter board ID"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Normal flows should not depend on memorized board IDs')
  })

  it('fetches the selected board access list when the selector changes', async () => {
    const wrapper = mount(BoardAccessView)
    await waitForUi()

    const selector = wrapper.get('#board-selector')
    await selector.setValue('board-2')
    await waitForUi()

    expect(permissionsStore.fetchBoardAccess).toHaveBeenLastCalledWith('board-2')
    expect(wrapper.text()).toContain('user-2')
  })

  it('shows the guided empty state when there are no boards to manage yet', async () => {
    boardsApiMocks.getBoards.mockResolvedValue([])

    const wrapper = mount(BoardAccessView)
    await waitForUi()

    expect(wrapper.text()).toContain('No boards available yet')
    expect(wrapper.text()).toContain('Create a board first')
    expect(wrapper.findAll('button').some((node) => node.text() === 'Create or Open Boards')).toBe(true)
  })
})
