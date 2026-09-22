import { defineComponent, inject, nextTick } from 'vue'
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import BoardCanvas from '../../components/board/BoardCanvas.vue'
import {
  assignmentSaveRegistryKey,
  type AssignmentSaveRegistry,
} from '../../composables/useAssignmentSaveRegistry'
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

const releases = new Map<string, Array<() => void>>()

const ColumnLaneStub = defineComponent({
  name: 'ColumnLane',
  props: { column: { type: Object, required: true } },
  setup(props) {
    const registry = inject(assignmentSaveRegistryKey) as AssignmentSaveRegistry
    function begin() {
      const owner = (props.column as Column).id
      const release = registry.begin(owner)
      const owners = releases.get(owner) ?? []
      owners.push(release)
      releases.set(owner, owners)
    }
    return { begin }
  },
  template: '<button :data-testid="`begin-${column.id}`" @click="begin">begin</button>',
})

function mountCanvas() {
  releases.clear()
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
    global: { stubs: { ColumnLane: ColumnLaneStub } },
  })
}

describe('BoardCanvas assignment-save lifecycle ownership', () => {
  it('stays saving after the owning lane unmounts and settles from the operation closure', async () => {
    const wrapper = mountCanvas()

    await wrapper.get('[data-testid="begin-column-a"]').trigger('click')
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true]])

    await wrapper.setProps({ sortedColumns: [columns[1]!] })
    expect(wrapper.find('[data-testid="begin-column-a"]').exists()).toBe(false)
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true]])

    releases.get('column-a')![0]!()
    await nextTick()
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true], [false]])
  })

  it('preserves multi-lane aggregation until the final operation settles', async () => {
    const wrapper = mountCanvas()

    await wrapper.get('[data-testid="begin-column-a"]').trigger('click')
    await wrapper.get('[data-testid="begin-column-b"]').trigger('click')
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true]])

    releases.get('column-a')![0]!()
    await nextTick()
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true]])

    releases.get('column-b')![0]!()
    await nextTick()
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true], [false]])
  })

  it('resets on board replacement and an old release cannot clear the new owner', async () => {
    const wrapper = mountCanvas()

    await wrapper.get('[data-testid="begin-column-a"]').trigger('click')
    const oldRelease = releases.get('column-a')![0]!

    await wrapper.setProps({ boardId: 'board-2' })
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true], [false]])

    await wrapper.get('[data-testid="begin-column-a"]').trigger('click')
    const newRelease = releases.get('column-a')![1]!
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true], [false], [true]])

    oldRelease()
    await nextTick()
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true], [false], [true]])

    newRelease()
    await nextTick()
    expect(wrapper.emitted('cardEditorSavingChange')).toEqual([[true], [false], [true], [false]])
  })
})
