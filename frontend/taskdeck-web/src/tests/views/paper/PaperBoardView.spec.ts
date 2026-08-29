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
let routeLeaveGuard: (() => boolean | Promise<boolean>) | null = null
let routeUpdateGuard: (() => boolean | Promise<boolean>) | null = null

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

const emptyBoard: BoardDetail = {
  ...board,
  id: 'board-1',
  name: 'Fresh Board',
  columns: [],
}

const mockBoardStore = reactive({
  currentBoard: board as BoardDetail | null,
  currentBoardCards: allCards,
  cardsByColumn,
  currentBoardLabels: [],
  loading: false,
  error: null as string | null,
  fetchBoard: vi.fn(async () => {}),
  moveCard: vi.fn(async () => {}),
  reorderColumns: vi.fn(async () => {}),
  createColumn: vi.fn(async (_boardId: string, dto: { name: string }) => makeColumn({ id: `col-${dto.name}`, name: dto.name })),
})

vi.mock('vue-router', () => ({
  useRoute: () => routeMock,
  useRouter: () => routerMock,
  onBeforeRouteLeave: (guard: () => boolean | Promise<boolean>) => {
    routeLeaveGuard = guard
  },
  onBeforeRouteUpdate: (guard: () => boolean | Promise<boolean>) => {
    routeUpdateGuard = guard
  },
}))

vi.mock('../../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

vi.mock('../../../composables/useViewportMode', () => ({
  useViewportMode: () => ({ mode: mockViewportMode }),
}))

const mountedViews: ReturnType<typeof mount>[] = []

function mountView(props: Record<string, unknown> = {}) {
  const wrapper = mount(PaperBoardView, {
    attachTo: document.body,
    props,
    global: {
      stubs: {
        CardModal: {
          name: 'CardModal',
          props: ['card', 'isOpen', 'labels', 'presentation'],
          emits: ['dirty-change'],
          template: '<div v-if="isOpen" data-testid="paper-card-modal" :data-presentation="presentation">{{ card.title }}</div>',
        },
        TdDialog: {
          props: ['open', 'title', 'description'],
          emits: ['close'],
          template: '<div v-if="open" role="dialog"><slot /><slot name="footer" /></div>',
        },
      },
    },
  })
  mountedViews.push(wrapper)
  return wrapper
}

afterEach(() => {
  for (const wrapper of mountedViews.splice(0)) wrapper.unmount()
})

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
    mockBoardStore.createColumn.mockClear()
    mockBoardStore.currentBoard = board
    mockBoardStore.currentBoardCards = allCards
    mockBoardStore.cardsByColumn = cardsByColumn
    mockBoardStore.error = null
    mockBoardStore.loading = false
    mockViewportMode.value = 'desktop'
    routeMock.params.id = 'board-1'
    routeLeaveGuard = null
    routeUpdateGuard = null
    window.localStorage.removeItem('td.paper.board-density.v1')
    window.localStorage.removeItem('td.paper.board-column-width.v1')
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

  it('keeps board capture and review actions clickable without advertising dead letter keys', async () => {
    const wrapper = mountView()
    const buttons = wrapper.findAll('button')
    const capture = buttons.find((button) => button.text().includes('Capture here'))
    const review = buttons.find((button) => button.text().includes('Review'))

    expect(capture).toBeDefined()
    expect(review).toBeDefined()
    expect(capture?.find('kbd').exists()).toBe(false)
    expect(review?.find('kbd').exists()).toBe(false)

    await capture?.trigger('click')
    expect(routerMock.push).toHaveBeenCalledWith({
      name: 'workspace-inbox',
      query: { boardId: 'board-1' },
    })

    await review?.trigger('click')
    expect(routerMock.push).toHaveBeenCalledWith({
      name: 'workspace-review',
      query: { boardId: 'board-1' },
    })
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

  it('requires explicit confirmation before switching away from a dirty inspector', async () => {
    const wrapper = mountView()
    const firstColumn = wrapper.findAllComponents(PaperBoardColumn)[0]!

    firstColumn.vm.$emit('card-click', cardsByColumn.get('col-backlog')![0])
    await nextTick()
    wrapper.findComponent({ name: 'CardModal' }).vm.$emit('dirty-change', true)
    firstColumn.vm.$emit('card-click', cardsByColumn.get('col-backlog')![1])
    await nextTick()

    expect(wrapper.get('[data-testid="paper-card-modal"]').text()).toContain('A')
    expect(wrapper.find('[data-testid="card-switch-confirm"]').exists()).toBe(true)

    await wrapper.get('[data-testid="card-switch-cancel"]').trigger('click')
    await nextTick()
    expect(wrapper.get('[data-testid="paper-card-modal"]').text()).toContain('A')
    expect(wrapper.find('[data-testid="card-switch-confirm"]').exists()).toBe(false)

    firstColumn.vm.$emit('card-click', cardsByColumn.get('col-backlog')![1])
    await nextTick()
    await wrapper.get('[data-testid="card-switch-confirm"]').trigger('click')
    await nextTick()

    expect(wrapper.get('[data-testid="paper-card-modal"]').text()).toContain('B')
    expect(wrapper.find('[data-testid="card-switch-confirm"]').exists()).toBe(false)
  })

  it('guards dirty route navigation until discard or cancel is chosen', async () => {
    const wrapper = mountView()
    const firstColumn = wrapper.findAllComponents(PaperBoardColumn)[0]!
    firstColumn.vm.$emit('card-click', cardsByColumn.get('col-backlog')![0])
    await nextTick()
    wrapper.findComponent({ name: 'CardModal' }).vm.$emit('dirty-change', true)

    const cancelledNavigation = routeLeaveGuard!()
    await nextTick()
    expect(wrapper.get('[role="dialog"]').text()).toContain('Discard and leave')
    await wrapper.get('[data-testid="card-switch-cancel"]').trigger('click')
    await expect(cancelledNavigation).resolves.toBe(false)
    expect(wrapper.get('[data-testid="paper-card-modal"]').text()).toContain('A')

    const allowedNavigation = routeLeaveGuard!()
    await nextTick()
    await wrapper.get('[data-testid="card-switch-confirm"]').trigger('click')
    await expect(allowedNavigation).resolves.toBe(true)
    expect(wrapper.find('[data-testid="paper-card-modal"]').exists()).toBe(false)
  })

  it('guards reused board-route changes while the inspector is dirty', async () => {
    const wrapper = mountView()
    wrapper.findAllComponents(PaperBoardColumn)[0]!.vm.$emit(
      'card-click',
      cardsByColumn.get('col-backlog')![0],
    )
    await nextTick()
    wrapper.findComponent({ name: 'CardModal' }).vm.$emit('dirty-change', true)

    const navigation = routeUpdateGuard!()
    await nextTick()
    expect(wrapper.get('[data-testid="paper-card-modal"]').text()).toContain('A')
    await wrapper.get('[data-testid="card-switch-confirm"]').trigger('click')

    await expect(navigation).resolves.toBe(true)
    expect(wrapper.find('[data-testid="paper-card-modal"]').exists()).toBe(false)
  })

  it('requests the browser unload confirmation only for a dirty inspector', async () => {
    const wrapper = mountView()
    const cleanUnload = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(cleanUnload)
    expect(cleanUnload.defaultPrevented).toBe(false)

    wrapper.findAllComponents(PaperBoardColumn)[0]!.vm.$emit(
      'card-click',
      cardsByColumn.get('col-backlog')![0],
    )
    await nextTick()
    wrapper.findComponent({ name: 'CardModal' }).vm.$emit('dirty-change', true)

    const dirtyUnload = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(dirtyUnload)
    expect(dirtyUnload.defaultPrevented).toBe(true)
  })

  it('uses a board-preserving inspector on desktop and keeps the modal fallback on tablet', async () => {
    const wrapper = mountView()
    wrapper.findAllComponents(PaperBoardColumn)[0]?.vm.$emit('card-click', cardsByColumn.get('col-backlog')![0])
    await nextTick()

    expect(wrapper.get('[data-testid="paper-card-modal"]').attributes('data-presentation')).toBe('inspector')
    expect(wrapper.find('[data-testid="paper-board-lanes"]').exists()).toBe(true)

    mockViewportMode.value = 'tablet'
    await nextTick()

    expect(wrapper.get('[data-testid="paper-card-modal"]').attributes('data-presentation')).toBe('modal')
  })

  it('toggles and persists compact board density through a keyboard-accessible button', async () => {
    const wrapper = mountView()
    const toggle = wrapper.get('[data-testid="paper-board-density-toggle"]')

    expect(toggle.attributes('aria-pressed')).toBe('false')
    expect(wrapper.get('[data-surface="paper-board"]').attributes('data-density')).toBe('comfortable')

    await toggle.trigger('keydown', { key: 'Enter' })
    await toggle.trigger('click')

    expect(toggle.attributes('aria-pressed')).toBe('true')
    expect(wrapper.get('[data-surface="paper-board"]').attributes('data-density')).toBe('compact')
    expect(window.localStorage.getItem('td.paper.board-density.v1')).toBe('compact')
  })

  it('restores the persisted compact density when the board mounts', async () => {
    window.localStorage.setItem('td.paper.board-density.v1', 'compact')
    const wrapper = mountView()
    await nextTick()

    expect(wrapper.get('[data-surface="paper-board"]').attributes('data-density')).toBe('compact')
  })

  it('changes and persists named column-width presets through a labelled native select', async () => {
    const wrapper = mountView()
    const control = wrapper.get('[data-testid="paper-board-width-control"]')
    const select = wrapper.get('[data-testid="paper-board-width-select"]')
    const firstColumn = wrapper.get('.paper-board-column').element as HTMLElement

    expect(control.element.tagName).toBe('LABEL')
    expect(select.element.tagName).toBe('SELECT')
    expect(select.attributes('aria-label')).toBe('Column width')
    expect(select.findAll('option').map((option) => option.text())).toEqual(['Narrow', 'Standard', 'Wide'])
    expect(wrapper.get('[data-surface="paper-board"]').attributes('data-column-width')).toBe('standard')
    expect(firstColumn.style.width).toBe('280px')

    const bubbledKeydown = vi.fn()
    window.addEventListener('keydown', bubbledKeydown)
    const selectArrow = new KeyboardEvent('keydown', {
      key: 'ArrowRight',
      bubbles: true,
      cancelable: true,
    })
    select.element.dispatchEvent(selectArrow)
    window.removeEventListener('keydown', bubbledKeydown)
    expect(bubbledKeydown).not.toHaveBeenCalled()
    expect(selectArrow.defaultPrevented).toBe(false)

    await select.setValue('narrow')
    expect(wrapper.get('[data-surface="paper-board"]').attributes('data-column-width')).toBe('narrow')
    expect(firstColumn.style.width).toBe('240px')
    expect(window.localStorage.getItem('td.paper.board-column-width.v1')).toBe('narrow')

    await select.setValue('wide')
    expect(wrapper.get('[data-surface="paper-board"]').attributes('data-column-width')).toBe('wide')
    expect(firstColumn.style.width).toBe('340px')
    expect(window.localStorage.getItem('td.paper.board-column-width.v1')).toBe('wide')

    mockBoardStore.currentBoard = { ...board, updatedAt: new Date().toISOString() }
    await nextTick()
    expect((wrapper.get('.paper-board-column').element as HTMLElement).style.width).toBe('340px')
  })

  it('restores a column-width preference after the board unmounts and remounts', async () => {
    const firstMount = mountView()
    await firstMount.get('[data-testid="paper-board-width-select"]').setValue('wide')
    firstMount.unmount()
    mountedViews.splice(mountedViews.indexOf(firstMount), 1)

    const reloaded = mountView()
    await nextTick()

    expect(reloaded.get('[data-testid="paper-board-width-select"]').element)
      .toHaveProperty('value', 'wide')
    expect(reloaded.get('[data-surface="paper-board"]').attributes('data-column-width')).toBe('wide')
    expect((reloaded.get('.paper-board-column').element as HTMLElement).style.width).toBe('340px')
  })

  it('falls back to the standard column width for invalid stored values', async () => {
    window.localStorage.setItem('td.paper.board-column-width.v1', 'stretch-to-fit')
    const wrapper = mountView()
    await nextTick()

    expect(wrapper.get('[data-testid="paper-board-width-select"]').element)
      .toHaveProperty('value', 'standard')
    expect(wrapper.get('[data-surface="paper-board"]').attributes('data-column-width')).toBe('standard')
    expect((wrapper.get('.paper-board-column').element as HTMLElement).style.width).toBe('280px')
  })

  it('uses presets on desktop while retaining tablet snap sizing and phone stacking width', async () => {
    window.localStorage.setItem('td.paper.board-column-width.v1', 'wide')
    const wrapper = mountView()
    await nextTick()

    const columnWidth = () => (wrapper.get('.paper-board-column').element as HTMLElement).style.width
    const lanes = () => wrapper.get('[data-testid="paper-board-lanes"]')
    expect(columnWidth()).toBe('340px')
    expect(lanes().classes()).not.toContain('paper-board-view__lanes--snap')

    mockViewportMode.value = 'tablet'
    await nextTick()
    expect(columnWidth()).toBe('280px')
    expect(lanes().classes()).toContain('paper-board-view__lanes--snap')

    mockViewportMode.value = 'phone'
    await nextTick()
    expect(columnWidth()).toBe('100%')
    expect(lanes().classes()).not.toContain('paper-board-view__lanes--snap')

    mockViewportMode.value = 'desktop'
    await nextTick()
    expect(columnWidth()).toBe('340px')
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

describe('PaperBoardView — empty board (#1765)', () => {
  beforeEach(() => {
    routerMock.push.mockClear()
    mockBoardStore.createColumn.mockClear()
    mockBoardStore.currentBoard = emptyBoard
    mockBoardStore.currentBoardCards = []
    mockBoardStore.cardsByColumn = new Map()
    mockBoardStore.error = null
    mockBoardStore.loading = false
    mockViewportMode.value = 'desktop'
  })

  afterEach(() => {
    mockBoardStore.currentBoard = board
    mockBoardStore.currentBoardCards = allCards
    mockBoardStore.cardsByColumn = cardsByColumn
    document.body.innerHTML = ''
  })

  it('offers add-column affordances instead of a dead end when the board has no columns', () => {
    const wrapper = mountView()

    expect(wrapper.find('[data-testid="paper-board-empty"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-board-empty-column-name"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-board-empty-add-column"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-board-empty-starter-columns"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-board-lanes"]').exists()).toBe(false)
  })

  it('disables the add-column submit until a name is typed', async () => {
    const wrapper = mountView()
    const submit = wrapper.get('[data-testid="paper-board-empty-add-column"]')

    expect(submit.attributes('disabled')).toBeDefined()

    await wrapper.get('[data-testid="paper-board-empty-column-name"]').setValue('Backlog')

    expect(submit.attributes('disabled')).toBeUndefined()
  })

  it('creates the first column through boardStore.createColumn and clears the field', async () => {
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-empty-column-name"]').setValue('  Backlog  ')
    await wrapper.get('form.paper-board-view__empty-form').trigger('submit')
    await flushPromises()

    expect(mockBoardStore.createColumn).toHaveBeenCalledTimes(1)
    expect(mockBoardStore.createColumn).toHaveBeenCalledWith('board-1', { name: 'Backlog' })
    expect(
      (wrapper.get('[data-testid="paper-board-empty-column-name"]').element as HTMLInputElement).value,
    ).toBe('')
  })

  it('does not create a column for a whitespace-only name', async () => {
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-empty-column-name"]').setValue('   ')
    await wrapper.get('form.paper-board-view__empty-form').trigger('submit')
    await flushPromises()

    expect(mockBoardStore.createColumn).not.toHaveBeenCalled()
  })

  it('creates the three starter columns in order from one click', async () => {
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-empty-starter-columns"]').trigger('click')
    await flushPromises()

    expect(mockBoardStore.createColumn.mock.calls.map((call) => call[1])).toEqual([
      { name: 'To Do', position: 0 },
      { name: 'In Progress', position: 1 },
      { name: 'Done', position: 2 },
    ])
  })

  it('keeps the affordance on screen and surfaces an error when the create fails', async () => {
    mockBoardStore.createColumn.mockRejectedValueOnce(new Error('boom'))
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-empty-column-name"]').setValue('Backlog')
    await wrapper.get('form.paper-board-view__empty-form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('[data-testid="paper-board-empty"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="paper-board-empty-error"]').text()).toContain(
      'Could not create the column',
    )
  })

  it('still shows the board error banner when the board itself failed to load', () => {
    mockBoardStore.currentBoard = null
    mockBoardStore.error = 'Board not found'

    const wrapper = mountView()

    expect(wrapper.get('.paper-board-view__error').text()).toBe('Board not found')
    expect(wrapper.find('[data-testid="paper-board-empty"]').exists()).toBe(false)
  })
})
