import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick, reactive, ref } from 'vue'
import BoardView from '../../views/BoardView.vue'
import BoardCanvas from '../../components/board/BoardCanvas.vue'
import ColumnLane from '../../components/board/ColumnLane.vue'
import { usePaperThemeStore } from '../../store/paperThemeStore'
import type { BoardDetail, Card, Column } from '../../types/board'

const routeGuards = vi.hoisted(() => ({
  leave: null as null | (() => boolean | Promise<boolean>),
  update: null as null | ((to: { fullPath: string }, from: { fullPath: string }) => boolean | Promise<boolean>),
}))

const routeMock = reactive({
  params: { id: 'board-1' },
  query: {} as Record<string, string>,
  fullPath: '/workspace/boards/board-1',
})

const routerMock = {
  push: vi.fn(),
  replace: vi.fn(),
}

vi.mock('vue-router', () => ({
  useRoute: () => routeMock,
  useRouter: () => routerMock,
  onBeforeRouteLeave: vi.fn((guard: () => boolean | Promise<boolean>) => {
    routeGuards.leave = guard
  }),
  onBeforeRouteUpdate: vi.fn((guard: (to: { fullPath: string }, from: { fullPath: string }) => boolean | Promise<boolean>) => {
    routeGuards.update = guard
  }),
}))

const sessionStore = reactive({
  userId: 'user-1' as string | null,
  username: 'alex' as string | null,
  isDemo: false,
})

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionStore,
}))

const column: Column = {
  id: 'column-1',
  boardId: 'board-1',
  name: 'Todo',
  position: 0,
  wipLimit: null,
  cardCount: 1,
  createdAt: '2026-09-18T00:00:00Z',
  updatedAt: '2026-09-18T00:00:00Z',
}

const card = {
  id: 'card-1',
  boardId: 'board-1',
  columnId: column.id,
  title: 'Keep the submitted assignment visible',
  description: '',
  dueDate: null,
  estimatedEffortMinutes: null,
  isBlocked: false,
  blockReason: null,
  isArchived: false,
  position: 0,
  labels: [],
  createdAt: '2026-09-18T00:00:00Z',
  updatedAt: '2026-09-18T00:00:00Z',
} as Card

const board: BoardDetail = {
  id: 'board-1',
  name: 'Legacy board',
  description: '',
  isArchived: false,
  columns: [column],
  createdAt: '2026-09-18T00:00:00Z',
  updatedAt: '2026-09-18T00:00:00Z',
}

const boardStore = reactive({
  currentBoard: board as BoardDetail | null,
  currentBoardLabels: [],
  currentBoardCards: [card] as Card[],
  cardsByColumn: new Map<string, Card[]>([[column.id, [card]]]),
  boardPresenceMembers: [],
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
  filteredCardCount: 1,
  totalCardCount: 1,
  fetchBoard: vi.fn(async () => true),
  cancelBackgroundBoardFetch: vi.fn(),
  setBoardPresenceMembers: vi.fn(),
  setEditingCard: vi.fn(),
  createColumn: vi.fn(async () => undefined),
  createCard: vi.fn(async () => undefined),
  moveCard: vi.fn(async () => undefined),
  reorderColumns: vi.fn(async () => undefined),
  updateFilters: vi.fn(),
})

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => boardStore,
}))

vi.mock('../../utils/demoMode', () => ({
  isDemoMode: false,
}))

vi.mock('../../composables/useKeyboardShortcuts', () => ({
  useKeyboardShortcuts: vi.fn(),
}))

vi.mock('../../composables/useBoardRealtime', () => ({
  createBoardRealtimeController: vi.fn(() => ({
    start: vi.fn(async () => undefined),
    switchBoard: vi.fn(async () => undefined),
    stop: vi.fn(async () => undefined),
    setEditingCard: vi.fn(async () => undefined),
  })),
}))

vi.mock('../../composables/useBoardDragDrop', () => ({
  useBoardDragDrop: vi.fn(() => ({
    draggedColumn: ref(null),
    dragOverColumnId: ref(null),
    draggedCard: ref(null),
    handleColumnDragStart: vi.fn(),
    handleColumnDragEnd: vi.fn(),
    handleColumnDragOver: vi.fn(),
    handleColumnDragLeave: vi.fn(),
    handleColumnDrop: vi.fn(),
    handleCardDragStart: vi.fn(),
    handleCardDragEnd: vi.fn(),
  })),
}))

vi.mock('../../composables/useBoardKeyboardNav', () => ({
  useBoardKeyboardNav: vi.fn(() => ({
    selectedCardId: ref<string | null>(null),
    selectedColumnIndex: ref(0),
    selectNextCard: vi.fn(),
    selectPreviousCard: vi.fn(),
    selectNextColumn: vi.fn(),
    selectPreviousColumn: vi.fn(),
    openSelectedCard: vi.fn(),
    createCardInSelectedColumn: vi.fn(),
    moveCardToNextColumn: vi.fn(),
    moveCardToPreviousColumn: vi.fn(),
    moveCardUp: vi.fn(),
    moveCardDown: vi.fn(),
    resetSelection: vi.fn(),
  })),
}))

vi.mock('../../composables/useShellKeyboardHelp', () => ({
  useShellKeyboardHelp: vi.fn(() => ({ open: vi.fn() })),
}))

vi.mock('../../composables/usePerformanceMark', () => ({
  usePerformanceMark: vi.fn(() => ({ start: vi.fn(), end: vi.fn() })),
}))

const mountedWrappers: Array<{ unmount: () => void }> = []

function mountLegacyBoardView() {
  const wrapper = mount(BoardView, {
    attachTo: document.body,
    global: {
      stubs: {
        BoardEstimateRollups: true,
        BoardCardArchive: true,
        BoardProposalPreview: true,
        PaperBoardView: true,
        BoardToolbar: true,
        BoardActionRail: true,
        WorkspaceHelpCallout: true,
        FilterPanel: true,
        BoardDialogHost: true,
        TdSkeleton: true,
        BoardCanvas: {
          name: 'BoardCanvas',
          emits: ['cardEditorSavingChange'],
          template: `
            <section data-testid="legacy-board-canvas">
              <button data-testid="begin-assignment-save" @click="$emit('cardEditorSavingChange', true)">Begin save</button>
              <button data-testid="settle-assignment-save" @click="$emit('cardEditorSavingChange', false)">Settle save</button>
            </section>
          `,
        },
        TdDialog: {
          props: ['open', 'title', 'description'],
          emits: ['close'],
          template: `
            <div v-if="open" role="dialog">
              <h2>{{ title }}</h2>
              <p>{{ description }}</p>
              <slot name="footer" />
            </div>
          `,
        },
      },
    },
  })
  mountedWrappers.push(wrapper)
  return wrapper
}

beforeEach(() => {
  vi.clearAllMocks()
  routeGuards.leave = null
  routeGuards.update = null
  routeMock.params.id = 'board-1'
  routeMock.query = {}
  routeMock.fullPath = '/workspace/boards/board-1'
  boardStore.currentBoard = board
  boardStore.currentBoardCards = [card]
  boardStore.cardsByColumn = new Map([[column.id, [card]]])
  boardStore.loading = false
  boardStore.error = null
  boardStore.fetchBoard.mockResolvedValue(true)
  usePaperThemeStore().disable()
})

afterEach(() => {
  mountedWrappers.splice(0).forEach((wrapper) => wrapper.unmount())
})

describe('Legacy assignment-save event propagation', () => {
  it('forwards CardModal saving state through ColumnLane', async () => {
    const wrapper = mount(ColumnLane, {
      props: {
        column,
        cards: [card],
        labels: [],
        boardId: board.id,
        allColumns: [column],
        draggedCard: null,
        selectedCardId: null,
      },
      global: {
        stubs: {
          CardItem: {
            name: 'CardItem',
            props: ['card'],
            emits: ['click', 'dragstart', 'dragend', 'move-to'],
            template: '<button data-testid="open-card" @click="$emit(\'click\', card)">Open</button>',
          },
          CardModal: {
            name: 'CardModal',
            props: ['card', 'isOpen', 'labels'],
            emits: ['close', 'updated', 'saving-change'],
            template: '<div data-testid="card-modal-stub" />',
          },
          CardEstimateField: true,
          ColumnEditModal: true,
        },
      },
    })
    mountedWrappers.push(wrapper)

    await wrapper.get('[data-testid="open-card"]').trigger('click')
    const modal = wrapper.findComponent({ name: 'CardModal' })
    modal.vm.$emit('saving-change', true)
    await nextTick()

    expect(wrapper.emitted('card-editor-saving-change')).toEqual([[true]])
  })

  it('forwards the lane saving state through BoardCanvas', async () => {
    const wrapper = mount(BoardCanvas, {
      props: {
        sortedColumns: [column],
        cardsByColumn: new Map([[column.id, [card]]]),
        labels: [],
        boardId: board.id,
        hasColumns: true,
        draggedColumn: null,
        dragOverColumnId: null,
        draggedCard: null,
        selectedCardId: null,
      },
      global: {
        stubs: {
          ColumnLane: {
            name: 'ColumnLane',
            emits: ['card-editor-saving-change'],
            template: '<button data-testid="lane-save" @click="$emit(\'card-editor-saving-change\', true)">Save</button>',
          },
        },
      },
    })
    mountedWrappers.push(wrapper)

    await wrapper.get('[data-testid="lane-save"]').trigger('click')

    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true]])
  })
})

describe('Legacy assignment-save navigation boundary', () => {
  it('refuses route leave and page exit until the save settles', async () => {
    const wrapper = mountLegacyBoardView()
    await flushPromises()

    expect(routeGuards.leave).toEqual(expect.any(Function))

    await wrapper.get('[data-testid="begin-assignment-save"]').trigger('click')
    const navigation = routeGuards.leave!()
    await nextTick()

    await expect(Promise.resolve(navigation)).resolves.toBe(false)
    const dialog = wrapper.get('[role="dialog"]')
    expect(dialog.text()).toContain('Saving assignments')
    expect(dialog.text()).toContain('cannot be discarded or cancelled')

    const beforeUnload = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(beforeUnload)
    expect(beforeUnload.defaultPrevented).toBe(true)

    await wrapper.get('[data-testid="settle-assignment-save"]').trigger('click')
    await nextTick()

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    expect(routeGuards.leave!()).toBe(true)

    const cleanUnload = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(cleanUnload)
    expect(cleanUnload.defaultPrevented).toBe(false)
  })

  it('refuses a same-view board route update until the save settles', async () => {
    const wrapper = mountLegacyBoardView()
    await flushPromises()

    expect(routeGuards.update).toEqual(expect.any(Function))

    await wrapper.get('[data-testid="begin-assignment-save"]').trigger('click')
    const to = { fullPath: '/workspace/boards/board-2' }
    const from = { fullPath: '/workspace/boards/board-1' }
    const navigation = routeGuards.update!(to, from)
    await nextTick()

    await expect(Promise.resolve(navigation)).resolves.toBe(false)
    expect(wrapper.get('[role="dialog"]').text()).toContain('cannot be discarded or cancelled')

    await wrapper.get('[data-testid="settle-assignment-save"]').trigger('click')
    await nextTick()

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    expect(routeGuards.update!(to, from)).toBe(true)
  })
})
