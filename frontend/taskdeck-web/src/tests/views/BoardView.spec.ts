import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import BoardView from '../../views/BoardView.vue'

const routerMock = vi.hoisted(() => ({
  push: vi.fn(),
}))

const routeMock = reactive({
  params: { id: 'board-1' },
})

const addCardToggleMock = vi.fn()

const realtimeMock = {
  start: vi.fn(async () => {}),
  switchBoard: vi.fn(async () => {}),
  stop: vi.fn(async () => {}),
  setEditingCard: vi.fn(async () => {}),
}

const mockBoardStore = reactive({
  currentBoard: {
    id: 'board-1',
    name: 'Ops Board',
    description: 'Primary board',
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
    ],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  },
  currentBoardLabels: [],
  cardsByColumn: new Map([['column-1', []]]),
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

function mountView() {
  return mount(BoardView, {
    attachTo: document.body,
    global: {
      stubs: {
        ColumnLane: {
          props: ['column'],
          template: `
            <div :data-column-id="column.id">
              <button data-action="toggle-add-card" type="button" @click="handleToggle">Toggle add card</button>
              <textarea data-action="add-card-input"></textarea>
            </div>
          `,
          methods: {
            handleToggle() {
              addCardToggleMock()
            },
          },
        },
        BoardSettingsModal: { template: '<div />' },
        LabelManagerModal: { template: '<div />' },
        StarterPackCatalogModal: { template: '<div />' },
        KeyboardShortcutsHelp: { template: '<div />' },
        FilterPanel: { template: '<div />' },
        CaptureModal: {
          props: ['boardId', 'boardName'],
          template: '<div data-testid="capture-modal">Capture {{ boardName }} {{ boardId }}</div>',
        },
      },
    },
  })
}

describe('BoardView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    routeMock.params.id = 'board-1'
    mockBoardStore.currentBoard = {
      id: 'board-1',
      name: 'Ops Board',
      description: 'Primary board',
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
      ],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }
    mockBoardStore.cardsByColumn = new Map([['column-1', []]])
    mockBoardStore.loading = false
    mockBoardStore.error = null
    addCardToggleMock.mockReset()
  })

  it('renders the board action rail and preserves board context for review and chat routes', async () => {
    const wrapper = mountView()
    await waitForUi()

    expect(wrapper.text()).toContain('What should happen on a board?')
    expect(wrapper.get('[data-board-action-rail]').text()).toContain('Capture here')
    expect(wrapper.get('[data-board-action-rail]').text()).toContain('Ask assistant')
    expect(wrapper.get('[data-board-action-rail]').text()).toContain('Review proposals')

    const askAssistant = wrapper.findAll('button').find((node) => node.text().trim() === 'Ask assistant')
    const reviewProposals = wrapper.findAll('button').find((node) => node.text().trim() === 'Review proposals')

    await askAssistant?.trigger('click')
    await reviewProposals?.trigger('click')

    expect(routerMock.push).toHaveBeenCalledWith({
      name: 'workspace-automations-chat',
      query: { boardId: 'board-1' },
    })
    expect(routerMock.push).toHaveBeenCalledWith({
      name: 'workspace-review',
      query: { boardId: 'board-1' },
    })
  })

  it('opens a board-scoped capture modal from the action rail', async () => {
    const wrapper = mountView()
    await waitForUi()

    const captureHere = wrapper.findAll('button').find((node) => node.text().trim() === 'Capture here')
    await captureHere?.trigger('click')
    await waitForUi()

    expect(wrapper.get('[data-testid="capture-modal"]').text()).toContain('Capture Ops Board board-1')
  })

  it('opens the column form when add card is triggered without columns', async () => {
    mockBoardStore.currentBoard = {
      ...mockBoardStore.currentBoard,
      columns: [],
    }

    const wrapper = mountView()
    await waitForUi()

    const addCard = wrapper.findAll('button').find((node) => node.text().trim() === 'Add card')
    await addCard?.trigger('click')
    await waitForUi()

    expect(wrapper.get('input[placeholder="Column name"]').exists()).toBe(true)
  })

  it('reuses the existing add-card affordance when columns are present', async () => {
    const wrapper = mountView()
    await waitForUi()

    const addCard = wrapper.findAll('button').find((node) => node.text().trim() === 'Add card')
    await addCard?.trigger('click')
    await waitForUi()

    expect(addCardToggleMock).toHaveBeenCalledTimes(1)
  })
})
