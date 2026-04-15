import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import BoardCanvas from '../../components/board/BoardCanvas.vue'
import type { Column, Card, Label } from '../../types/board'

function makeColumn(overrides: Partial<Column> = {}): Column {
  return {
    id: 'col-1',
    boardId: 'board-1',
    name: 'Todo',
    position: 0,
    wipLimit: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  }
}

function makeCard(id: string, columnId: string): Card {
  return {
    id,
    boardId: 'board-1',
    columnId,
    title: `Card ${id}`,
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

const columnLaneStub = {
  props: ['column', 'cards', 'labels', 'boardId', 'allColumns', 'draggedCard', 'selectedCardId'],
  template: '<div :data-column-id="column.id" class="stub-column"><span>{{ column.name }}</span> ({{ cards.length }} cards)</div>',
}

function mountCanvas(props: Partial<InstanceType<typeof BoardCanvas>['$props']> = {}) {
  return mount(BoardCanvas, {
    props: {
      sortedColumns: [],
      cardsByColumn: new Map(),
      labels: [] as Label[],
      boardId: 'board-1',
      hasColumns: false,
      draggedColumn: null,
      dragOverColumnId: null,
      draggedCard: null,
      selectedCardId: null,
      ...props,
    },
    global: {
      stubs: { ColumnLane: columnLaneStub },
    },
  })
}

describe('BoardCanvas — empty state', () => {
  it('shows empty state when hasColumns is false', () => {
    const wrapper = mountCanvas({ hasColumns: false, sortedColumns: [] })

    expect(wrapper.find('.td-board-canvas__empty').exists()).toBe(true)
    expect(wrapper.text()).toContain('No columns yet')
    expect(wrapper.text()).toContain('Click "Add Column" to get started')
  })

  it('does not show empty state when columns exist', () => {
    const columns = [makeColumn()]
    const wrapper = mountCanvas({
      hasColumns: true,
      sortedColumns: columns,
    })

    expect(wrapper.find('.td-board-canvas__empty').exists()).toBe(false)
  })
})

describe('BoardCanvas — column rendering', () => {
  it('renders one ColumnLane stub per sorted column', () => {
    const columns = [
      makeColumn({ id: 'col-1', name: 'Todo', position: 0 }),
      makeColumn({ id: 'col-2', name: 'Done', position: 1 }),
    ]

    const wrapper = mountCanvas({
      hasColumns: true,
      sortedColumns: columns,
    })

    const lanes = wrapper.findAll('.stub-column')
    expect(lanes).toHaveLength(2)
    expect(lanes[0].text()).toContain('Todo')
    expect(lanes[1].text()).toContain('Done')
  })

  it('passes correct cards count to each column lane', () => {
    const columns = [
      makeColumn({ id: 'col-1', name: 'Todo', position: 0 }),
      makeColumn({ id: 'col-2', name: 'Done', position: 1 }),
    ]
    const cardsByColumn = new Map([
      ['col-1', [makeCard('c1', 'col-1'), makeCard('c2', 'col-1')]],
      ['col-2', [makeCard('c3', 'col-2')]],
    ])

    const wrapper = mountCanvas({
      hasColumns: true,
      sortedColumns: columns,
      cardsByColumn,
    })

    const lanes = wrapper.findAll('.stub-column')
    expect(lanes[0].text()).toContain('2 cards')
    expect(lanes[1].text()).toContain('1 cards')
  })

  it('renders empty card list for columns with no cards', () => {
    const columns = [makeColumn({ id: 'col-1', name: 'Empty' })]
    const cardsByColumn = new Map<string, Card[]>()

    const wrapper = mountCanvas({
      hasColumns: true,
      sortedColumns: columns,
      cardsByColumn,
    })

    expect(wrapper.text()).toContain('0 cards')
  })
})

describe('BoardCanvas — drag state classes', () => {
  it('applies opacity class to dragged column', () => {
    const columns = [
      makeColumn({ id: 'col-1', name: 'Dragging' }),
      makeColumn({ id: 'col-2', name: 'Other' }),
    ]
    const draggedColumn = columns[0]

    const wrapper = mountCanvas({
      hasColumns: true,
      sortedColumns: columns,
      draggedColumn,
    })

    const dndWrappers = wrapper.findAll('[data-column-dnd-id]')
    expect(dndWrappers[0].classes()).toContain('opacity-50')
    expect(dndWrappers[1].classes()).not.toContain('opacity-50')
  })

  it('applies scale class to drag-over target column', () => {
    const columns = [
      makeColumn({ id: 'col-1', name: 'Source' }),
      makeColumn({ id: 'col-2', name: 'Target' }),
    ]

    const wrapper = mountCanvas({
      hasColumns: true,
      sortedColumns: columns,
      dragOverColumnId: 'col-2',
    })

    const dndWrappers = wrapper.findAll('[data-column-dnd-id]')
    expect(dndWrappers[0].classes()).not.toContain('transform')
    expect(dndWrappers[1].classes()).toContain('transform')
    expect(dndWrappers[1].classes()).toContain('scale-105')
  })
})

describe('BoardCanvas — drag event emissions', () => {
  it('emits columnDragOver when dragover event fires on a column wrapper', async () => {
    const columns = [makeColumn({ id: 'col-1' })]
    const wrapper = mountCanvas({
      hasColumns: true,
      sortedColumns: columns,
    })

    const colWrapper = wrapper.find('[data-column-dnd-id="col-1"]')
    await colWrapper.trigger('dragover')

    expect(wrapper.emitted('columnDragOver')).toBeTruthy()
  })

  it('emits columnDragLeave when dragleave event fires', async () => {
    const columns = [makeColumn({ id: 'col-1' })]
    const wrapper = mountCanvas({
      hasColumns: true,
      sortedColumns: columns,
    })

    const colWrapper = wrapper.find('[data-column-dnd-id="col-1"]')
    await colWrapper.trigger('dragleave')

    expect(wrapper.emitted('columnDragLeave')).toBeTruthy()
  })

  it('emits columnDrop when drop event fires on a column wrapper', async () => {
    const columns = [makeColumn({ id: 'col-1' })]
    const wrapper = mountCanvas({
      hasColumns: true,
      sortedColumns: columns,
    })

    const colWrapper = wrapper.find('[data-column-dnd-id="col-1"]')
    await colWrapper.trigger('drop')

    expect(wrapper.emitted('columnDrop')).toBeTruthy()
  })

  it('emits columnDragEnd when dragend event fires', async () => {
    const columns = [makeColumn({ id: 'col-1' })]
    const wrapper = mountCanvas({
      hasColumns: true,
      sortedColumns: columns,
    })

    const colWrapper = wrapper.find('[data-column-dnd-id="col-1"]')
    await colWrapper.trigger('dragend')

    expect(wrapper.emitted('columnDragEnd')).toBeTruthy()
  })
})

describe('BoardCanvas — accessibility', () => {
  it('assigns role="group" and aria-label to each column wrapper', () => {
    const columns = [makeColumn({ id: 'col-1', name: 'Todo' })]
    const wrapper = mountCanvas({
      hasColumns: true,
      sortedColumns: columns,
    })

    const colWrapper = wrapper.find('[data-column-dnd-id="col-1"]')
    expect(colWrapper.attributes('role')).toBe('group')
    expect(colWrapper.attributes('aria-label')).toBe('Column: Todo')
  })
})
