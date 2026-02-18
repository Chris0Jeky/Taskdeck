import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import ActivityView from '../../views/ActivityView.vue'

const mockRouter = {
  push: vi.fn().mockResolvedValue(undefined),
}

const mockRoute = reactive({
  name: 'workspace-activity',
  fullPath: '/workspace/activity',
  params: {} as Record<string, string>,
})

const mockAuditStore = reactive({
  entries: [] as Array<Record<string, unknown>>,
  loading: false,
  error: null as string | null,
  fetchBoardHistory: vi.fn().mockResolvedValue(undefined),
  fetchEntityHistory: vi.fn().mockResolvedValue(undefined),
  fetchUserHistory: vi.fn().mockResolvedValue(undefined),
})

const mockBoardStore = reactive({
  boards: [] as Array<{ id: string; name: string; isArchived: boolean; description: null; createdAt: string; updatedAt: string }>,
  currentBoard: null as null | { id: string; columns: Array<{ id: string; name: string; position: number }> },
  currentBoardCards: [] as Array<{ id: string; title: string; columnId: string; position: number }>,
  currentBoardLabels: [] as Array<{ id: string; name: string }>,
  fetchBoards: vi.fn<(...args: unknown[]) => Promise<void>>(),
  fetchBoard: vi.fn<(boardId: string) => Promise<void>>(),
})

const mockSessionStore = reactive({
  userId: 'user-1',
  username: 'activity-user',
})

const mockToastStore = {
  error: vi.fn(),
  success: vi.fn(),
}

vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
  useRouter: () => mockRouter,
}))

vi.mock('../../store/auditStore', () => ({
  useAuditStore: () => mockAuditStore,
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => mockToastStore,
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
  await Promise.resolve()
}

function seedBoards() {
  mockBoardStore.boards = [
    {
      id: 'board-1',
      name: 'Board One',
      isArchived: false,
      description: null,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    },
    {
      id: 'board-2',
      name: 'Board Two',
      isArchived: false,
      description: null,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    },
  ]
}

describe('ActivityView selector discoverability', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    mockRoute.name = 'workspace-activity'
    mockRoute.fullPath = '/workspace/activity'
    mockRoute.params = {}

    mockAuditStore.entries = []
    mockAuditStore.loading = false
    mockAuditStore.error = null

    mockBoardStore.currentBoard = null
    mockBoardStore.currentBoardCards = []
    mockBoardStore.currentBoardLabels = []

    mockBoardStore.fetchBoards.mockImplementation(async () => {
      seedBoards()
    })

    mockBoardStore.fetchBoard.mockImplementation(async (boardId: string) => {
      if (boardId !== 'board-1') {
        mockBoardStore.currentBoard = {
          id: boardId,
          columns: [],
        }
        mockBoardStore.currentBoardCards = []
        mockBoardStore.currentBoardLabels = []
        return
      }

      mockBoardStore.currentBoard = {
        id: 'board-1',
        columns: [
          { id: 'col-1', name: 'Todo', position: 1 },
          { id: 'col-2', name: 'Done', position: 2 },
        ],
      }
      mockBoardStore.currentBoardCards = [
        { id: 'card-1', title: 'Card One', columnId: 'col-1', position: 1 },
        { id: 'card-2', title: 'Card Two', columnId: 'col-2', position: 2 },
      ]
      mockBoardStore.currentBoardLabels = [
        { id: 'label-1', name: 'Bug' },
      ]
    })
  })

  it('uses board selector flow instead of raw ID input', async () => {
    const wrapper = mount(ActivityView)
    await waitForUi()

    expect(mockBoardStore.fetchBoards).toHaveBeenCalledWith(undefined, true)
    expect(wrapper.find('input[placeholder="Board ID"]').exists()).toBe(false)

    const boardSelect = wrapper.get('#activity-board-select')
    await boardSelect.setValue('board-2')
    await wrapper.get('button.td-btn--primary').trigger('click')
    await waitForUi()

    expect(mockRouter.push).toHaveBeenLastCalledWith({
      name: 'workspace-activity-board',
      params: { boardId: 'board-2' },
    })
    expect(mockAuditStore.fetchBoardHistory).toHaveBeenLastCalledWith('board-2', 50)
  })

  it('fetches entity history from discoverable selectors', async () => {
    const wrapper = mount(ActivityView)
    await waitForUi()

    await wrapper.get('#activity-view-mode').setValue('entity')
    await waitForUi()

    await wrapper.get('#activity-entity-type').setValue('Card')
    await waitForUi()

    expect(mockBoardStore.fetchBoard).toHaveBeenCalledWith('board-1')

    const entitySelect = wrapper.get('#activity-entity-select')
    await entitySelect.setValue('card-2')

    await wrapper.get('button.td-btn--primary').trigger('click')
    await waitForUi()

    expect(mockRouter.push).toHaveBeenLastCalledWith({
      name: 'workspace-activity-entity',
      params: {
        entityType: 'Card',
        entityId: 'card-2',
      },
    })
    expect(mockAuditStore.fetchEntityHistory).toHaveBeenLastCalledWith('Card', 'card-2', 50)
  })

  it('supports user mode without requiring IDs', async () => {
    const wrapper = mount(ActivityView)
    await waitForUi()

    await wrapper.get('#activity-view-mode').setValue('user')
    await wrapper.get('button.td-btn--primary').trigger('click')
    await waitForUi()

    expect(wrapper.text()).toContain('Current user:')
    expect(mockRouter.push).toHaveBeenLastCalledWith({ name: 'workspace-activity-user' })
    expect(mockAuditStore.fetchUserHistory).toHaveBeenLastCalledWith(50)
  })

  it('preserves deep-linked entity ID before selector options hydrate', async () => {
    mockRoute.name = 'workspace-activity-entity'
    mockRoute.fullPath = '/workspace/activity/entity/Card/card-route'
    mockRoute.params = {
      entityType: 'Card',
      entityId: 'card-route',
    }

    mount(ActivityView)
    await waitForUi()

    expect(mockAuditStore.fetchEntityHistory).toHaveBeenCalledWith('Card', 'card-route', 50)
  })
})
