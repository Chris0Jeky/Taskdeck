import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick, reactive } from 'vue'
import BoardView from '../../views/BoardView.vue'
import { useKeyboardShortcuts } from '../../composables/useKeyboardShortcuts'
import { usePaperThemeStore } from '../../store/paperThemeStore'
import type { BoardPresenceSnapshot } from '../../types/realtime'

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

const addCardToggleMock = vi.fn()
const mountedWrappers: Array<{ unmount: () => void }> = []

const realtimeMock = {
  start: vi.fn(async () => {}),
  switchBoard: vi.fn(async () => {}),
  stop: vi.fn(async () => {}),
  setEditingCard: vi.fn(async () => {}),
}

// Captures the onPresenceChanged callback passed by BoardView so tests can
// simulate incoming SignalR presence snapshots.
let capturedOnPresenceChanged: ((snapshot: BoardPresenceSnapshot) => void) | undefined
let capturedRealtimeFetchBoard: ((boardId: string) => Promise<void>) | undefined

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
  currentBoardCards: [],
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
  fetchBoard: vi.fn(async () => true),
  setBoardPresenceMembers: vi.fn(),
  setEditingCard: vi.fn(),
  createColumn: vi.fn(async () => {}),
  reorderColumns: vi.fn(async () => {}),
  updateFilters: vi.fn(),
})

vi.mock('vue-router', () => ({
  useRoute: () => routeMock,
  useRouter: () => routerMock,
  onBeforeRouteLeave: vi.fn(),
  onBeforeRouteUpdate: vi.fn(),
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

vi.mock('../../composables/useKeyboardShortcuts', () => ({
  useKeyboardShortcuts: vi.fn(),
}))

vi.mock('../../composables/useBoardRealtime', () => ({
  createBoardRealtimeController: vi.fn((options) => {
    capturedOnPresenceChanged = options.onPresenceChanged
    capturedRealtimeFetchBoard = options.fetchBoard
    return realtimeMock
  }),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((innerResolve, innerReject) => {
    resolve = innerResolve
    reject = innerReject
  })
  return { promise, resolve, reject }
}

function mountView() {
  const wrapper = mount(BoardView, {
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
  mountedWrappers.push(wrapper)
  return wrapper
}

describe('BoardView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    capturedOnPresenceChanged = undefined
    capturedRealtimeFetchBoard = undefined
    localStorage.clear()
    routeMock.params.id = 'board-1'
    mockSessionStore.userId = 'user-abc'
    mockSessionStore.username = 'alice'
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
    mockBoardStore.currentBoardCards = []
    mockBoardStore.loading = false
    mockBoardStore.error = null
    addCardToggleMock.mockReset()
    usePaperThemeStore().disable()
  })

  afterEach(() => {
    mountedWrappers.splice(0).forEach((wrapper) => wrapper.unmount())
  })

  it('renders the board action rail and preserves board context for review, inbox, and chat routes', async () => {
    const wrapper = mountView()
    await waitForUi()

    expect(wrapper.text()).toContain('What should happen on a board?')
    expect(wrapper.text()).toContain('Only approved changes land on this board.')
    const actionRail = wrapper.get('[data-board-action-rail]')
    expect(actionRail.text()).toContain('Capture here')
    expect(actionRail.text()).toContain('Ask assistant')
    expect(actionRail.text()).toContain('Review proposals')
    expect(actionRail.text()).toContain('Open Inbox')

    const askAssistant = actionRail.findAll('button').find((node) => node.text().trim() === 'Ask assistant')
    const openInbox = actionRail.findAll('button').find((node) => node.text().trim() === 'Open Inbox')
    const reviewProposals = actionRail.findAll('button').find((node) => node.text().trim() === 'Review proposals')

    await askAssistant?.trigger('click')
    await openInbox?.trigger('click')
    await reviewProposals?.trigger('click')

    expect(routerMock.push).toHaveBeenCalledWith({
      name: 'workspace-automations-chat',
      query: { boardId: 'board-1' },
    })
    expect(routerMock.push).toHaveBeenCalledWith({
      name: 'workspace-inbox',
      query: { boardId: 'board-1' },
    })
    expect(routerMock.push).toHaveBeenCalledWith({
      name: 'workspace-review',
      query: { boardId: 'board-1' },
    })
  })

  it('keeps board navigation shortcuts enabled in paper mode while gating hidden controls', async () => {
    usePaperThemeStore().setMode('paper')

    mountView()
    await waitForUi()

    const shortcuts = vi.mocked(useKeyboardShortcuts).mock.calls.at(-1)?.[0] ?? []
    const shortcut = (key: string, description: string) =>
      shortcuts.find((s) => s.key === key && s.description === description)

    expect(shortcut('j', 'Next card')?.enabled?.()).toBe(true)
    expect(shortcut('ArrowRight', 'Move card to next column')?.enabled?.()).toBe(true)
    expect(shortcut('Enter', 'Open selected card')?.enabled?.()).toBe(true)
    // Escape stays ungated on purpose — the dialogs close themselves through
    // the shared escape stack.
    expect(shortcut('Escape', 'Close open dialog/panel')?.enabled).toBeUndefined()

    // `n` is ungated *by skin* as of #1945: PaperBoardColumn now renders the
    // `[data-action="toggle-add-card"]` button and `[data-action="add-card-input"]`
    // textarea that `createCardInSelectedColumn` drives, so the shortcut is live
    // in BOTH skins. `?` and `f` stay gated — Paper has its own shortcuts
    // overlay and no filter panel, so those controls really are hidden.
    expect(shortcut('n', 'New card in current column')?.enabled?.()).toBe(true)
    expect(shortcut('?', 'Toggle keyboard shortcuts help')?.enabled?.()).toBe(false)
    expect(shortcut('f', 'Toggle filter panel')?.enabled?.()).toBe(false)
  })

  it('makes the board shortcuts inert while a Paper dialog is open', async () => {
    usePaperThemeStore().setMode('paper')

    const wrapper = mountView()
    await waitForUi()

    const shortcuts = vi.mocked(useKeyboardShortcuts).mock.calls.at(-1)?.[0] ?? []
    const shortcut = (key: string, description: string) =>
      shortcuts.find((s) => s.key === key && s.description === description)

    expect(shortcut('n', 'New card in current column')?.enabled?.()).toBe(true)

    // Open a real Paper dialog through the real button, not by poking state:
    // an ungated `n` here clicked `[data-action="toggle-add-card"]` on the
    // column behind the modal and then stole focus into the composer.
    await wrapper.get('[data-testid="paper-board-settings"]').trigger('click')
    await waitForUi()

    expect(shortcut('n', 'New card in current column')?.enabled?.()).toBe(false)
    expect(shortcut('j', 'Next card')?.enabled?.()).toBe(false)
    expect(shortcut('Enter', 'Open selected card')?.enabled?.()).toBe(false)
    // Escape must still reach its handler.
    expect(shortcut('Escape', 'Close open dialog/panel')?.enabled).toBeUndefined()

    await wrapper.get('[data-action="close-dialog"]').trigger('click')
    await waitForUi()

    expect(shortcut('n', 'New card in current column')?.enabled?.()).toBe(true)
  })

  it('shows a demo-board badge for the client onboarding demo board', async () => {
    mockBoardStore.currentBoard = {
      ...mockBoardStore.currentBoard,
      name: 'DEMO: Client Onboarding Demo',
    }
    const wrapper = mountView()
    await waitForUi()

    expect(wrapper.text()).toContain('Demo board')
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

    expect(wrapper.find('input[placeholder="Column name"]').exists()).toBe(true)
  })

  it('reuses the existing add-card affordance when columns are present', async () => {
    const wrapper = mountView()
    await waitForUi()

    const addCard = wrapper.findAll('button').find((node) => node.text().trim() === 'Add card')
    await addCard?.trigger('click')
    await waitForUi()

    expect(addCardToggleMock).toHaveBeenCalledTimes(1)
  })

  it('seeds presence with the current user immediately on mount so the panel never flickers to empty', async () => {
    // Verify setBoardPresenceMembers is called with the current user before
    // fetchBoard and realtime.start resolve — no empty-state window (#523).
    mountView()
    await waitForUi()

    const firstCall = mockBoardStore.setBoardPresenceMembers.mock.calls[0]
    expect(firstCall).toBeDefined()
    expect(firstCall[0]).toEqual([
      { userId: 'user-abc', displayName: 'alice', editingCardId: null },
    ])
  })

  it('seeds presence with empty array when no user session is active', async () => {
    mockSessionStore.userId = null
    mockSessionStore.username = null

    mountView()
    await waitForUi()

    const firstCall = mockBoardStore.setBoardPresenceMembers.mock.calls[0]
    expect(firstCall).toBeDefined()
    expect(firstCall[0]).toEqual([])
  })

  it('switches realtime only for the newest board load when A resolves after B', async () => {
    const firstLoad = createDeferred<boolean>()
    const secondLoad = createDeferred<boolean>()
    mockBoardStore.fetchBoard.mockImplementationOnce(() => firstLoad.promise)
    mockBoardStore.fetchBoard.mockImplementationOnce(() => secondLoad.promise)

    mountView()
    await waitForUi()

    routeMock.params.id = 'board-2'
    await nextTick()
    await waitForUi()
    expect(mockBoardStore.fetchBoard).toHaveBeenNthCalledWith(1, 'board-1')
    expect(mockBoardStore.fetchBoard).toHaveBeenNthCalledWith(2, 'board-2')

    secondLoad.resolve(true)
    await waitForUi()
    expect(realtimeMock.switchBoard).toHaveBeenCalledTimes(1)
    expect(realtimeMock.switchBoard).toHaveBeenCalledWith('board-2')
    expect(realtimeMock.start).not.toHaveBeenCalled()

    firstLoad.resolve(false)
    await waitForUi()
    expect(realtimeMock.start).not.toHaveBeenCalled()
    expect(realtimeMock.switchBoard).toHaveBeenCalledTimes(1)

  })

  it('does not let a previous realtime subscription refresh after navigation to a new board', async () => {
    const boardBLoad = createDeferred<boolean>()
    mockBoardStore.fetchBoard.mockImplementationOnce(async () => true)
    mockBoardStore.fetchBoard.mockImplementationOnce(() => boardBLoad.promise)

    mountView()
    await waitForUi()

    routeMock.params.id = 'board-2'
    await nextTick()
    await waitForUi()
    expect(mockBoardStore.fetchBoard).toHaveBeenNthCalledWith(1, 'board-1')
    expect(mockBoardStore.fetchBoard).toHaveBeenNthCalledWith(2, 'board-2')

    expect(capturedRealtimeFetchBoard).toBeDefined()
    await capturedRealtimeFetchBoard!('board-1')
    expect(mockBoardStore.fetchBoard).toHaveBeenCalledTimes(2)

    boardBLoad.resolve(true)
    await waitForUi()
    expect(realtimeMock.switchBoard).toHaveBeenCalledWith('board-2')
  })

  it('normalizes current user displayName to username when server sends email in presence snapshot (#683)', async () => {
    mountView()
    await waitForUi()

    // Simulate SignalR snapshot where server sent email as displayName for current user
    capturedOnPresenceChanged?.({
      boardId: 'board-1',
      occurredAt: new Date().toISOString(),
      members: [
        { userId: 'user-abc', displayName: 'alice@taskdeck.local', editingCardId: 'card-1' },
      ],
    })
    await waitForUi()

    // Should have replaced email with username, preserving editingCardId
    const lastCall = mockBoardStore.setBoardPresenceMembers.mock.calls.at(-1)
    expect(lastCall).toBeDefined()
    expect(lastCall![0]).toEqual([
      { userId: 'user-abc', displayName: 'alice', editingCardId: 'card-1' },
    ])
  })

  it('leaves other members displayName unchanged when normalizing presence snapshot (#683)', async () => {
    mountView()
    await waitForUi()

    capturedOnPresenceChanged?.({
      boardId: 'board-1',
      occurredAt: new Date().toISOString(),
      members: [
        { userId: 'user-abc', displayName: 'alice@taskdeck.local', editingCardId: null },
        { userId: 'user-xyz', displayName: 'bob@taskdeck.local', editingCardId: null },
      ],
    })
    await waitForUi()

    const lastCall = mockBoardStore.setBoardPresenceMembers.mock.calls.at(-1)
    expect(lastCall).toBeDefined()
    // Current user normalized to username, other member unchanged
    expect(lastCall![0]).toEqual([
      { userId: 'user-abc', displayName: 'alice', editingCardId: null },
      { userId: 'user-xyz', displayName: 'bob@taskdeck.local', editingCardId: null },
    ])
  })

  it('ignores presence snapshots for other boards (#683)', async () => {
    mountView()
    await waitForUi()

    const callCountBefore = mockBoardStore.setBoardPresenceMembers.mock.calls.length

    capturedOnPresenceChanged?.({
      boardId: 'board-other',
      occurredAt: new Date().toISOString(),
      members: [{ userId: 'user-abc', displayName: 'alice@taskdeck.local', editingCardId: null }],
    })
    await waitForUi()

    expect(mockBoardStore.setBoardPresenceMembers.mock.calls.length).toBe(callCountBefore)
  })
})
