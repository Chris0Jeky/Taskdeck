import { describe, it, expect, vi, beforeEach } from 'vitest'
import { computed, ref } from 'vue'
import { useBoardDragDrop } from '../../composables/useBoardDragDrop'
import type { Column } from '../../types/board'

const mockBoardStore = {
  currentBoard: { id: 'board-1', columns: [] },
  reorderColumns: vi.fn(async () => {}),
}

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

function makeColumn(id: string, position: number): Column {
  return {
    id,
    boardId: 'board-1',
    name: `Col ${id}`,
    position,
    wipLimit: null,
    cardCount: 0,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  }
}

function makeDragEvent(target: EventTarget | null = null): DragEvent {
  const event = new Event('drag') as unknown as DragEvent
  Object.defineProperty(event, 'preventDefault', { value: vi.fn() })
  Object.defineProperty(event, 'target', { value: target, configurable: true })
  Object.defineProperty(event, 'dataTransfer', {
    value: { effectAllowed: '', dropEffect: '', setData: vi.fn() },
    configurable: true,
  })
  return event
}

describe('useBoardDragDrop', () => {
  const columns = [makeColumn('c1', 0), makeColumn('c2', 1)]
  const sortedColumns = computed(() => columns)
  const boardIdRef = ref('board-1')

  beforeEach(() => {
    vi.clearAllMocks()
    mockBoardStore.currentBoard = { id: 'board-1', columns: [] }
  })

  it('blocks column drag when target lacks the drag-handle attribute', () => {
    const dnd = useBoardDragDrop(() => boardIdRef.value, sortedColumns)
    const event = makeDragEvent(document.createElement('div'))

    dnd.handleColumnDragStart(columns[0]!, event)

    expect(event.preventDefault).toHaveBeenCalled()
    expect(dnd.draggedColumn.value).toBeNull()
  })

  it('allows column drag when target has the drag-handle attribute', () => {
    const dnd = useBoardDragDrop(() => boardIdRef.value, sortedColumns)
    const handle = document.createElement('div')
    handle.setAttribute('data-action', 'drag-column-handle')
    document.body.appendChild(handle)

    const event = makeDragEvent(handle)

    dnd.handleColumnDragStart(columns[0]!, event)

    expect(dnd.draggedColumn.value).toEqual(columns[0])
    document.body.removeChild(handle)
  })

  it('resets state on column drag end', () => {
    const dnd = useBoardDragDrop(() => boardIdRef.value, sortedColumns)
    dnd.draggedColumn.value = columns[0]!
    dnd.dragOverColumnId.value = 'c2'

    dnd.handleColumnDragEnd()

    expect(dnd.draggedColumn.value).toBeNull()
    expect(dnd.dragOverColumnId.value).toBeNull()
  })

  it('tracks card drag start and end', () => {
    const dnd = useBoardDragDrop(() => boardIdRef.value, sortedColumns)
    const card = { id: 'card-1' } as any

    dnd.handleCardDragStart(card)
    expect(dnd.draggedCard.value).toEqual(card)

    dnd.handleCardDragEnd()
    expect(dnd.draggedCard.value).toBeNull()
  })

  it('sets dragOverColumnId during column drag over for a different column', () => {
    const dnd = useBoardDragDrop(() => boardIdRef.value, sortedColumns)
    dnd.draggedColumn.value = columns[0]!

    const event = makeDragEvent()
    dnd.handleColumnDragOver(columns[1]!, event)

    expect(dnd.dragOverColumnId.value).toBe('c2')
  })

  it('clears dragOverColumnId when dragging over same column', () => {
    const dnd = useBoardDragDrop(() => boardIdRef.value, sortedColumns)
    dnd.draggedColumn.value = columns[0]!

    const event = makeDragEvent()
    dnd.handleColumnDragOver(columns[0]!, event)

    expect(dnd.dragOverColumnId.value).toBeNull()
  })

  it('calls reorderColumns on valid column drop', async () => {
    const dnd = useBoardDragDrop(() => boardIdRef.value, sortedColumns)
    dnd.draggedColumn.value = columns[0]!

    const event = makeDragEvent()
    await dnd.handleColumnDrop(columns[1], event)

    expect(mockBoardStore.reorderColumns).toHaveBeenCalledWith('board-1', ['c2', 'c1'])
  })

  it('does not reorder when dropping column on itself', async () => {
    const dnd = useBoardDragDrop(() => boardIdRef.value, sortedColumns)
    dnd.draggedColumn.value = columns[0]!

    const event = makeDragEvent()
    await dnd.handleColumnDrop(columns[0], event)

    expect(mockBoardStore.reorderColumns).not.toHaveBeenCalled()
  })
})
