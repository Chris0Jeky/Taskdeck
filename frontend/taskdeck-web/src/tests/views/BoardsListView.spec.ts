import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import BoardsListView from '../../views/BoardsListView.vue'
import type { Board } from '../../types/board'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const mockBoardStore = reactive({
  boards: [] as Board[],
  loading: false,
  error: null as string | null,
  fetchBoards: vi.fn<() => Promise<void>>(),
  createBoard: vi.fn<(payload: { name: string }) => Promise<Board>>(),
})

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: routerMocks.push,
  }),
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

function makeBoard(overrides: Partial<Board> = {}): Board {
  return {
    id: 'board-1',
    name: 'Test Board',
    description: null,
    isArchived: false,
    workspaceId: 'ws-1',
    createdAt: '2024-01-01T00:00:00.000Z',
    updatedAt: '2024-01-01T00:00:00.000Z',
    ...overrides,
  }
}

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('BoardsListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockBoardStore.boards = []
    mockBoardStore.loading = false
    mockBoardStore.error = null
    mockBoardStore.fetchBoards.mockResolvedValue(undefined)
    mockBoardStore.createBoard.mockResolvedValue(makeBoard())
  })

  it('fetches boards on mount', async () => {
    mount(BoardsListView)
    await waitForUi()

    expect(mockBoardStore.fetchBoards).toHaveBeenCalledTimes(1)
  })

  it('renders the page title', async () => {
    const wrapper = mount(BoardsListView)
    await waitForUi()

    expect(wrapper.text()).toContain('My Boards')
  })

  it('shows loading spinner while boards are loading', async () => {
    mockBoardStore.loading = true

    const wrapper = mount(BoardsListView)
    await waitForUi()

    expect(wrapper.text()).toContain('Loading boards...')
  })

  it('shows error state when boardStore.error is set', async () => {
    mockBoardStore.error = 'Failed to load boards'

    const wrapper = mount(BoardsListView)
    await waitForUi()

    expect(wrapper.text()).toContain('Failed to load boards')
  })

  it('shows empty state when no boards exist', async () => {
    mockBoardStore.boards = []

    const wrapper = mount(BoardsListView)
    await waitForUi()

    expect(wrapper.text()).toContain('No boards')
    expect(wrapper.text()).toContain('Get started by creating a new board.')
  })

  it('renders boards grid when boards exist', async () => {
    mockBoardStore.boards = [
      makeBoard({ id: 'b1', name: 'Alpha Board', description: 'First board' }),
      makeBoard({ id: 'b2', name: 'Beta Board', description: null }),
    ]

    const wrapper = mount(BoardsListView)
    await waitForUi()

    expect(wrapper.text()).toContain('Alpha Board')
    expect(wrapper.text()).toContain('First board')
    expect(wrapper.text()).toContain('Beta Board')
    expect(wrapper.text()).toContain('No description')
  })

  it('navigates to a board when its card is clicked', async () => {
    mockBoardStore.boards = [makeBoard({ id: 'board-xyz', name: 'My Board' })]

    const wrapper = mount(BoardsListView)
    await waitForUi()

    const boardCard = wrapper.find('[key]') // grid item
    // Find the clickable board card by looking for the board name in a clickable div
    const boardCards = wrapper.findAll('.cursor-pointer')
    const targetCard = boardCards.find((c) => c.text().includes('My Board'))
    expect(targetCard).toBeDefined()
    await targetCard!.trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith('/boards/board-xyz')
  })

  describe('create board form', () => {
    it('toggles the create form when + New Board is clicked', async () => {
      const wrapper = mount(BoardsListView)
      await waitForUi()

      expect(wrapper.find('form').exists()).toBe(false)

      const newBoardBtn = wrapper.findAll('button').find((b) => b.text().includes('+ New Board'))
      expect(newBoardBtn).toBeDefined()
      await newBoardBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.find('form').exists()).toBe(true)
      expect(wrapper.text()).toContain('Create New Board')
    })

    it('shows the create form when "Create Board" empty-state button is clicked', async () => {
      mockBoardStore.boards = []

      const wrapper = mount(BoardsListView)
      await waitForUi()

      const createBtn = wrapper.findAll('button').find((b) => b.text().includes('+ Create Board'))
      expect(createBtn).toBeDefined()
      await createBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.find('form').exists()).toBe(true)
    })

    it('does not submit when board name is empty', async () => {
      const wrapper = mount(BoardsListView)
      await waitForUi()

      const newBoardBtn = wrapper.findAll('button').find((b) => b.text().includes('+ New Board'))
      await newBoardBtn!.trigger('click')
      await waitForUi()

      await wrapper.find('form').trigger('submit')
      await waitForUi()

      expect(mockBoardStore.createBoard).not.toHaveBeenCalled()
    })

    it('creates a board and navigates to it', async () => {
      const newBoard = makeBoard({ id: 'new-board-id', name: 'My New Board' })
      mockBoardStore.createBoard.mockResolvedValue(newBoard)

      const wrapper = mount(BoardsListView)
      await waitForUi()

      const newBoardBtn = wrapper.findAll('button').find((b) => b.text().includes('+ New Board'))
      await newBoardBtn!.trigger('click')
      await waitForUi()

      await wrapper.find('input[placeholder="Board name"]').setValue('My New Board')
      await wrapper.find('form').trigger('submit')
      await waitForUi()

      expect(mockBoardStore.createBoard).toHaveBeenCalledWith({ name: 'My New Board' })
      expect(routerMocks.push).toHaveBeenCalledWith('/boards/new-board-id')
    })

    it('hides the create form after successful board creation', async () => {
      const newBoard = makeBoard({ id: 'nb', name: 'Fresh Board' })
      mockBoardStore.createBoard.mockResolvedValue(newBoard)

      const wrapper = mount(BoardsListView)
      await waitForUi()

      const newBoardBtn = wrapper.findAll('button').find((b) => b.text().includes('+ New Board'))
      await newBoardBtn!.trigger('click')
      await waitForUi()

      await wrapper.find('input[placeholder="Board name"]').setValue('Fresh Board')
      await wrapper.find('form').trigger('submit')
      await waitForUi()

      expect(wrapper.find('form').exists()).toBe(false)
    })

    it('cancels the create form when Cancel is clicked', async () => {
      const wrapper = mount(BoardsListView)
      await waitForUi()

      const newBoardBtn = wrapper.findAll('button').find((b) => b.text().includes('+ New Board'))
      await newBoardBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.find('form').exists()).toBe(true)

      const cancelBtn = wrapper.findAll('button').find((b) => b.text().includes('Cancel'))
      expect(cancelBtn).toBeDefined()
      await cancelBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.find('form').exists()).toBe(false)
    })
  })
})
