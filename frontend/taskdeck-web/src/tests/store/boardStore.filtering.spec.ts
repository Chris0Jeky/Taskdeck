import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useBoardStore } from '../../store/boardStore'
import type { Card } from '../../types/board'

// Mock the API modules
vi.mock('../../api/boardsApi', () => ({
  boardsApi: {
    getBoards: vi.fn(),
    getBoard: vi.fn(),
    createBoard: vi.fn(),
    updateBoard: vi.fn(),
    deleteBoard: vi.fn(),
  }
}))

vi.mock('../../api/columnsApi', () => ({
  columnsApi: {
    getColumns: vi.fn(),
    createColumn: vi.fn(),
    updateColumn: vi.fn(),
    deleteColumn: vi.fn(),
    reorderColumns: vi.fn(),
  }
}))

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getCards: vi.fn(),
    createCard: vi.fn(),
    updateCard: vi.fn(),
    deleteCard: vi.fn(),
    moveCard: vi.fn(),
  }
}))

vi.mock('../../api/labelsApi', () => ({
  labelsApi: {
    getLabels: vi.fn(),
    createLabel: vi.fn(),
    updateLabel: vi.fn(),
    deleteLabel: vi.fn(),
  }
}))

describe('boardStore - Filtering', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  const createMockCard = (overrides: Partial<Card> = {}): Card => ({
    id: 'card-1',
    boardId: 'board-1',
    columnId: 'column-1',
    title: 'Test Card',
    description: 'Test Description',
    dueDate: null,
    isBlocked: false,
    blockReason: null,
    position: 0,
    labels: [],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides
  })

  describe('Search Text Filter', () => {
    it('should filter cards by title', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1', title: 'Bug Fix' }),
        createMockCard({ id: '2', title: 'Feature Request' }),
        createMockCard({ id: '3', title: 'Documentation' })
      ]

      store.updateFilters({ ...store.filters, searchText: 'bug' })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(1)
      expect(filteredCards[0].title).toBe('Bug Fix')
    })

    it('should filter cards by description', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1', title: 'Task 1', description: 'Fix the login bug' }),
        createMockCard({ id: '2', title: 'Task 2', description: 'Add new feature' }),
      ]

      store.updateFilters({ ...store.filters, searchText: 'login' })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(1)
      expect(filteredCards[0].description).toContain('login')
    })

    it('should be case insensitive', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1', title: 'BUG FIX' }),
        createMockCard({ id: '2', title: 'feature' })
      ]

      store.updateFilters({ ...store.filters, searchText: 'Bug' })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(1)
    })

    it('should return all cards when search is empty', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1' }),
        createMockCard({ id: '2' })
      ]

      store.updateFilters({ ...store.filters, searchText: '' })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(2)
    })
  })

  describe('Label Filter', () => {
    it('should filter cards by single label', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({
          id: '1',
          labels: [{ id: 'label-1', boardId: 'board-1', name: 'Bug', colorHex: '#ff0000', createdAt: '', updatedAt: '' }]
        }),
        createMockCard({
          id: '2',
          labels: [{ id: 'label-2', boardId: 'board-1', name: 'Feature', colorHex: '#00ff00', createdAt: '', updatedAt: '' }]
        }),
      ]

      store.updateFilters({ ...store.filters, labelIds: ['label-1'] })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(1)
      expect(filteredCards[0].id).toBe('1')
    })

    it('should filter cards by multiple labels', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({
          id: '1',
          labels: [{ id: 'label-1', boardId: 'board-1', name: 'Bug', colorHex: '#ff0000', createdAt: '', updatedAt: '' }]
        }),
        createMockCard({
          id: '2',
          labels: [{ id: 'label-2', boardId: 'board-1', name: 'Feature', colorHex: '#00ff00', createdAt: '', updatedAt: '' }]
        }),
        createMockCard({
          id: '3',
          labels: [{ id: 'label-3', boardId: 'board-1', name: 'Docs', colorHex: '#0000ff', createdAt: '', updatedAt: '' }]
        }),
      ]

      store.updateFilters({ ...store.filters, labelIds: ['label-1', 'label-2'] })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(2)
    })

    it('should match cards with multiple labels when any label matches', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({
          id: '1',
          labels: [
            { id: 'label-1', boardId: 'board-1', name: 'Bug', colorHex: '#ff0000', createdAt: '', updatedAt: '' },
            { id: 'label-2', boardId: 'board-1', name: 'Critical', colorHex: '#ff00ff', createdAt: '', updatedAt: '' }
          ]
        }),
        createMockCard({ id: '2', labels: [] }),
      ]

      store.updateFilters({ ...store.filters, labelIds: ['label-1'] })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(1)
      expect(filteredCards[0].id).toBe('1')
    })
  })

  describe('Due Date Filter', () => {
    it('should filter overdue cards', () => {
      const yesterday = new Date()
      yesterday.setDate(yesterday.getDate() - 1)

      const tomorrow = new Date()
      tomorrow.setDate(tomorrow.getDate() + 1)

      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1', dueDate: yesterday.toISOString() }),
        createMockCard({ id: '2', dueDate: tomorrow.toISOString() }),
        createMockCard({ id: '3', dueDate: null }),
      ]

      store.updateFilters({ ...store.filters, dueDateFilter: 'overdue' })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(1)
      expect(filteredCards[0].id).toBe('1')
    })

    it('should filter cards due today', () => {
      const today = new Date()
      const tomorrow = new Date()
      tomorrow.setDate(tomorrow.getDate() + 1)

      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1', dueDate: today.toISOString() }),
        createMockCard({ id: '2', dueDate: tomorrow.toISOString() }),
      ]

      store.updateFilters({ ...store.filters, dueDateFilter: 'due-today' })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(1)
      expect(filteredCards[0].id).toBe('1')
    })

    it('should filter cards due this week', () => {
      const tomorrow = new Date()
      tomorrow.setDate(tomorrow.getDate() + 1)

      const nextMonth = new Date()
      nextMonth.setMonth(nextMonth.getMonth() + 1)

      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1', dueDate: tomorrow.toISOString() }),
        createMockCard({ id: '2', dueDate: nextMonth.toISOString() }),
      ]

      store.updateFilters({ ...store.filters, dueDateFilter: 'due-week' })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(1)
      expect(filteredCards[0].id).toBe('1')
    })

    it('should filter cards with no due date', () => {
      const tomorrow = new Date()
      tomorrow.setDate(tomorrow.getDate() + 1)

      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1', dueDate: null }),
        createMockCard({ id: '2', dueDate: tomorrow.toISOString() }),
      ]

      store.updateFilters({ ...store.filters, dueDateFilter: 'no-date' })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(1)
      expect(filteredCards[0].id).toBe('1')
    })
  })

  describe('Blocked Status Filter', () => {
    it('should filter only blocked cards', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1', isBlocked: true, blockReason: 'Waiting on review' }),
        createMockCard({ id: '2', isBlocked: false }),
        createMockCard({ id: '3', isBlocked: true, blockReason: 'Dependencies' }),
      ]

      store.updateFilters({ ...store.filters, showBlockedOnly: true })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(2)
      expect(filteredCards.every(c => c.isBlocked)).toBe(true)
    })

    it('should show all cards when showBlockedOnly is false', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1', isBlocked: true }),
        createMockCard({ id: '2', isBlocked: false }),
      ]

      store.updateFilters({ ...store.filters, showBlockedOnly: false })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(2)
    })
  })

  describe('Combined Filters', () => {
    it('should apply multiple filters simultaneously', () => {
      const today = new Date()

      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({
          id: '1',
          title: 'Bug Fix',
          dueDate: today.toISOString(),
          isBlocked: true,
          labels: [{ id: 'label-1', boardId: 'board-1', name: 'Bug', colorHex: '#ff0000', createdAt: '', updatedAt: '' }]
        }),
        createMockCard({
          id: '2',
          title: 'Bug Fix',
          dueDate: today.toISOString(),
          isBlocked: false,
        }),
        createMockCard({
          id: '3',
          title: 'Feature',
          dueDate: today.toISOString(),
          isBlocked: true,
        }),
      ]

      store.updateFilters({
        searchText: 'bug',
        labelIds: ['label-1'],
        dueDateFilter: 'due-today',
        showBlockedOnly: true
      })

      const filteredCards = Array.from(store.cardsByColumn.values()).flat()
      expect(filteredCards).toHaveLength(1)
      expect(filteredCards[0].id).toBe('1')
    })
  })

  describe('Filter Counts', () => {
    it('should calculate filteredCardCount correctly', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1', title: 'Bug' }),
        createMockCard({ id: '2', title: 'Feature' }),
        createMockCard({ id: '3', title: 'Bug Fix' }),
      ]

      store.updateFilters({ ...store.filters, searchText: 'bug' })

      expect(store.filteredCardCount).toBe(2)
      expect(store.totalCardCount).toBe(3)
    })

    it('should return totalCardCount when no filters are active', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1' }),
        createMockCard({ id: '2' }),
      ]

      expect(store.filteredCardCount).toBe(2)
      expect(store.totalCardCount).toBe(2)
    })
  })

  describe('Filter Actions', () => {
    it('should update filters', () => {
      const store = useBoardStore()

      store.updateFilters({
        searchText: 'test',
        labelIds: ['label-1'],
        dueDateFilter: 'overdue',
        showBlockedOnly: true
      })

      expect(store.filters.searchText).toBe('test')
      expect(store.filters.labelIds).toEqual(['label-1'])
      expect(store.filters.dueDateFilter).toBe('overdue')
      expect(store.filters.showBlockedOnly).toBe(true)
    })

    it('should clear all filters', () => {
      const store = useBoardStore()

      store.updateFilters({
        searchText: 'test',
        labelIds: ['label-1'],
        dueDateFilter: 'overdue',
        showBlockedOnly: true
      })

      store.clearFilters()

      expect(store.filters.searchText).toBe('')
      expect(store.filters.labelIds).toEqual([])
      expect(store.filters.dueDateFilter).toBe('all')
      expect(store.filters.showBlockedOnly).toBe(false)
    })
  })

  describe('Cards by Column with Filters', () => {
    it('should group filtered cards by column', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1', columnId: 'col-1', title: 'Bug 1' }),
        createMockCard({ id: '2', columnId: 'col-1', title: 'Feature 1' }),
        createMockCard({ id: '3', columnId: 'col-2', title: 'Bug 2' }),
      ]

      store.updateFilters({ ...store.filters, searchText: 'bug' })

      expect(store.cardsByColumn.get('col-1')).toHaveLength(1)
      expect(store.cardsByColumn.get('col-2')).toHaveLength(1)
      expect(store.cardsByColumn.get('col-1')?.[0].id).toBe('1')
      expect(store.cardsByColumn.get('col-2')?.[0].id).toBe('3')
    })

    it('should maintain card sorting within columns after filtering', () => {
      const store = useBoardStore()
      store.currentBoardCards = [
        createMockCard({ id: '1', columnId: 'col-1', title: 'Bug', position: 2 }),
        createMockCard({ id: '2', columnId: 'col-1', title: 'Bug Fix', position: 0 }),
        createMockCard({ id: '3', columnId: 'col-1', title: 'Bug Report', position: 1 }),
        createMockCard({ id: '4', columnId: 'col-1', title: 'Feature', position: 3 }),
      ]

      store.updateFilters({ ...store.filters, searchText: 'bug' })

      const cards = store.cardsByColumn.get('col-1') || []
      expect(cards).toHaveLength(3)
      expect(cards[0].id).toBe('2') // position 0
      expect(cards[1].id).toBe('3') // position 1
      expect(cards[2].id).toBe('1') // position 2
    })
  })
})
