import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import PaperBoardView from '../../../views/paper/PaperBoardView.vue'
import PaperBoardColumn from '../../../views/paper/PaperBoardColumn.vue'
import type { BoardDetail, Card, Column } from '../../../types/board'

const routerMock = { push: vi.fn() }
const routeMock = reactive({ params: { id: 'board-1' } })

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

function makeCard(id: string, columnId: string, title = 'card'): Card {
  return {
    id,
    boardId: 'board-1',
    columnId,
    title,
    description: '',
    dueDate: null,
    isBlocked: false,
    blockReason: null,
    position: 0,
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
    makeCard('card-1', 'col-backlog', 'A'),
    makeCard('card-2', 'col-backlog', 'B'),
    makeCard('card-3', 'col-backlog', 'C'),
  ]],
  ['col-today', []],
  ['col-progress', [makeCard('card-4', 'col-progress', 'D')]],
  ['col-done', []],
])

const mockBoardStore = reactive({
  currentBoard: board,
  cardsByColumn,
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

function mountView() {
  return mount(PaperBoardView, {
    attachTo: document.body,
  })
}

describe('PaperBoardView', () => {
  beforeEach(() => {
    routerMock.push.mockClear()
    mockBoardStore.fetchBoard.mockClear()
    mockBoardStore.moveCard.mockClear()
    mockBoardStore.error = null
    mockBoardStore.loading = false
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
})
