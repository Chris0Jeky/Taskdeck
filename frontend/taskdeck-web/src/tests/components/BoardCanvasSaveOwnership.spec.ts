import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import BoardCanvas from '../../components/board/BoardCanvas.vue'
import type { Column } from '../../types/board'

const columns: Column[] = [
  {
    id: 'column-a',
    boardId: 'board-1',
    name: 'Todo',
    position: 0,
    wipLimit: null,
    cardCount: 0,
    createdAt: '2026-09-20T10:00:00Z',
    updatedAt: '2026-09-20T10:00:00Z',
  },
  {
    id: 'column-b',
    boardId: 'board-1',
    name: 'Done',
    position: 1,
    wipLimit: null,
    cardCount: 0,
    createdAt: '2026-09-20T10:00:00Z',
    updatedAt: '2026-09-20T10:00:00Z',
  },
]

function mountCanvas() {
  return mount(BoardCanvas, {
    props: {
      sortedColumns: columns,
      cardsByColumn: new Map(),
      labels: [],
      boardId: 'board-1',
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
          props: ['column'],
          emits: ['card-editor-saving-change'],
          template: `
            <section :data-testid="'lane-' + column.id">
              <button
                :data-testid="'start-' + column.id"
                @click="$emit('card-editor-saving-change', true)"
              >start</button>
              <button
                :data-testid="'settle-' + column.id"
                @click="$emit('card-editor-saving-change', false)"
              >settle</button>
            </section>
          `,
        },
      },
    },
  })
}

describe('BoardCanvas assignment-save ownership', () => {
  it('stays globally saving until the final lane owner settles', async () => {
    const wrapper = mountCanvas()

    await wrapper.get('[data-testid="start-column-a"]').trigger('click')
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true]])

    await wrapper.get('[data-testid="start-column-b"]').trigger('click')
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true]])

    await wrapper.get('[data-testid="settle-column-a"]').trigger('click')
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true]])

    await wrapper.get('[data-testid="settle-column-b"]').trigger('click')
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true], [false]])
  })

  it('treats duplicate lane events as idempotent aggregate state', async () => {
    const wrapper = mountCanvas()

    await wrapper.get('[data-testid="start-column-a"]').trigger('click')
    await wrapper.get('[data-testid="start-column-a"]').trigger('click')
    await wrapper.get('[data-testid="settle-column-b"]').trigger('click')
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true]])

    await wrapper.get('[data-testid="settle-column-a"]').trigger('click')
    await wrapper.get('[data-testid="settle-column-a"]').trigger('click')
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true], [false]])
  })
})
