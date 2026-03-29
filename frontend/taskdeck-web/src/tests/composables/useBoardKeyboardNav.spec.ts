import { describe, it, expect, vi, beforeEach } from 'vitest'
import { computed } from 'vue'
import { useBoardKeyboardNav } from '../../composables/useBoardKeyboardNav'
import type { Column, Card } from '../../types/board'

const cardsByColumn = new Map<string, Card[]>()
const mockBoardStore = {
  cardsByColumn,
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

function makeCard(id: string, columnId: string, position: number): Card {
  return {
    id,
    boardId: 'board-1',
    columnId,
    title: `Card ${id}`,
    description: '',
    dueDate: null,
    isBlocked: false,
    blockReason: null,
    position,
    labels: [],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  }
}

describe('useBoardKeyboardNav', () => {
  const col1 = makeColumn('c1', 0)
  const col2 = makeColumn('c2', 1)
  const columns = [col1, col2]
  const sortedColumns = computed(() => columns)

  const card1 = makeCard('card-1', 'c1', 0)
  const card2 = makeCard('card-2', 'c1', 1)
  const card3 = makeCard('card-3', 'c2', 0)

  beforeEach(() => {
    cardsByColumn.clear()
    cardsByColumn.set('c1', [card1, card2])
    cardsByColumn.set('c2', [card3])
  })

  it('selects the first card on selectNextCard when nothing is selected', () => {
    const nav = useBoardKeyboardNav(sortedColumns)
    nav.selectNextCard()
    expect(nav.selectedCardId.value).toBe('card-1')
  })

  it('advances to the next card on selectNextCard', () => {
    const nav = useBoardKeyboardNav(sortedColumns)
    nav.selectedCardId.value = 'card-1'
    nav.selectNextCard()
    expect(nav.selectedCardId.value).toBe('card-2')
  })

  it('stays on last card when selectNextCard reaches the end', () => {
    const nav = useBoardKeyboardNav(sortedColumns)
    nav.selectedCardId.value = 'card-2'
    nav.selectNextCard()
    expect(nav.selectedCardId.value).toBe('card-2')
  })

  it('selects last card on selectPreviousCard when nothing is selected', () => {
    const nav = useBoardKeyboardNav(sortedColumns)
    nav.selectPreviousCard()
    expect(nav.selectedCardId.value).toBe('card-2')
  })

  it('moves to previous card on selectPreviousCard', () => {
    const nav = useBoardKeyboardNav(sortedColumns)
    nav.selectedCardId.value = 'card-2'
    nav.selectPreviousCard()
    expect(nav.selectedCardId.value).toBe('card-1')
  })

  it('stays on first card when selectPreviousCard reaches the beginning', () => {
    const nav = useBoardKeyboardNav(sortedColumns)
    nav.selectedCardId.value = 'card-1'
    nav.selectPreviousCard()
    expect(nav.selectedCardId.value).toBe('card-1')
  })

  it('moves to next column and selects first card', () => {
    const nav = useBoardKeyboardNav(sortedColumns)
    nav.selectNextColumn()
    expect(nav.selectedColumnIndex.value).toBe(1)
    expect(nav.selectedCardId.value).toBe('card-3')
  })

  it('does not go beyond the last column', () => {
    const nav = useBoardKeyboardNav(sortedColumns)
    nav.selectedColumnIndex.value = 1
    nav.selectNextColumn()
    expect(nav.selectedColumnIndex.value).toBe(1)
  })

  it('moves to previous column and selects first card', () => {
    const nav = useBoardKeyboardNav(sortedColumns)
    nav.selectedColumnIndex.value = 1
    nav.selectPreviousColumn()
    expect(nav.selectedColumnIndex.value).toBe(0)
    expect(nav.selectedCardId.value).toBe('card-1')
  })

  it('does not go before the first column', () => {
    const nav = useBoardKeyboardNav(sortedColumns)
    nav.selectPreviousColumn()
    expect(nav.selectedColumnIndex.value).toBe(0)
  })

  it('resets selection state', () => {
    const nav = useBoardKeyboardNav(sortedColumns)
    nav.selectedCardId.value = 'card-2'
    nav.selectedColumnIndex.value = 1
    nav.resetSelection()
    expect(nav.selectedCardId.value).toBeNull()
    expect(nav.selectedColumnIndex.value).toBe(0)
  })

  it('handles empty columns gracefully', () => {
    const emptyColumns = computed(() => [] as Column[])
    const nav = useBoardKeyboardNav(emptyColumns)
    // Should not throw
    nav.selectNextCard()
    nav.selectPreviousCard()
    nav.selectNextColumn()
    nav.selectPreviousColumn()
    nav.openSelectedCard()
    expect(nav.selectedCardId.value).toBeNull()
  })
})
