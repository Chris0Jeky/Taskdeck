import { describe, it, expect, vi, beforeEach } from 'vitest'
import { computed } from 'vue'
import { useBoardKeyboardNav } from '../../composables/useBoardKeyboardNav'
import type { Column, Card } from '../../types/board'

const cardsByColumn = new Map<string, Card[]>()
const moveCard = vi.fn()
const mockBoardStore = {
  cardsByColumn,
  moveCard,
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

  describe('openSelectedCard', () => {
    it('selects the first card and clicks its DOM element when no card is selected', () => {
      const mockElement = {
        scrollIntoView: vi.fn(),
        click: vi.fn(),
      } as unknown as HTMLElement

      const querySpy = vi.spyOn(document, 'querySelector').mockReturnValue(mockElement)

      const nav = useBoardKeyboardNav(sortedColumns)
      nav.selectedCardId.value = null
      nav.openSelectedCard()

      // Should have selected card-1 (first in column)
      expect(nav.selectedCardId.value).toBe('card-1')
      expect(querySpy).toHaveBeenCalledWith('[data-card-id="card-1"]')
      expect(mockElement.scrollIntoView).toHaveBeenCalledWith({ block: 'nearest', inline: 'nearest' })
      expect(mockElement.click).toHaveBeenCalled()

      querySpy.mockRestore()
    })

    it('clicks the DOM element for an already-selected card', () => {
      const mockElement = {
        scrollIntoView: vi.fn(),
        click: vi.fn(),
      } as unknown as HTMLElement

      const querySpy = vi.spyOn(document, 'querySelector').mockReturnValue(mockElement)

      const nav = useBoardKeyboardNav(sortedColumns)
      nav.selectedCardId.value = 'card-2'
      nav.openSelectedCard()

      expect(querySpy).toHaveBeenCalledWith('[data-card-id="card-2"]')
      expect(mockElement.click).toHaveBeenCalled()

      querySpy.mockRestore()
    })

    it('does nothing when the card element is not in the DOM', () => {
      const querySpy = vi.spyOn(document, 'querySelector').mockReturnValue(null)

      const nav = useBoardKeyboardNav(sortedColumns)
      nav.selectedCardId.value = 'card-1'
      // Should not throw
      nav.openSelectedCard()

      querySpy.mockRestore()
    })
  })

  describe('moveCardToNextColumn', () => {
    beforeEach(() => {
      moveCard.mockReset()
      moveCard.mockResolvedValue(undefined)
    })

    it('moves selected card to the next column', async () => {
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = 'card-1'
      nav.selectedColumnIndex.value = 0

      await nav.moveCardToNextColumn()

      expect(moveCard).toHaveBeenCalledWith('board-1', 'card-1', 'c2', 1)
      expect(nav.selectedColumnIndex.value).toBe(1)
    })

    it('does nothing when already in last column', async () => {
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = 'card-3'
      nav.selectedColumnIndex.value = 1

      await nav.moveCardToNextColumn()

      expect(moveCard).not.toHaveBeenCalled()
    })

    it('does nothing when no card is selected', async () => {
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = null

      await nav.moveCardToNextColumn()

      expect(moveCard).not.toHaveBeenCalled()
    })

    it('does nothing when boardId is not provided', async () => {
      const nav = useBoardKeyboardNav(sortedColumns)
      nav.selectedCardId.value = 'card-1'

      await nav.moveCardToNextColumn()

      expect(moveCard).not.toHaveBeenCalled()
    })
  })

  describe('moveCardToPreviousColumn', () => {
    beforeEach(() => {
      moveCard.mockReset()
      moveCard.mockResolvedValue(undefined)
    })

    it('moves selected card to the previous column', async () => {
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = 'card-3'
      nav.selectedColumnIndex.value = 1

      await nav.moveCardToPreviousColumn()

      expect(moveCard).toHaveBeenCalledWith('board-1', 'card-3', 'c1', 2)
      expect(nav.selectedColumnIndex.value).toBe(0)
    })

    it('does nothing when already in first column', async () => {
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = 'card-1'
      nav.selectedColumnIndex.value = 0

      await nav.moveCardToPreviousColumn()

      expect(moveCard).not.toHaveBeenCalled()
    })

    it('does nothing when no card is selected', async () => {
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = null

      await nav.moveCardToPreviousColumn()

      expect(moveCard).not.toHaveBeenCalled()
    })
  })

  describe('moveCardUp', () => {
    beforeEach(() => {
      moveCard.mockReset()
      moveCard.mockResolvedValue(undefined)
    })

    it('moves selected card up one position', async () => {
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = 'card-2'
      nav.selectedColumnIndex.value = 0

      await nav.moveCardUp()

      expect(moveCard).toHaveBeenCalledWith('board-1', 'card-2', 'c1', card1.position)
    })

    it('does nothing when card is already at top', async () => {
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = 'card-1'
      nav.selectedColumnIndex.value = 0

      await nav.moveCardUp()

      expect(moveCard).not.toHaveBeenCalled()
    })

    it('does nothing when no card is selected', async () => {
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = null

      await nav.moveCardUp()

      expect(moveCard).not.toHaveBeenCalled()
    })
  })

  describe('moveCardDown', () => {
    beforeEach(() => {
      moveCard.mockReset()
      moveCard.mockResolvedValue(undefined)
    })

    it('moves selected card down one position', async () => {
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = 'card-1'
      nav.selectedColumnIndex.value = 0

      await nav.moveCardDown()

      expect(moveCard).toHaveBeenCalledWith('board-1', 'card-1', 'c1', card2.position)
    })

    it('does nothing when card is already at bottom', async () => {
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = 'card-2'
      nav.selectedColumnIndex.value = 0

      await nav.moveCardDown()

      expect(moveCard).not.toHaveBeenCalled()
    })

    it('does nothing when no card is selected', async () => {
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = null

      await nav.moveCardDown()

      expect(moveCard).not.toHaveBeenCalled()
    })
  })

  describe('move error handling', () => {
    beforeEach(() => {
      moveCard.mockReset()
    })

    it('does not throw when moveCard rejects', async () => {
      moveCard.mockRejectedValue(new Error('Network error'))
      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = 'card-1'
      nav.selectedColumnIndex.value = 0

      // Should not throw
      await expect(nav.moveCardToNextColumn()).resolves.not.toThrow()
    })
  })

  describe('focusSelectedCard (via move operations)', () => {
    beforeEach(() => {
      moveCard.mockReset()
      moveCard.mockResolvedValue(undefined)
    })

    it('scrolls and focuses the card element after a successful move', async () => {
      const mockElement = {
        scrollIntoView: vi.fn(),
        focus: vi.fn(),
      } as unknown as HTMLElement

      const querySpy = vi.spyOn(document, 'querySelector').mockReturnValue(mockElement)

      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = 'card-1'
      nav.selectedColumnIndex.value = 0

      await nav.moveCardToNextColumn()

      expect(mockElement.scrollIntoView).toHaveBeenCalledWith({ block: 'nearest', inline: 'nearest' })
      expect(mockElement.focus).toHaveBeenCalled()

      querySpy.mockRestore()
    })

    it('does not throw when the card element is absent after move', async () => {
      const querySpy = vi.spyOn(document, 'querySelector').mockReturnValue(null)

      const nav = useBoardKeyboardNav(sortedColumns, () => 'board-1')
      nav.selectedCardId.value = 'card-1'
      nav.selectedColumnIndex.value = 0

      await expect(nav.moveCardToNextColumn()).resolves.not.toThrow()

      querySpy.mockRestore()
    })
  })
})
