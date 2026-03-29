import { ref, type ComputedRef } from 'vue'
import type { Column, Card } from '../types/board'
import { useBoardStore } from '../store/boardStore'

/**
 * Composable encapsulating all drag-and-drop state and handlers for the board view.
 *
 * Drag-handle safety contract: column drag only starts when the event target is
 * inside an element marked with `data-action="drag-column-handle"`.
 */
export function useBoardDragDrop(boardId: () => string, sortedColumns: ComputedRef<Column[]>) {
  const boardStore = useBoardStore()

  const draggedColumn = ref<Column | null>(null)
  const dragOverColumnId = ref<string | null>(null)
  const draggedCard = ref<Card | null>(null)

  function isColumnDragHandleTarget(target: EventTarget | null): boolean {
    return target instanceof Element && target.closest('[data-action="drag-column-handle"]') !== null
  }

  function handleColumnDragStart(column: Column, event: DragEvent) {
    if (!isColumnDragHandleTarget(event.target)) {
      event.preventDefault()
      return
    }

    draggedColumn.value = column
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move'
      event.dataTransfer.setData('text/plain', column.id)
    }
  }

  function handleColumnDragEnd() {
    draggedColumn.value = null
    dragOverColumnId.value = null
  }

  function handleColumnDragOver(column: Column, event: DragEvent) {
    event.preventDefault()
    if (!draggedColumn.value || draggedColumn.value.id === column.id) {
      dragOverColumnId.value = null
      return
    }
    dragOverColumnId.value = column.id
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move'
    }
  }

  function handleColumnDragLeave() {
    dragOverColumnId.value = null
  }

  async function handleColumnDrop(targetColumn: Column | undefined, event: DragEvent) {
    event.preventDefault()
    dragOverColumnId.value = null

    if (!draggedColumn.value || !boardStore.currentBoard || !targetColumn) return
    if (draggedColumn.value.id === targetColumn.id) return

    try {
      const columns = sortedColumns.value
      const draggedIndex = columns.findIndex((c) => c.id === draggedColumn.value!.id)
      const targetIndex = columns.findIndex((c) => c.id === targetColumn.id)

      if (draggedIndex === -1 || targetIndex === -1) return

      const reordered = [...columns]
      const [removed] = reordered.splice(draggedIndex, 1)
      if (!removed) return
      reordered.splice(targetIndex, 0, removed)

      const columnIds = reordered.map((col) => col.id)
      await boardStore.reorderColumns(boardId(), columnIds)
    } catch (error) {
      console.error('Failed to reorder columns:', error)
    }
  }

  function handleCardDragStart(card: Card) {
    draggedCard.value = card
  }

  function handleCardDragEnd() {
    draggedCard.value = null
  }

  return {
    draggedColumn,
    dragOverColumnId,
    draggedCard,
    handleColumnDragStart,
    handleColumnDragEnd,
    handleColumnDragOver,
    handleColumnDragLeave,
    handleColumnDrop,
    handleCardDragStart,
    handleCardDragEnd,
  }
}
