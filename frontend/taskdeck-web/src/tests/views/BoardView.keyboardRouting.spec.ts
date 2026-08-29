import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import BoardView from '../../views/BoardView.vue'
import { usePaperThemeStore } from '../../store/paperThemeStore'

/**
 * Behaviour guard for the board <-> workspace keyboard collision (#2008).
 *
 * AppShell registers the workspace bindings on `window` in the CAPTURE phase
 * and calls `stopImmediatePropagation`, so a board-local binding for a key the
 * shell already owns can never run. `h` is one of those keys (it navigates
 * Home), which is why BoardView must bind Left — and only Left — for
 * previous-column navigation.
 *
 * Unlike the sibling BoardView specs this file deliberately does NOT mock
 * `useKeyboardShortcuts`: the assertions are about what actually happens when a
 * key is pressed, not about what was registered.
 */

const mockSessionStore = reactive<{ userId: string | null; username: string | null }>({
  userId: 'user-abc',
  username: 'alice',
})

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))

const routerMock = vi.hoisted(() => ({ push: vi.fn() }))
const routeMock = reactive({ params: { id: 'board-1' } })

vi.mock('vue-router', () => ({
  useRoute: () => routeMock,
  useRouter: () => routerMock,
  onBeforeRouteLeave: vi.fn(),
  onBeforeRouteUpdate: vi.fn(),
}))

function column(id: string, name: string, position: number) {
  return {
    id,
    boardId: 'board-1',
    name,
    position,
    wipLimit: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  }
}

function card(id: string, columnId: string) {
  return {
    id,
    columnId,
    boardId: 'board-1',
    title: `Card ${id}`,
    description: '',
    position: 0,
    labels: [],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  }
}

const mockBoardStore = reactive({
  currentBoard: {
    id: 'board-1',
    name: 'Ops Board',
    description: 'Primary board',
    columns: [column('column-1', 'Todo', 0), column('column-2', 'Doing', 1)],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  },
  currentBoardLabels: [],
  cardsByColumn: new Map<string, unknown[]>([
    ['column-1', [card('card-1', 'column-1')]],
    ['column-2', [card('card-2', 'column-2')]],
  ]),
  currentBoardCards: [card('card-1', 'column-1'), card('card-2', 'column-2')],
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
  filteredCardCount: 2,
  totalCardCount: 2,
  fetchBoard: vi.fn(async () => true),
  setBoardPresenceMembers: vi.fn(),
  setEditingCard: vi.fn(),
  createColumn: vi.fn(async () => {}),
  reorderColumns: vi.fn(async () => {}),
  updateFilters: vi.fn(),
})

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

vi.mock('../../composables/useBoardRealtime', () => ({
  createBoardRealtimeController: vi.fn(() => ({
    start: vi.fn(async () => {}),
    switchBoard: vi.fn(async () => {}),
    stop: vi.fn(async () => {}),
    setEditingCard: vi.fn(async () => {}),
  })),
}))

const mountedWrappers: Array<{ unmount: () => void }> = []

// The stub surfaces the live selection so the assertions can read the effect of
// `selectPreviousColumn` instead of inspecting registration data.
const BoardCanvasStub = {
  props: ['selectedCardId', 'sortedColumns', 'cardsByColumn', 'labels', 'boardId', 'hasColumns', 'draggedColumn', 'dragOverColumnId', 'draggedCard'],
  template: '<div data-testid="board-canvas" :data-selected-card-id="selectedCardId ?? \'\'"></div>',
}

function mountView() {
  const wrapper = mount(BoardView, {
    attachTo: document.body,
    global: {
      stubs: {
        BoardCanvas: BoardCanvasStub,
        BoardSettingsModal: { template: '<div />' },
        LabelManagerModal: { template: '<div />' },
        StarterPackCatalogModal: { template: '<div />' },
        KeyboardShortcutsHelp: { template: '<div />' },
        FilterPanel: { template: '<div />' },
        CaptureModal: { template: '<div />' },
      },
    },
  })
  mountedWrappers.push(wrapper)
  return wrapper
}

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

function pressKey(key: string) {
  document.body.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }))
}

function selectedCardId(): string {
  const canvas = document.body.querySelector('[data-testid="board-canvas"]')
  return canvas?.getAttribute('data-selected-card-id') ?? ''
}

describe('BoardView keyboard routing', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    routeMock.params.id = 'board-1'
    mockBoardStore.loading = false
    mockBoardStore.error = null
    usePaperThemeStore().disable()
  })

  afterEach(() => {
    mountedWrappers.splice(0).forEach((wrapper) => wrapper.unmount())
  })

  it('still moves to the previous column with ArrowLeft', async () => {
    mountView()
    await waitForUi()

    pressKey('ArrowRight')
    await waitForUi()
    expect(selectedCardId()).toBe('card-2')

    pressKey('ArrowLeft')
    await waitForUi()
    expect(selectedCardId()).toBe('card-1')
  })

  it('leaves `h` to the workspace Home binding instead of moving the column selection', async () => {
    mountView()
    await waitForUi()

    pressKey('ArrowRight')
    await waitForUi()
    expect(selectedCardId()).toBe('card-2')

    pressKey('h')
    await waitForUi()

    // The board must not consume `h`. AppShell owns it and routes Home; if
    // BoardView re-bound it the selection would slide back to card-1 here.
    expect(selectedCardId()).toBe('card-2')
  })
})
