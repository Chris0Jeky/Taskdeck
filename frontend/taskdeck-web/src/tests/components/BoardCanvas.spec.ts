import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import BoardCanvas from '../../components/board/BoardCanvas.vue'

const columns = [
  {
    id: 'column-1',
    boardId: 'board-1',
    name: 'Todo',
    position: 0,
    wipLimit: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  },
]

const cards = [
  {
    id: 'card-1',
    boardId: 'board-1',
    columnId: 'column-1',
    title: 'First card',
    description: null,
    dueDate: null,
    position: 0,
    labels: [],
    isBlocked: false,
    blockReason: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    comments: [],
  },
]

describe('BoardCanvas', () => {
  it('renders column lanes with board-scoped props', () => {
    const wrapper = mount(BoardCanvas, {
      props: {
        sortedColumns: columns,
        cardsByColumn: new Map([['column-1', cards]]),
        labels: [],
        boardId: 'board-1',
        hasColumns: true,
        draggedColumn: null,
        dragOverColumnId: null,
        draggedCard: null,
        selectedCardId: 'card-1',
      },
      global: {
        stubs: {
          ColumnLane: {
            props: ['column', 'cards', 'boardId', 'selectedCardId'],
            template: '<div data-testid="column-lane">{{ column.name }}|{{ cards.length }}|{{ boardId }}|{{ selectedCardId }}</div>',
          },
        },
      },
    })

    expect(wrapper.get('[data-testid="column-lane"]').text()).toBe('Todo|1|board-1|card-1')
  })

  it('emits column drag lifecycle events from the wrapper lane', async () => {
    const wrapper = mount(BoardCanvas, {
      props: {
        sortedColumns: columns,
        cardsByColumn: new Map([['column-1', cards]]),
        labels: [],
        boardId: 'board-1',
        hasColumns: true,
        draggedColumn: columns[0],
        dragOverColumnId: 'column-1',
        draggedCard: null,
        selectedCardId: null,
      },
      global: {
        stubs: {
          ColumnLane: true,
        },
      },
    })

    const lane = wrapper.get('[data-column-dnd-id="column-1"]')
    const dragEvent = new Event('dragover') as DragEvent

    await lane.trigger('dragstart', dragEvent)
    await lane.trigger('dragover', dragEvent)
    await lane.trigger('dragleave')
    await lane.trigger('drop', dragEvent)
    await lane.trigger('dragend')

    expect(wrapper.emitted('columnDragStart')?.[0]?.[0]).toEqual(columns[0])
    expect(wrapper.emitted('columnDragOver')?.[0]?.[0]).toEqual(columns[0])
    expect(wrapper.emitted('columnDragLeave')).toHaveLength(1)
    expect(wrapper.emitted('columnDrop')?.[0]?.[0]).toEqual(columns[0])
    expect(wrapper.emitted('columnDragEnd')).toHaveLength(1)
    expect(lane.classes()).toContain('opacity-50')
    expect(lane.classes()).toContain('transform')
  })

  it('forwards card drag events from column lanes', async () => {
    const wrapper = mount(BoardCanvas, {
      props: {
        sortedColumns: columns,
        cardsByColumn: new Map([['column-1', cards]]),
        labels: [],
        boardId: 'board-1',
        hasColumns: true,
        draggedColumn: null,
        dragOverColumnId: null,
        draggedCard: cards[0],
        selectedCardId: null,
      },
      global: {
        stubs: {
          ColumnLane: {
            props: ['column', 'cards', 'labels', 'boardId', 'draggedCard', 'selectedCardId'],
            emits: ['card-drag-start', 'card-drag-end'],
            template: `
              <div>
                <button data-testid="card-drag-start" type="button" @click="$emit('card-drag-start', cards[0])">start</button>
                <button data-testid="card-drag-end" type="button" @click="$emit('card-drag-end')">end</button>
              </div>
            `,
          },
        },
      },
    })

    await wrapper.get('[data-testid="card-drag-start"]').trigger('click')
    await wrapper.get('[data-testid="card-drag-end"]').trigger('click')

    expect(wrapper.emitted('cardDragStart')?.[0]).toEqual([cards[0]])
    expect(wrapper.emitted('cardDragEnd')).toHaveLength(1)
  })

  it('shows the empty state when the board has no columns', () => {
    const wrapper = mount(BoardCanvas, {
      props: {
        sortedColumns: [],
        cardsByColumn: new Map(),
        labels: [],
        boardId: 'board-1',
        hasColumns: false,
        draggedColumn: null,
        dragOverColumnId: null,
        draggedCard: null,
        selectedCardId: null,
      },
      global: {
        stubs: {
          ColumnLane: true,
        },
      },
    })

    expect(wrapper.text()).toContain('No columns yet')
    expect(wrapper.text()).toContain('Click "Add Column" to get started')
  })
})
