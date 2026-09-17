import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { reactive } from 'vue'
import BoardView from '../../views/BoardView.vue'
import { cardsApi } from '../../api/cardsApi'
import { usePaperThemeStore } from '../../store/paperThemeStore'
import type { Card } from '../../types/board'

const demoModeFlag = vi.hoisted(() => ({ value: true }))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    get isDemoMode() {
      return demoModeFlag.value
    },
  }
})

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getArchivedCards: vi.fn(),
    setArchived: vi.fn(),
  },
}))

const routeMock = reactive({ params: { id: 'demo-board' } })
const routerMock = { push: vi.fn(), replace: vi.fn() }

vi.mock('vue-router', () => ({
  useRoute: () => routeMock,
  useRouter: () => routerMock,
  onBeforeRouteLeave: vi.fn(),
  onBeforeRouteUpdate: vi.fn(),
}))

const sessionStore = reactive({
  userId: 'demo-user',
  username: 'demo',
  isDemo: true,
})

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionStore,
}))

const boardStore = reactive({
  currentBoard: {
    id: 'demo-board',
    name: 'Demo board',
    description: 'Synthetic demo board',
    columns: [],
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
  },
  currentBoardLabels: [],
  cardsByColumn: new Map<string, Card[]>(),
  currentBoardCards: [] as Card[],
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
  cancelBackgroundBoardFetch: vi.fn(),
  setBoardPresenceMembers: vi.fn(),
  setEditingCard: vi.fn(),
  createColumn: vi.fn(async () => undefined),
  reorderColumns: vi.fn(async () => undefined),
  updateFilters: vi.fn(),
})

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => boardStore,
}))

vi.mock('../../composables/useKeyboardShortcuts', () => ({
  useKeyboardShortcuts: vi.fn(),
}))

const realtime = {
  start: vi.fn(async () => undefined),
  switchBoard: vi.fn(async () => undefined),
  stop: vi.fn(async () => undefined),
  setEditingCard: vi.fn(async () => undefined),
}

vi.mock('../../composables/useBoardRealtime', () => ({
  createBoardRealtimeController: vi.fn(() => realtime),
}))

function mountView(): VueWrapper {
  return mount(BoardView, {
    attachTo: document.body,
    global: {
      stubs: {
        RouterLink: true,
        BoardEstimateRollups: true,
        BoardProposalPreview: true,
        BoardToolbar: true,
        BoardActionRail: true,
        WorkspaceHelpCallout: true,
        FilterPanel: true,
        BoardCanvas: { template: '<div data-testid="legacy-board" />' },
        BoardDialogHost: true,
        PaperBoardView: { template: '<div data-testid="paper-board" />' },
        TdSkeleton: true,
      },
    },
  })
}

describe('BoardView demo archive history', () => {
  const wrappers: VueWrapper[] = []

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    demoModeFlag.value = true
    sessionStore.isDemo = true
    routeMock.params.id = 'demo-board'
    boardStore.currentBoard.id = 'demo-board'
    boardStore.loading = false
    boardStore.error = null
    boardStore.fetchBoard.mockResolvedValue(true)
    usePaperThemeStore().disable()
  })

  afterEach(() => {
    wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
    document.body.innerHTML = ''
  })

  async function render() {
    const wrapper = mountView()
    wrappers.push(wrapper)
    await flushPromises()
    return wrapper
  }

  function expectOfflineArchiveExplanation(wrapper: VueWrapper) {
    const archive = wrapper.get('[aria-label="Card archive"]')
    expect(archive.text()).toContain('Archived card history is not available in this demo.')
    expect(archive.find('button').exists()).toBe(false)
    expect(cardsApi.getArchivedCards).not.toHaveBeenCalled()
    expect(cardsApi.setArchived).not.toHaveBeenCalled()
  }

  it('keeps archive history offline in the Legacy board presentation', async () => {
    const wrapper = await render()

    expect(wrapper.find('[data-testid="legacy-board"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-board"]').exists()).toBe(false)
    expectOfflineArchiveExplanation(wrapper)
  })

  it('keeps archive history offline in the Paper board presentation', async () => {
    usePaperThemeStore().enable()
    const wrapper = await render()

    expect(wrapper.find('[data-testid="paper-board"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="legacy-board"]').exists()).toBe(false)
    expectOfflineArchiveExplanation(wrapper)
  })
})
