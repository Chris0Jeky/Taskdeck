import { ref, nextTick, type ComputedRef } from 'vue'
import { useBoardStore } from '../store/boardStore'
import type { Card, Column } from '../types/board'

/**
 * Composable encapsulating card/column keyboard navigation state and actions
 * for the board view. Also provides keyboard-driven card movement between
 * columns (Alt+Left/Right) and reordering within a column (Alt+Up/Down).
 *
 * Selection movement is focus-aware (roving pattern): when DOM focus sits
 * inside a card while J/K/H/L (or plain arrows) change the selection, focus
 * follows the newly selected card's activator so Enter always opens the card
 * the user sees highlighted. See `withFocusFollowingSelection`.
 */
export function useBoardKeyboardNav(
  sortedColumns: ComputedRef<Column[]>,
  boardId?: () => string,
  cardsByColumnSource?: ComputedRef<Map<string, Card[]>>,
  isColumnNavigable?: (columnId: string) => boolean,
) {
  const boardStore = useBoardStore()

  const selectedCardId = ref<string | null>(null)
  const selectedColumnIndex = ref<number>(0)

  function cardActivator(cardElement: HTMLElement): HTMLElement {
    return cardElement.querySelector<HTMLElement>('[data-action="open-card"]') ?? cardElement
  }

  function allCardsForColumn(columnId: string): Card[] {
    return cardsByColumnSource?.value.get(columnId) ?? boardStore.cardsByColumn.get(columnId) ?? []
  }

  function cardsForColumn(columnId: string): Card[] {
    if (isColumnNavigable && !isColumnNavigable(columnId)) return []
    return allCardsForColumn(columnId)
  }

  function focusIsWithinBoardCard(): boolean {
    const active = document.activeElement
    return active instanceof Element && active.closest('[data-card-id]') !== null
  }

  /**
   * Roving focus for selection movement: when a board selection shortcut
   * (J/K/H/L or plain arrows) changes the selected card while DOM focus sits
   * inside a card (e.g. on its `data-action="open-card"` activator), focus
   * follows the new selection so the focused card and the visible selection
   * highlight can never disagree — the focused opener's own Enter handler and
   * the global Enter shortcut then open the same card. When focus is elsewhere
   * (body, composer, modal), selection movement leaves focus untouched.
   */
  function withFocusFollowingSelection(applySelection: () => void) {
    const focusWasWithinCard = focusIsWithinBoardCard()
    const previousCardId = selectedCardId.value
    applySelection()
    if (
      focusWasWithinCard &&
      selectedCardId.value !== null &&
      selectedCardId.value !== previousCardId
    ) {
      void focusSelectedCard()
    }
  }

  function selectNextCard() {
    withFocusFollowingSelection(applySelectNextCard)
  }

  function selectPreviousCard() {
    withFocusFollowingSelection(applySelectPreviousCard)
  }

  function selectNextColumn() {
    withFocusFollowingSelection(applySelectNextColumn)
  }

  function selectPreviousColumn() {
    withFocusFollowingSelection(applySelectPreviousColumn)
  }

  function applySelectNextCard() {
    const columns = sortedColumns.value
    if (columns.length === 0) return

    const currentColumn = columns[selectedColumnIndex.value]
    if (!currentColumn) return

    const cards = cardsForColumn(currentColumn.id)
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

  function applySelectPreviousCard() {
    const columns = sortedColumns.value
    if (columns.length === 0) return

    const currentColumn = columns[selectedColumnIndex.value]
    if (!currentColumn) return

    const cards = cardsForColumn(currentColumn.id)
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

  function applySelectNextColumn() {
    const columns = sortedColumns.value
    if (columns.length === 0) return

    if (selectedColumnIndex.value < columns.length - 1) {
      selectedColumnIndex.value++
      const newColumn = columns[selectedColumnIndex.value]
      if (newColumn) {
        const cards = cardsForColumn(newColumn.id)
        selectedCardId.value = cards.length > 0 ? (cards[0]?.id || null) : null
      }
    }
  }

  function applySelectPreviousColumn() {
    const columns = sortedColumns.value
    if (columns.length === 0) return

    if (selectedColumnIndex.value > 0) {
      selectedColumnIndex.value--
      const newColumn = columns[selectedColumnIndex.value]
      if (newColumn) {
        const cards = cardsForColumn(newColumn.id)
        selectedCardId.value = cards.length > 0 ? (cards[0]?.id || null) : null
      }
    }
  }

  function openSelectedCard() {
    const columns = sortedColumns.value
    if (columns.length === 0) return

    const currentColumn = columns[selectedColumnIndex.value]
    if (!currentColumn) return

    const cards = cardsForColumn(currentColumn.id)
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
    cardActivator(cardElement).click()
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

    const openComposerAndFocus = () => {
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

    if (columnElement.dataset.collapsed === 'true') {
      const expandButton = columnElement.querySelector(
        '[data-action="expand-column"]'
      ) as HTMLButtonElement | null
      if (!expandButton) return
      expandButton.click()
      window.setTimeout(openComposerAndFocus, 0)
      return
    }

    openComposerAndFocus()
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
      cardActivator(el).focus()
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

    const cards = allCardsForColumn(currentColumn.id)
    const card = cards.find((c) => c.id === selectedCardId.value)
    if (!card) return

    const targetCards = allCardsForColumn(targetColumn.id)
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

    const cards = allCardsForColumn(currentColumn.id)
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
