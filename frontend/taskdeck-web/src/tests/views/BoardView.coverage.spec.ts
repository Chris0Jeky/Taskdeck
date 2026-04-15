import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import BoardView from '../../views/BoardView.vue'

const mockSessionStore = reactive<{ userId: string | null; username: string | null }>({
  userId: 'user-abc',
  username: 'alice',
})

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))

const routerMock = vi.hoisted(() => ({
  push: vi.fn(),
}))

const routeMock = reactive({
  params: { id: 'board-1' },
})

const realtimeMock = {
  start: vi.fn(async () => {}),
  switchBoard: vi.fn(async () => {}),
  stop: vi.fn(async () => {}),
  setEditingCard: vi.fn(async () => {}),
}

const mockBoardStore = reactive({
  currentBoard: null as {
    id: string
    name: string
    description: string | null
    columns: Array<{
      id: string
      boardId: string
      name: string
      position: number
      wipLimit: number | null
      createdAt: string
      updatedAt: string
    }>
    createdAt: string
    updatedAt: string
  } | null,
  currentBoardLabels: [],
  cardsByColumn: new Map<string, never[]>(),
  boardPresenceMembers: [] as Array<{ userId: string }>,
  editingCardId: null as string | null,
  loading: false,
  error: null as string | null,
  filters: {
    search: '',
    labelIds: [],
    onlyBlocked: false,
    dueBefore: '',
    dueAfter: '',
  },
  filteredCardCount: 0,
  totalCardCount: 0,
  fetchBoard: vi.fn(async () => {}),
  setBoardPresenceMembers: vi.fn(),
  setEditingCard: vi.fn(),
  createColumn: vi.fn(async () => {}),
  reorderColumns: vi.fn(async () => {}),
  updateFilters: vi.fn(),
})

vi.mock('vue-router', () => ({
  useRoute: () => routeMock,
  useRouter: () => routerMock,
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

vi.mock('../../composables/useKeyboardShortcuts', () => ({
  useKeyboardShortcuts: vi.fn(),
}))

vi.mock('../../composables/useBoardRealtime', () => ({
  createBoardRealtimeController: vi.fn(() => realtimeMock),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

function makeBoard(overrides: Partial<typeof mockBoardStore.currentBoard> = {}) {
  return {
    id: 'board-1',
    name: 'Test Board',
    description: 'A test board',
    columns: [
      {
        id: 'column-1',
        boardId: 'board-1',
        name: 'Todo',
        position: 0,
        wipLimit: null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
      {
        id: 'column-2',
        boardId: 'board-1',
        name: 'In Progress',
        position: 1,
        wipLimit: null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  }
}

let mountedWrapper: ReturnType<typeof mount> | null = null

function mountView() {
  const wrapper = mount(BoardView, {
    attachTo: document.body,
    global: {
      stubs: {
        ColumnLane: {
          props: ['column'],
          template: '<div :data-column-id="column.id"><span>{{ column.name }}</span></div>',
        },
        BoardSettingsModal: { template: '<div />' },
        LabelManagerModal: { template: '<div />' },
        StarterPackCatalogModal: { template: '<div />' },
        KeyboardShortcutsHelp: { template: '<div />' },
        FilterPanel: { template: '<div />' },
        CaptureModal: { template: '<div />' },
      },
    },
  })
  mountedWrapper = wrapper
  return wrapper
}

describe('BoardView — loading and error states', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    routeMock.params.id = 'board-1'
    mockSessionStore.userId = 'user-abc'
    mockSessionStore.username = 'alice'
    mockBoardStore.currentBoard = null
    mockBoardStore.cardsByColumn = new Map()
    mockBoardStore.loading = false
    mockBoardStore.error = null
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
  })

  it('shows loading spinner when board is loading and not yet available', async () => {
    mockBoardStore.loading = true
    mockBoardStore.currentBoard = null

    const wrapper = mountView()
    await waitForUi()

    expect(wrapper.find('[role="status"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Loading board...')
  })

  it('shows error state when board store has an error', async () => {
    mockBoardStore.error = 'Board not found'

    const wrapper = mountView()
    await waitForUi()

    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Board not found')
  })

  it('does not show loading spinner when board is loaded even if loading flag is still true', async () => {
    mockBoardStore.loading = true
    mockBoardStore.currentBoard = makeBoard()

    const wrapper = mountView()
    await waitForUi()

    // Loading spinner should not appear because board data is available
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
  })
})

describe('BoardView — column creation flow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    routeMock.params.id = 'board-1'
    mockSessionStore.userId = 'user-abc'
    mockSessionStore.username = 'alice'
    mockBoardStore.currentBoard = makeBoard({ columns: [] })
    mockBoardStore.cardsByColumn = new Map()
    mockBoardStore.loading = false
    mockBoardStore.error = null
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
  })

  it('shows Add card button which opens column form when no columns exist', async () => {
    const wrapper = mountView()
    await waitForUi()

    const addCard = wrapper.findAll('button').find((node) => node.text().trim() === 'Add card')
    expect(addCard).toBeDefined()
    await addCard!.trigger('click')
    await waitForUi()

    expect(wrapper.find('input[placeholder="Column name"]').exists()).toBe(true)
  })

  it('submits the column form and calls createColumn on the store', async () => {
    const wrapper = mountView()
    await waitForUi()

    // Click Add card to open column form
    const addCard = wrapper.findAll('button').find((node) => node.text().trim() === 'Add card')
    await addCard!.trigger('click')
    await waitForUi()

    const input = wrapper.get('input[placeholder="Column name"]')
    await input.setValue('New Column')

    const createBtn = wrapper.findAll('button').find((node) => node.text().trim() === 'Create')
    await createBtn!.trigger('click')
    await waitForUi()

    expect(mockBoardStore.createColumn).toHaveBeenCalledWith('board-1', { name: 'New Column' })
  })

  it('does not create column with empty name', async () => {
    const wrapper = mountView()
    await waitForUi()

    const addCard = wrapper.findAll('button').find((node) => node.text().trim() === 'Add card')
    await addCard!.trigger('click')
    await waitForUi()

    const createBtn = wrapper.findAll('button').find((node) => node.text().trim() === 'Create')
    await createBtn!.trigger('click')
    await waitForUi()

    expect(mockBoardStore.createColumn).not.toHaveBeenCalled()
  })

  it('hides column form when Cancel is clicked', async () => {
    const wrapper = mountView()
    await waitForUi()

    const addCard = wrapper.findAll('button').find((node) => node.text().trim() === 'Add card')
    await addCard!.trigger('click')
    await waitForUi()

    expect(wrapper.find('input[placeholder="Column name"]').exists()).toBe(true)

    const cancelBtn = wrapper.findAll('button').find((node) => node.text().trim() === 'Cancel')
    await cancelBtn!.trigger('click')
    await waitForUi()

    expect(wrapper.find('input[placeholder="Column name"]').exists()).toBe(false)
  })
})

describe('BoardView — board toolbar and columns rendering', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    routeMock.params.id = 'board-1'
    mockSessionStore.userId = 'user-abc'
    mockSessionStore.username = 'alice'
    mockBoardStore.currentBoard = makeBoard()
    mockBoardStore.cardsByColumn = new Map([
      ['column-1', []],
      ['column-2', []],
    ])
    mockBoardStore.loading = false
    mockBoardStore.error = null
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
  })

  it('renders columns sorted by position', async () => {
    const wrapper = mountView()
    await waitForUi()

    const columnElements = wrapper.findAll('[data-column-id]')
    expect(columnElements).toHaveLength(2)
    expect(columnElements[0].attributes('data-column-id')).toBe('column-1')
    expect(columnElements[1].attributes('data-column-id')).toBe('column-2')
  })

  it('calls fetchBoard on mount', async () => {
    mountView()
    await waitForUi()

    expect(mockBoardStore.fetchBoard).toHaveBeenCalledWith('board-1')
  })

  it('starts realtime connection on mount', async () => {
    mountView()
    await waitForUi()

    expect(realtimeMock.start).toHaveBeenCalledWith('board-1')
  })

  it('clears presence and stops realtime on unmount', async () => {
    const wrapper = mountView()
    await waitForUi()

    wrapper.unmount()

    expect(mockBoardStore.setBoardPresenceMembers).toHaveBeenCalledWith([])
    expect(mockBoardStore.setEditingCard).toHaveBeenCalledWith(null)
    expect(realtimeMock.stop).toHaveBeenCalled()
  })
})

describe('BoardView — help callout', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    routeMock.params.id = 'board-1'
    mockSessionStore.userId = 'user-abc'
    mockSessionStore.username = 'alice'
    mockBoardStore.currentBoard = makeBoard()
    mockBoardStore.cardsByColumn = new Map([['column-1', []]])
    mockBoardStore.loading = false
    mockBoardStore.error = null
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
  })

  it('shows the workspace help callout with board topic', async () => {
    const wrapper = mountView()
    await waitForUi()

    expect(wrapper.text()).toContain('What should happen on a board?')
  })
})
