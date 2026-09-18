import type { Card, Column } from '../types/board'

/**
 * Reconcile the card-count fields on a board detail with the cards returned by
 * the detail read. The API returns the board and cards as separate payloads,
 * so the client owns this small response-shaping step before committing them
 * together to the board store.
 */
export function applyBoardCardCounts(
  board: { columns: Array<Pick<Column, 'id' | 'cardCount'>> },
  cards: ReadonlyArray<Pick<Card, 'columnId'>>,
): void {
  const cardCounts = cards.reduce((counts, card) => {
    counts.set(card.columnId, (counts.get(card.columnId) ?? 0) + 1)
    return counts
  }, new Map<string, number>())

  board.columns.forEach((column) => {
    column.cardCount = cardCounts.get(column.id) ?? 0
  })
}
