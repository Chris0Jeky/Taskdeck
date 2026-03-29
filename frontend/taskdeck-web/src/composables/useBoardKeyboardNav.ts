import { ref, type ComputedRef } from 'vue'
import { useBoardStore } from '../store/boardStore'
import type { Column } from '../types/board'

/**
 * Composable encapsulating card/column keyboard navigation state and actions
 * for the board view.
 */
export function useBoardKeyboardNav(sortedColumns: ComputedRef<Column[]>) {
  const boardStore = useBoardStore()

  const selectedCardId = ref<string | null>(null)
  const selectedColumnIndex = ref<number>(0)

  function selectNextCard() {
    const columns = sortedColumns.value
    if (columns.length === 0) return

    const currentColumn = columns[selectedColumnIndex.value]
    if (!currentColumn) return

    const cards = boardStore.cardsByColumn.get(currentColumn.id) || []
    if (cards.length === 0) return

    if (!selectedCardId.value) {
      selectedCardId.value = cards[0]?.id || null
      return
    }

    const currentIndex = cards.findIndex(c => c.id === selectedCardId.value)
    if (currentIndex < cards.length - 1) {
      selectedCardId.value = cards[currentIndex + 1]?.id || null
    }
  }

  function selectPreviousCard() {
    const columns = sortedColumns.value
    if (columns.length === 0) return

    const currentColumn = columns[selectedColumnIndex.value]
    if (!currentColumn) return

    const cards = boardStore.cardsByColumn.get(currentColumn.id) || []
    if (cards.length === 0) return

    if (!selectedCardId.value) {
      selectedCardId.value = cards[cards.length - 1]?.id || null
      return
    }

    const currentIndex = cards.findIndex(c => c.id === selectedCardId.value)
    if (currentIndex > 0) {
      selectedCardId.value = cards[currentIndex - 1]?.id || null
    }
  }

  function selectNextColumn() {
    const columns = sortedColumns.value
    if (columns.length === 0) return

    if (selectedColumnIndex.value < columns.length - 1) {
      selectedColumnIndex.value++
      const newColumn = columns[selectedColumnIndex.value]
      if (newColumn) {
        const cards = boardStore.cardsByColumn.get(newColumn.id) || []
        selectedCardId.value = cards.length > 0 ? (cards[0]?.id || null) : null
      }
    }
  }

  function selectPreviousColumn() {
    const columns = sortedColumns.value
    if (columns.length === 0) return

    if (selectedColumnIndex.value > 0) {
      selectedColumnIndex.value--
      const newColumn = columns[selectedColumnIndex.value]
      if (newColumn) {
        const cards = boardStore.cardsByColumn.get(newColumn.id) || []
        selectedCardId.value = cards.length > 0 ? (cards[0]?.id || null) : null
      }
    }
  }

  function openSelectedCard() {
    const columns = sortedColumns.value
    if (columns.length === 0) return

    const currentColumn = columns[selectedColumnIndex.value]
    if (!currentColumn) return

    const cards = boardStore.cardsByColumn.get(currentColumn.id) || []
    if (cards.length === 0) return

    if (!selectedCardId.value) {
      selectedCardId.value = cards[0]?.id || null
    }

    const card = cards.find(c => c.id === selectedCardId.value)
    if (!card) return

    const cardElement = document.querySelector(
      `[data-card-id="${card.id}"]`
    ) as HTMLElement | null

    if (!cardElement) return

    cardElement.scrollIntoView({ block: 'nearest', inline: 'nearest' })
    cardElement.click()
  }

  function createCardInSelectedColumn() {
    const columns = sortedColumns.value
    if (columns.length === 0) return

    const currentColumn = columns[selectedColumnIndex.value]
    if (!currentColumn) return

    const columnElement = document.querySelector(
      `[data-column-id="${currentColumn.id}"]`
    ) as HTMLElement | null

    if (!columnElement) return

    const toggleButton = columnElement.querySelector(
      '[data-action="toggle-add-card"]'
    ) as HTMLButtonElement | null

    if (!toggleButton) return

    toggleButton.click()

    window.setTimeout(() => {
      const cardInput = columnElement.querySelector(
        '[data-action="add-card-input"]'
      ) as HTMLTextAreaElement | null
      cardInput?.focus()
    }, 0)
  }

  function resetSelection() {
    selectedCardId.value = null
    selectedColumnIndex.value = 0
  }

  return {
    selectedCardId,
    selectedColumnIndex,
    selectNextCard,
    selectPreviousCard,
    selectNextColumn,
    selectPreviousColumn,
    openSelectedCard,
    createCardInSelectedColumn,
    resetSelection,
  }
}
