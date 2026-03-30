import { ref, nextTick, type ComputedRef } from 'vue'
import { useBoardStore } from '../store/boardStore'
import type { Column } from '../types/board'

/**
 * Composable encapsulating card/column keyboard navigation state and actions
 * for the board view. Also provides keyboard-driven card movement between
 * columns (Alt+Left/Right) and reordering within a column (Alt+Up/Down).
 */
export function useBoardKeyboardNav(
  sortedColumns: ComputedRef<Column[]>,
  boardId?: () => string,
) {
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

  /**
   * Focus the card element matching selectedCardId in the DOM after a move.
   * Uses nextTick so the DOM has time to update after the store mutation.
   */
  async function focusSelectedCard() {
    await nextTick()
    if (!selectedCardId.value) return
    const el = document.querySelector(
      `[data-card-id="${selectedCardId.value}"]`,
    ) as HTMLElement | null
    if (el) {
      el.scrollIntoView({ block: 'nearest', inline: 'nearest' })
      el.focus()
    }
  }

  /**
   * Shared logic for moving the selected card to an adjacent column.
   * Private helper — not exported.
   */
  async function moveCardToAdjacentColumn(direction: 'next' | 'previous') {
    if (!boardId) return
    const columns = sortedColumns.value
    if (columns.length === 0) return
    if (!selectedCardId.value) return

    const currentColumn = columns[selectedColumnIndex.value]
    if (!currentColumn) return

    const targetColIndex =
      direction === 'next'
        ? selectedColumnIndex.value + 1
        : selectedColumnIndex.value - 1

    if (targetColIndex < 0 || targetColIndex >= columns.length) return

    const targetColumn = columns[targetColIndex]
    if (!targetColumn) return

    const cards = boardStore.cardsByColumn.get(currentColumn.id) || []
    const card = cards.find((c) => c.id === selectedCardId.value)
    if (!card) return

    const targetCards = boardStore.cardsByColumn.get(targetColumn.id) || []
    const targetPosition = targetCards.length

    try {
      await boardStore.moveCard(boardId(), card.id, targetColumn.id, targetPosition)
      selectedColumnIndex.value = targetColIndex
      await focusSelectedCard()
    } catch {
      // moveCard already surfaces toast errors via the store
    }
  }

  /**
   * Move the currently selected card to the next column (right).
   * The card is placed at the end of the target column.
   */
  async function moveCardToNextColumn() {
    await moveCardToAdjacentColumn('next')
  }

  /**
   * Move the currently selected card to the previous column (left).
   * The card is placed at the end of the target column.
   */
  async function moveCardToPreviousColumn() {
    await moveCardToAdjacentColumn('previous')
  }

  /**
   * Shared logic for reordering the selected card within its current column.
   * Private helper — not exported.
   */
  async function reorderCard(direction: 'up' | 'down') {
    if (!boardId) return
    const columns = sortedColumns.value
    if (columns.length === 0) return
    if (!selectedCardId.value) return

    const currentColumn = columns[selectedColumnIndex.value]
    if (!currentColumn) return

    const cards = boardStore.cardsByColumn.get(currentColumn.id) || []
    const cardIndex = cards.findIndex((c) => c.id === selectedCardId.value)

    if (direction === 'up') {
      if (cardIndex <= 0) return // already at top or not found
    } else {
      if (cardIndex === -1 || cardIndex >= cards.length - 1) return // already at bottom or not found
    }

    const card = cards[cardIndex]
    if (!card) return

    const targetPosition =
      direction === 'up'
        ? cards[cardIndex - 1]!.position
        : cards[cardIndex + 1]!.position

    try {
      await boardStore.moveCard(boardId(), card.id, currentColumn.id, targetPosition)
      await focusSelectedCard()
    } catch {
      // moveCard already surfaces toast errors via the store
    }
  }

  /**
   * Reorder the selected card up (lower position) within its current column.
   */
  async function moveCardUp() {
    await reorderCard('up')
  }

  /**
   * Reorder the selected card down (higher position) within its current column.
   */
  async function moveCardDown() {
    await reorderCard('down')
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
    moveCardToNextColumn,
    moveCardToPreviousColumn,
    moveCardUp,
    moveCardDown,
    resetSelection,
  }
}
