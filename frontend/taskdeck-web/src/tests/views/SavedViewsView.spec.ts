import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import SavedViewsView from '../../views/SavedViewsView.vue'
import type { SavedView, SavedViewFilter } from '../../store/savedViewStore'
import type { Board } from '../../types/board'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const routeMock = vi.hoisted(() => ({
  params: {} as Record<string, unknown>,
}))

function makeFilter(overrides: Partial<SavedViewFilter> = {}): SavedViewFilter {
  return {
    searchText: '',
    labelNames: [],
    dueDateFilter: 'all',
    showBlockedOnly: false,
    ...overrides,
  }
}

function makeView(overrides: Partial<SavedView> = {}): SavedView {
  return {
    id: 'view-1',
    name: 'Default View',
    icon: 'D',
    filter: makeFilter(),
    isDefault: true,
    createdAt: '2024-01-01T00:00:00.000Z',
    ...overrides,
  }
}

function makeBoard(overrides: Partial<Board> = {}): Board {
  return {
    id: 'board-1',
    name: 'My Board',
    description: null,
    isArchived: false,
    createdAt: '2024-01-01T00:00:00.000Z',
    updatedAt: '2024-01-01T00:00:00.000Z',
    ...overrides,
  }
}

const mockSavedViewStore = reactive({
  defaultViews: [
    makeView({ id: 'default-blocked', name: 'Blocked Work', icon: 'X' }),
    makeView({ id: 'default-due-week', name: 'Due This Week', icon: 'W' }),
  ] as SavedView[],
  customViews: [] as SavedView[],
  activeView: null as SavedView | null,
  setActiveView: vi.fn<(id: string | null) => void>(),
  createView: vi.fn<(name: string, icon: string, filter: SavedViewFilter) => SavedView>(),
  deleteView: vi.fn<(id: string) => void>(),
})

const mockBoardStore = reactive({
  boards: [] as Board[],
  fetchBoards: vi.fn<() => Promise<void>>(),
})

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: routerMocks.push,
  }),
  useRoute: () => routeMock,
}))

vi.mock('../../store/savedViewStore', () => ({
  useSavedViewStore: () => mockSavedViewStore,
  cardMatchesSavedViewFilter: vi.fn().mockReturnValue(true),
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getCards: vi.fn().mockResolvedValue([]),
  },
}))

vi.mock('../../components/workspace/WorkspaceHelpCallout.vue', () => ({
  default: {
    template: '<div data-testid="workspace-help-callout" />',
    props: ['topic', 'title', 'description'],
  },
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('SavedViewsView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    routeMock.params = {}
    mockSavedViewStore.defaultViews = [
      makeView({ id: 'default-blocked', name: 'Blocked Work', icon: 'X' }),
      makeView({ id: 'default-due-week', name: 'Due This Week', icon: 'W' }),
    ]
    mockSavedViewStore.customViews = []
    mockSavedViewStore.activeView = null
    mockBoardStore.boards = []
    mockBoardStore.fetchBoards.mockResolvedValue(undefined)
    mockSavedViewStore.createView.mockImplementation((name, icon, filter) =>
      makeView({ id: `custom-${Date.now()}`, name, icon, filter, isDefault: false }),
    )
  })

  it('renders the Saved Views title', async () => {
    const wrapper = mount(SavedViewsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Saved Views')
  })

  it('renders default views in the picker', async () => {
    const wrapper = mount(SavedViewsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Default Views')
    expect(wrapper.text()).toContain('Blocked Work')
    expect(wrapper.text()).toContain('Due This Week')
  })

  it('shows the help callout', async () => {
    const wrapper = mount(SavedViewsView)
    await waitForUi()

    expect(wrapper.find('[data-testid="workspace-help-callout"]').exists()).toBe(true)
  })

  it('shows New View button and toggles create form', async () => {
    const wrapper = mount(SavedViewsView)
    await waitForUi()

    const newViewBtn = wrapper.findAll('button').find((b) => b.text().includes('New View'))
    expect(newViewBtn).toBeDefined()

    await newViewBtn!.trigger('click')
    await waitForUi()

    expect(wrapper.text()).toContain('Create a custom view')
    expect(wrapper.find('#sv-name').exists()).toBe(true)
  })

  it('hides create form when Cancel is clicked', async () => {
    const wrapper = mount(SavedViewsView)
    await waitForUi()

    const newViewBtn = wrapper.findAll('button').find((b) => b.text().includes('New View'))
    await newViewBtn!.trigger('click')
    await waitForUi()

    const cancelBtn = wrapper.findAll('button').find((b) => b.text().includes('Cancel'))
    await cancelBtn!.trigger('click')
    await waitForUi()

    expect(wrapper.find('#sv-name').exists()).toBe(false)
  })

  it('does not call createView when name is empty', async () => {
    const wrapper = mount(SavedViewsView)
    await waitForUi()

    const newViewBtn = wrapper.findAll('button').find((b) => b.text().includes('New View'))
    await newViewBtn!.trigger('click')
    await waitForUi()

    const createBtn = wrapper
      .findAll('button')
      .find((b) => b.text().includes('Create View'))
    expect(createBtn!.attributes('disabled')).toBeDefined()
    await createBtn!.trigger('click')
    await waitForUi()

    expect(mockSavedViewStore.createView).not.toHaveBeenCalled()
  })

  it('calls createView and navigates when a new custom view is created', async () => {
    const newView = makeView({
      id: 'custom-1',
      name: 'My Custom View',
      icon: 'M',
      isDefault: false,
    })
    mockSavedViewStore.createView.mockReturnValue(newView)

    const wrapper = mount(SavedViewsView)
    await waitForUi()

    const newViewBtn = wrapper.findAll('button').find((b) => b.text().includes('New View'))
    await newViewBtn!.trigger('click')
    await waitForUi()

    await wrapper.find('#sv-name').setValue('My Custom View')
    await wrapper.find('#sv-icon').setValue('M')

    const createBtn = wrapper
      .findAll('button')
      .find((b) => b.text().includes('Create View'))
    await createBtn!.trigger('click')
    await waitForUi()

    expect(mockSavedViewStore.createView).toHaveBeenCalledWith(
      'My Custom View',
      'M',
      expect.objectContaining({ searchText: '', showBlockedOnly: false }),
    )
    expect(routerMocks.push).toHaveBeenCalledWith(`/workspace/views/${newView.id}`)
  })

  it('renders custom views in the picker when they exist', async () => {
    mockSavedViewStore.customViews = [
      makeView({ id: 'custom-1', name: 'My Custom', icon: 'C', isDefault: false }),
    ]

    const wrapper = mount(SavedViewsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Custom Views')
    expect(wrapper.text()).toContain('My Custom')
  })

  it('calls deleteView when the delete button is clicked on a custom view', async () => {
    mockSavedViewStore.customViews = [
      makeView({ id: 'custom-1', name: 'Deletable View', icon: 'D', isDefault: false }),
    ]

    const wrapper = mount(SavedViewsView)
    await waitForUi()

    const deleteBtn = wrapper
      .findAll('button[aria-label="Delete view"]')
      .at(0)
    expect(deleteBtn).toBeDefined()
    await deleteBtn!.trigger('click')
    await waitForUi()

    expect(mockSavedViewStore.deleteView).toHaveBeenCalledWith('custom-1')
  })

  it('navigates to the view route when a view card is clicked', async () => {
    const wrapper = mount(SavedViewsView)
    await waitForUi()

    const viewCard = wrapper.findAll('.td-saved-views__card').at(0)
    expect(viewCard).toBeDefined()
    await viewCard!.trigger('click')
    await waitForUi()

    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/views/default-blocked')
  })

  describe('results panel', () => {
    it('shows active view results when a view is active', async () => {
      mockSavedViewStore.activeView = makeView({
        id: 'default-blocked',
        name: 'Blocked Work',
        icon: 'X',
      })

      const wrapper = mount(SavedViewsView)
      await waitForUi()

      expect(wrapper.text()).toContain('Blocked Work')
      expect(wrapper.text()).toContain('0 cards')
    })

    it('shows loading state while cards are being loaded', async () => {
      mockSavedViewStore.activeView = makeView()
      mockBoardStore.boards = [makeBoard()]

      // Delay fetchBoards resolution to hold loading=true
      let resolveBoards!: () => void
      mockBoardStore.fetchBoards.mockReturnValue(
        new Promise<void>((res) => {
          resolveBoards = res
        }),
      )

      const wrapper = mount(SavedViewsView)
      // Flush microtasks so onMounted starts executing but fetchBoards hasn't resolved
      await Promise.resolve()

      // While fetchBoards is still pending, loading indicator should be visible
      // (loading=true is set synchronously before the first await inside loadAllCards)
      expect(wrapper.find('[aria-live="polite"]').exists()).toBe(true)
      expect(wrapper.find('[aria-live="polite"]').text()).toContain('Loading cards...')

      resolveBoards()
      await waitForUi()
    })

    it('shows empty state when no cards match active view', async () => {
      mockSavedViewStore.activeView = makeView({
        id: 'default-blocked',
        name: 'Blocked Work',
      })
      // cardsApi.getCards returns [] — nothing will match

      const wrapper = mount(SavedViewsView)
      await waitForUi()

      expect(wrapper.text()).toContain('No cards match this view')
    })

    it('shows Clear Filter button when a view is active', async () => {
      mockSavedViewStore.activeView = makeView()

      const wrapper = mount(SavedViewsView)
      await waitForUi()

      const clearBtn = wrapper.findAll('button').find((b) => b.text().includes('Clear Filter'))
      expect(clearBtn).toBeDefined()
    })

    it('navigates to /workspace/views when Clear Filter is clicked', async () => {
      mockSavedViewStore.activeView = makeView()

      const wrapper = mount(SavedViewsView)
      await waitForUi()

      const clearBtn = wrapper.findAll('button').find((b) => b.text().includes('Clear Filter'))
      await clearBtn!.trigger('click')
      await waitForUi()

      expect(routerMocks.push).toHaveBeenCalledWith('/workspace/views')
    })
  })
})
