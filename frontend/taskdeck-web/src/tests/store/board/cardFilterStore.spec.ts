import { describe, expect, it, vi, beforeEach } from 'vitest'
import { ref } from 'vue'
import { createCardFilterActions } from '../../../store/board/cardFilterStore'
import type { Card } from '../../../types/board'

function makeCard(overrides: Partial<Card> = {}): Card {
  return {
    id: overrides.id ?? 'card-1',
    columnId: overrides.columnId ?? 'col-1',
    title: overrides.title ?? 'Test Card',
    description: overrides.description ?? null,
    position: overrides.position ?? 0,
    dueDate: overrides.dueDate ?? null,
    isBlocked: overrides.isBlocked ?? false,
    blockReason: overrides.blockReason ?? null,
    labels: overrides.labels ?? [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  } as Card
}

function createMockState(cards: Card[] = []) {
  return {
    currentBoardCards: ref(cards),
    filters: ref({
      searchText: '',
      labelIds: [] as string[],
      dueDateFilter: 'all' as const,
      showBlockedOnly: false,
    }),
  }
}

describe('cardFilterStore', () => {
  describe('cardMatchesFilters — search text', () => {
    it('matches card title case-insensitively', () => {
      const cards = [makeCard({ title: 'Deploy Feature' })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.searchText = 'deploy'
      expect(filteredCardCount.value).toBe(1)
    })

    it('matches card description', () => {
      const cards = [makeCard({ title: 'X', description: 'Fix the login bug' })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.searchText = 'login'
      expect(filteredCardCount.value).toBe(1)
    })

    it('excludes cards that do not match search text', () => {
      const cards = [makeCard({ title: 'Something', description: 'unrelated' })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.searchText = 'deploy'
      expect(filteredCardCount.value).toBe(0)
    })

    it('shows all cards when search text is empty', () => {
      const cards = [makeCard({ id: '1' }), makeCard({ id: '2' })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.searchText = ''
      expect(filteredCardCount.value).toBe(2)
    })
  })

  describe('cardMatchesFilters — label filter', () => {
    it('includes card with matching label', () => {
      const cards = [makeCard({ labels: [{ id: 'lbl-1', name: 'Bug', colorHex: '#f00' }] as any })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.labelIds = ['lbl-1']
      expect(filteredCardCount.value).toBe(1)
    })

    it('excludes card without matching label', () => {
      const cards = [makeCard({ labels: [{ id: 'lbl-2', name: 'Feature', colorHex: '#0f0' }] as any })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.labelIds = ['lbl-1']
      expect(filteredCardCount.value).toBe(0)
    })

    it('shows all cards when labelIds is empty', () => {
      const cards = [makeCard({ id: '1' }), makeCard({ id: '2' })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.labelIds = []
      expect(filteredCardCount.value).toBe(2)
    })
  })

  describe('cardMatchesFilters — due date filter', () => {
    it('overdue: includes cards with past due date', () => {
      const yesterday = new Date()
      yesterday.setDate(yesterday.getDate() - 1)
      const cards = [makeCard({ dueDate: yesterday.toISOString() })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.dueDateFilter = 'overdue'
      expect(filteredCardCount.value).toBe(1)
    })

    it('overdue: excludes cards with no due date', () => {
      const cards = [makeCard({ dueDate: null })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.dueDateFilter = 'overdue'
      expect(filteredCardCount.value).toBe(0)
    })

    it('due-today: includes cards due today', () => {
      const today = new Date()
      today.setHours(12, 0, 0, 0)
      const cards = [makeCard({ dueDate: today.toISOString() })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.dueDateFilter = 'due-today'
      expect(filteredCardCount.value).toBe(1)
    })

    it('due-today: excludes cards due tomorrow', () => {
      const tomorrow = new Date()
      tomorrow.setDate(tomorrow.getDate() + 1)
      const cards = [makeCard({ dueDate: tomorrow.toISOString() })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.dueDateFilter = 'due-today'
      expect(filteredCardCount.value).toBe(0)
    })

    it('due-week: includes cards due within 7 days', () => {
      const inThreeDays = new Date()
      inThreeDays.setDate(inThreeDays.getDate() + 3)
      const cards = [makeCard({ dueDate: inThreeDays.toISOString() })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.dueDateFilter = 'due-week'
      expect(filteredCardCount.value).toBe(1)
    })

    it('due-week: excludes cards due in more than 7 days', () => {
      const inTenDays = new Date()
      inTenDays.setDate(inTenDays.getDate() + 10)
      const cards = [makeCard({ dueDate: inTenDays.toISOString() })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.dueDateFilter = 'due-week'
      expect(filteredCardCount.value).toBe(0)
    })

    it('no-date: includes cards without due date', () => {
      const cards = [makeCard({ dueDate: null })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.dueDateFilter = 'no-date'
      expect(filteredCardCount.value).toBe(1)
    })

    it('no-date: excludes cards with due date', () => {
      const cards = [makeCard({ dueDate: '2026-06-01T00:00:00Z' })]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.dueDateFilter = 'no-date'
      expect(filteredCardCount.value).toBe(0)
    })
  })

  describe('cardMatchesFilters — blocked filter', () => {
    it('showBlockedOnly includes only blocked cards', () => {
      const cards = [
        makeCard({ id: '1', isBlocked: true }),
        makeCard({ id: '2', isBlocked: false }),
      ]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.showBlockedOnly = true
      expect(filteredCardCount.value).toBe(1)
    })

    it('showBlockedOnly false includes all cards', () => {
      const cards = [
        makeCard({ id: '1', isBlocked: true }),
        makeCard({ id: '2', isBlocked: false }),
      ]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.showBlockedOnly = false
      expect(filteredCardCount.value).toBe(2)
    })
  })

  describe('cardsByColumn', () => {
    it('groups filtered cards by columnId', () => {
      const cards = [
        makeCard({ id: '1', columnId: 'col-a', position: 1 }),
        makeCard({ id: '2', columnId: 'col-b', position: 0 }),
        makeCard({ id: '3', columnId: 'col-a', position: 0 }),
      ]
      const state = createMockState(cards)
      const { cardsByColumn } = createCardFilterActions(state as any)
      const result = cardsByColumn.value
      expect(result.get('col-a')?.map((c) => c.id)).toEqual(['3', '1'])
      expect(result.get('col-b')?.map((c) => c.id)).toEqual(['2'])
    })

    it('sorts cards by position within column', () => {
      const cards = [
        makeCard({ id: 'c', columnId: 'col-1', position: 2 }),
        makeCard({ id: 'a', columnId: 'col-1', position: 0 }),
        makeCard({ id: 'b', columnId: 'col-1', position: 1 }),
      ]
      const state = createMockState(cards)
      const { cardsByColumn } = createCardFilterActions(state as any)
      expect(cardsByColumn.value.get('col-1')?.map((c) => c.id)).toEqual(['a', 'b', 'c'])
    })

    it('respects active filters', () => {
      const cards = [
        makeCard({ id: '1', columnId: 'col-1', title: 'Match' }),
        makeCard({ id: '2', columnId: 'col-1', title: 'Other' }),
      ]
      const state = createMockState(cards)
      const { cardsByColumn } = createCardFilterActions(state as any)
      state.filters.value.searchText = 'match'
      expect(cardsByColumn.value.get('col-1')?.length).toBe(1)
    })
  })

  describe('totalCardCount', () => {
    it('returns total count regardless of filters', () => {
      const cards = [makeCard({ id: '1' }), makeCard({ id: '2' }), makeCard({ id: '3' })]
      const state = createMockState(cards)
      const { totalCardCount } = createCardFilterActions(state as any)
      state.filters.value.searchText = 'nonexistent'
      expect(totalCardCount.value).toBe(3)
    })
  })

  describe('updateFilters', () => {
    it('replaces filter state', () => {
      const state = createMockState([])
      const { updateFilters } = createCardFilterActions(state as any)
      updateFilters({
        searchText: 'hello',
        labelIds: ['lbl-1'],
        dueDateFilter: 'overdue',
        showBlockedOnly: true,
      })
      expect(state.filters.value.searchText).toBe('hello')
      expect(state.filters.value.labelIds).toEqual(['lbl-1'])
      expect(state.filters.value.dueDateFilter).toBe('overdue')
      expect(state.filters.value.showBlockedOnly).toBe(true)
    })
  })

  describe('clearFilters', () => {
    it('resets all filters to defaults', () => {
      const state = createMockState([])
      state.filters.value = {
        searchText: 'something',
        labelIds: ['a', 'b'],
        dueDateFilter: 'overdue',
        showBlockedOnly: true,
      }
      const { clearFilters } = createCardFilterActions(state as any)
      clearFilters()
      expect(state.filters.value).toEqual({
        searchText: '',
        labelIds: [],
        dueDateFilter: 'all',
        showBlockedOnly: false,
      })
    })
  })

  describe('combined filters', () => {
    it('applies search + label + blocked together', () => {
      const cards = [
        makeCard({ id: '1', title: 'Deploy API', isBlocked: true, labels: [{ id: 'l1', name: 'A', colorHex: '#f00' }] as any }),
        makeCard({ id: '2', title: 'Deploy UI', isBlocked: false, labels: [{ id: 'l1', name: 'A', colorHex: '#f00' }] as any }),
        makeCard({ id: '3', title: 'Deploy API', isBlocked: true, labels: [{ id: 'l2', name: 'B', colorHex: '#0f0' }] as any }),
      ]
      const state = createMockState(cards)
      const { filteredCardCount } = createCardFilterActions(state as any)
      state.filters.value.searchText = 'deploy api'
      state.filters.value.labelIds = ['l1']
      state.filters.value.showBlockedOnly = true
      expect(filteredCardCount.value).toBe(1)
    })
  })
})
