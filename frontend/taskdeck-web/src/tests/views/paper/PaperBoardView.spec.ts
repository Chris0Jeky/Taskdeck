import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick, reactive, ref } from 'vue'
import PaperBoardView from '../../../views/paper/PaperBoardView.vue'
import PaperBoardColumn from '../../../views/paper/PaperBoardColumn.vue'
import type { BoardDetail, Card, Column } from '../../../types/board'
import type { ViewportMode } from '../../../composables/useViewportMode'

const routerMock = { push: vi.fn() }
const routeMock = reactive({ params: { id: 'board-1' } })
const mockViewportMode = ref<ViewportMode>('desktop')

function makeColumn(partial: Partial<Column> = {}): Column {
  return {
    id: 'col-1',
    boardId: 'board-1',
    name: 'Backlog',
    position: 0,
    wipLimit: null,
    cardCount: 0,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...partial,
  }
}

function makeCard(id: string, columnId: string, title = 'card', position = 0): Card {
  return {
    id,
    boardId: 'board-1',
    columnId,
    title,
    description: '',
    dueDate: null,
    isBlocked: false,
    blockReason: null,
    position,
    labels: [],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  }
}

const columns: Column[] = [
  makeColumn({ id: 'col-backlog', name: 'Backlog', position: 0, wipLimit: 2 }),
  makeColumn({ id: 'col-today', name: 'Today', position: 1 }),
  makeColumn({ id: 'col-progress', name: 'In Progress', position: 2 }),
  makeColumn({ id: 'col-done', name: 'Done', position: 3 }),
]

const board: BoardDetail = {
  id: 'board-1',
  name: 'Product Backlog',
  description: 'Primary board',
  isArchived: false,
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
  columns,
}

const cardsByColumn = new Map<string, Card[]>([
  // Backlog has 3 cards but wipLimit is 2 → triggers the OVERDUE tagstamp.
  ['col-backlog', [
    makeCard('card-1', 'col-backlog', 'A', 0),
    makeCard('card-2', 'col-backlog', 'B', 1),
    makeCard('card-3', 'col-backlog', 'C', 2),
  ]],
  ['col-today', []],
  ['col-progress', [makeCard('card-4', 'col-progress', 'D')]],
  ['col-done', []],
])

const allCards = [...cardsByColumn.values()].flat()

const mockBoardStore = reactive({
  currentBoard: board,
  currentBoardCards: allCards,
  cardsByColumn,
  currentBoardLabels: [],
  loading: false,
  error: null as string | null,
  fetchBoard: vi.fn(async () => {}),
  moveCard: vi.fn(async () => {}),
  reorderColumns: vi.fn(async () => {}),
})

vi.mock('vue-router', () => ({
  useRoute: () => routeMock,
  useRouter: () => routerMock,
}))

vi.mock('../../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

vi.mock('../../../composables/useViewportMode', () => ({
  useViewportMode: () => ({ mode: mockViewportMode }),
}))

function mountView(props: Record<string, unknown> = {}) {
  return mount(PaperBoardView, {
    attachTo: document.body,
    props,
    global: {
      stubs: {
        CardModal: {
          props: ['card', 'isOpen', 'labels'],
          template: '<div v-if="isOpen" data-testid="paper-card-modal">{{ card.title }}</div>',
        },
      },
    },
  })
}

function makeDragEvent(type: string): DragEvent {
  const event = new Event(type, { bubbles: true, cancelable: true }) as unknown as DragEvent
  Object.defineProperty(event, 'dataTransfer', {
    value: { effectAllowed: '', dropEffect: '', setData: vi.fn() },
    configurable: true,
  })
  return event
}

describe('PaperBoardView', () => {
  beforeEach(() => {
    routerMock.push.mockClear()
    mockBoardStore.fetchBoard.mockClear()
    mockBoardStore.moveCard.mockClear()
    mockBoardStore.currentBoardCards = allCards
    mockBoardStore.cardsByColumn = cardsByColumn
    mockBoardStore.error = null
    mockBoardStore.loading = false
    mockViewportMode.value = 'desktop'
  })

  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('renders all four columns from the stubbed boardStore', () => {
    const wrapper = mountView()
    const renderedColumns = wrapper.findAllComponents(PaperBoardColumn)
    expect(renderedColumns).toHaveLength(4)
    expect(renderedColumns.map((c) => c.props('column').name)).toEqual([
      'Backlog',
      'Today',
      'In Progress',
      'Done',
    ])
    // Indices passed to columns are 1-based serials.
    expect(renderedColumns.map((c) => c.props('index'))).toEqual([1, 2, 3, 4])
  })

  it('renders unfiltered cards when the paper surface hides filter controls', () => {
    mockBoardStore.cardsByColumn = new Map<string, Card[]>([
      ['col-backlog', [cardsByColumn.get('col-backlog')![0]]],
      ['col-today', []],
      ['col-progress', []],
      ['col-done', []],
    ])

    const wrapper = mountView()

    expect(wrapper.find('[data-card-id="card-1"]').exists()).toBe(true)
    expect(wrapper.find('[data-card-id="card-2"]').exists()).toBe(true)
    expect(wrapper.find('[data-card-id="card-3"]').exists()).toBe(true)
    expect(wrapper.find('[data-card-id="card-4"]').exists()).toBe(true)
    expect(wrapper.find('.paper-board-view__subline').text()).toContain('4 cards')
  })

  it('highlights the card selected by wrapping board keyboard navigation', () => {
    const wrapper = mountView({ selectedCardId: 'card-2' })

    expect(wrapper.get('[data-card-id="card-2"]').classes()).toContain('paper-board-card--selected')
  })

  it('shows the hairline empty placeholder for columns with no cards', () => {
    const wrapper = mountView()
    const empties = wrapper.findAll('[data-testid="paper-column-empty"]')
    // Today and Done are empty in the stub.
    expect(empties.length).toBe(2)
    expect(empties[0]?.text()).toContain('empty')
  })

  it('surfaces the overdue tagstamp on a column whose card count exceeds wipLimit', () => {
    const wrapper = mountView()
    const stamps = wrapper.findAll('[data-testid="paper-column-wip-warning"]')
    // Only Backlog is over its WIP limit (3 cards, limit 2).
    expect(stamps).toHaveLength(1)
    expect(stamps[0]?.text()).toBe('OVERDUE')
  })

  it('renders the board title from the store', () => {
    const wrapper = mountView()
    expect(wrapper.find('.paper-board-view__title').text()).toBe('Product Backlog')
  })

  it('does not fetch board data itself because the wrapping BoardView owns loading', () => {
    mountView()
    expect(mockBoardStore.fetchBoard).not.toHaveBeenCalled()
  })

  it('wires the paper column header as the column drag handle', () => {
    const wrapper = mountView()
    const handle = wrapper.get('[data-action="drag-column-handle"]')

    expect(handle.attributes('draggable')).toBe('true')
  })

  it('opens the card modal when a paper card is clicked', async () => {
    const wrapper = mountView()
    const firstColumn = wrapper.findAllComponents(PaperBoardColumn)[0]

    firstColumn?.vm.$emit('card-click', cardsByColumn.get('col-backlog')![0])
    await nextTick()

    expect(wrapper.find('[data-testid="paper-card-modal"]').text()).toContain('A')
  })

  it('blocks paper card drags that do not start from the card handle', async () => {
    const wrapper = mountView()
    const card = wrapper.get('[data-card-id="card-1"]')
    const dragStart = makeDragEvent('dragstart')

    card.element.dispatchEvent(dragStart)
    await nextTick()

    expect(dragStart.defaultPrevented).toBe(true)
    expect(mockBoardStore.moveCard).not.toHaveBeenCalled()
  })

  it('starts paper card drags from the explicit card handle', async () => {
    const wrapper = mountView()
    const handle = wrapper.get('[data-card-id="card-1"] [data-action="drag-card-handle"]')
    const dragStart = makeDragEvent('dragstart')

    handle.element.dispatchEvent(dragStart)
    await nextTick()

    expect(dragStart.defaultPrevented).toBe(false)
  })

  it('highlights the target lane while a paper card is dragged over it and moves on drop', async () => {
    const wrapper = mountView()
    const handle = wrapper.get('[data-card-id="card-1"] [data-action="drag-card-handle"]')
    handle.element.dispatchEvent(makeDragEvent('dragstart'))
    await nextTick()

    const targetLane = wrapper.get('[data-column-dnd-id="col-today"]')
    targetLane.element.dispatchEvent(makeDragEvent('dragover'))
    await nextTick()

    expect(wrapper.findComponent(PaperBoardColumn).exists()).toBe(true)
    expect(targetLane.classes()).toContain('paper-board-view__lane--drop-target')

    targetLane.element.dispatchEvent(makeDragEvent('drop'))
    await flushPromises()

    expect(mockBoardStore.moveCard).toHaveBeenCalledWith('board-1', 'card-1', 'col-today', 0)
  })

  it('reorders paper cards within the same column by dropping on another card', async () => {
    const wrapper = mountView()
    const handle = wrapper.get('[data-card-id="card-1"] [data-action="drag-card-handle"]')
    handle.element.dispatchEvent(makeDragEvent('dragstart'))
    await nextTick()

    const targetCard = wrapper.get('[data-card-id="card-3"]')
    targetCard.element.dispatchEvent(makeDragEvent('dragover'))
    targetCard.element.dispatchEvent(makeDragEvent('drop'))
    await flushPromises()

    expect(mockBoardStore.moveCard).toHaveBeenCalledWith('board-1', 'card-1', 'col-backlog', 1)
  })

  it('applies snap-scroll CSS class at tablet viewport', () => {
    mockViewportMode.value = 'tablet'
    const wrapper = mountView()
    const lanes = wrapper.find('[data-testid="paper-board-lanes"]')
    expect(lanes.classes()).toContain('paper-board-view__lanes--snap')
  })

  it('does not apply snap-scroll CSS class at desktop viewport', () => {
    mockViewportMode.value = 'desktop'
    const wrapper = mountView()
    const lanes = wrapper.find('[data-testid="paper-board-lanes"]')
    expect(lanes.classes()).not.toContain('paper-board-view__lanes--snap')
  })
})
