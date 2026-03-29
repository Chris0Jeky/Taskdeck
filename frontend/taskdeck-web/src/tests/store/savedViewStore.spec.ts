import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSavedViewStore, cardMatchesSavedViewFilter } from '../../store/savedViewStore'
import type { SavedViewFilter } from '../../store/savedViewStore'
import type { Card } from '../../types/board'

function createMockCard(overrides: Partial<Card> = {}): Card {
  return {
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
    ...overrides,
  }
}

function createBaseFilter(overrides: Partial<SavedViewFilter> = {}): SavedViewFilter {
  return {
    searchText: '',
    labelNames: [],
    dueDateFilter: 'all',
    showBlockedOnly: false,
    ...overrides,
  }
}

describe('savedViewStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  describe('default views', () => {
    it('should include four default starter views', () => {
      const store = useSavedViewStore()
      expect(store.defaultViews).toHaveLength(4)
    })

    it('should include Blocked Work view', () => {
      const store = useSavedViewStore()
      const blocked = store.defaultViews.find((v) => v.id === 'default-blocked')
      expect(blocked).toBeDefined()
      expect(blocked!.name).toBe('Blocked Work')
      expect(blocked!.filter.showBlockedOnly).toBe(true)
      expect(blocked!.isDefault).toBe(true)
    })

    it('should include Due This Week view', () => {
      const store = useSavedViewStore()
      const dueWeek = store.defaultViews.find((v) => v.id === 'default-due-week')
      expect(dueWeek).toBeDefined()
      expect(dueWeek!.filter.dueDateFilter).toBe('due-week')
    })

    it('should include Needs Review view', () => {
      const store = useSavedViewStore()
      const review = store.defaultViews.find((v) => v.id === 'default-needs-review')
      expect(review).toBeDefined()
      expect(review!.filter.labelNames).toContain('review')
    })

    it('should include Overdue view', () => {
      const store = useSavedViewStore()
      const overdue = store.defaultViews.find((v) => v.id === 'default-overdue')
      expect(overdue).toBeDefined()
      expect(overdue!.filter.dueDateFilter).toBe('overdue')
    })

    it('should use stable timestamps for default views', () => {
      const store1 = useSavedViewStore()
      const ts1 = store1.defaultViews[0].createdAt

      setActivePinia(createPinia())
      const store2 = useSavedViewStore()
      const ts2 = store2.defaultViews[0].createdAt

      expect(ts1).toBe(ts2)
      expect(ts1).toBe('2024-01-01T00:00:00.000Z')
    })
  })

  describe('createView', () => {
    it('should create a custom view', () => {
      const store = useSavedViewStore()
      const view = store.createView('My View', 'M', createBaseFilter({ showBlockedOnly: true }))

      expect(view.name).toBe('My View')
      expect(view.icon).toBe('M')
      expect(view.isDefault).toBe(false)
      expect(view.filter.showBlockedOnly).toBe(true)
      expect(store.customViews).toHaveLength(1)
    })

    it('should generate unique ids', () => {
      const store = useSavedViewStore()
      const v1 = store.createView('View 1', 'A', createBaseFilter())
      const v2 = store.createView('View 2', 'B', createBaseFilter())
      expect(v1.id).not.toBe(v2.id)
    })
  })

  describe('updateView', () => {
    it('should update a custom view name', () => {
      const store = useSavedViewStore()
      const view = store.createView('Old Name', 'O', createBaseFilter())

      store.updateView(view.id, { name: 'New Name' })

      const updated = store.views.find((v) => v.id === view.id)
      expect(updated!.name).toBe('New Name')
    })

    it('should not update a default view', () => {
      const store = useSavedViewStore()
      store.updateView('default-blocked', { name: 'Renamed' })

      const blocked = store.views.find((v) => v.id === 'default-blocked')
      expect(blocked!.name).toBe('Blocked Work')
    })

    it('should be a no-op for non-existent views', () => {
      const store = useSavedViewStore()
      const countBefore = store.views.length
      store.updateView('nonexistent-id', { name: 'No Effect' })
      expect(store.views.length).toBe(countBefore)
    })
  })

  describe('deleteView', () => {
    it('should delete a custom view', () => {
      const store = useSavedViewStore()
      const view = store.createView('Temp', 'T', createBaseFilter())
      const countBefore = store.views.length

      store.deleteView(view.id)

      expect(store.views.length).toBe(countBefore - 1)
      expect(store.views.find((v) => v.id === view.id)).toBeUndefined()
    })

    it('should not delete a default view', () => {
      const store = useSavedViewStore()
      const countBefore = store.views.length

      store.deleteView('default-blocked')

      expect(store.views.length).toBe(countBefore)
    })

    it('should clear activeViewId when the active view is deleted', () => {
      const store = useSavedViewStore()
      const view = store.createView('Active', 'A', createBaseFilter())
      store.setActiveView(view.id)
      expect(store.activeViewId).toBe(view.id)

      store.deleteView(view.id)

      expect(store.activeViewId).toBeNull()
    })
  })

  describe('setActiveView', () => {
    it('should set and clear the active view', () => {
      const store = useSavedViewStore()
      store.setActiveView('default-blocked')
      expect(store.activeView?.id).toBe('default-blocked')

      store.setActiveView(null)
      expect(store.activeView).toBeNull()
    })

    it('should return null for unknown view id', () => {
      const store = useSavedViewStore()
      store.setActiveView('nonexistent')
      expect(store.activeView).toBeNull()
    })
  })

  describe('persistence', () => {
    it('should persist custom views to localStorage', () => {
      const store = useSavedViewStore()
      store.createView('Saved', 'S', createBaseFilter({ showBlockedOnly: true }))

      const raw = localStorage.getItem('taskdeck_saved_views')
      expect(raw).not.toBeNull()
      const parsed = JSON.parse(raw!)
      expect(parsed).toHaveLength(1)
      expect(parsed[0].name).toBe('Saved')
    })

    it('should restore custom views from localStorage on init', () => {
      // Create a view first
      const store1 = useSavedViewStore()
      store1.createView('Persisted', 'P', createBaseFilter({ dueDateFilter: 'overdue' }))

      // New store instance simulates reload
      setActivePinia(createPinia())
      const store2 = useSavedViewStore()

      expect(store2.customViews).toHaveLength(1)
      expect(store2.customViews[0].name).toBe('Persisted')
      expect(store2.customViews[0].filter.dueDateFilter).toBe('overdue')
    })

    it('should handle invalid JSON in localStorage gracefully', () => {
      localStorage.setItem('taskdeck_saved_views', 'not-valid-json')

      const store = useSavedViewStore()
      // Should fall back to defaults only
      expect(store.customViews).toHaveLength(0)
      expect(store.defaultViews).toHaveLength(4)
    })

    it('should handle non-array JSON in localStorage gracefully', () => {
      localStorage.setItem('taskdeck_saved_views', '{"not":"an-array"}')

      const store = useSavedViewStore()
      expect(store.customViews).toHaveLength(0)
      expect(store.defaultViews).toHaveLength(4)
    })

    it('should not persist default views', () => {
      const _store = useSavedViewStore()

      const raw = localStorage.getItem('taskdeck_saved_views')
      // Defaults are not persisted unless a custom view triggers persist()
      // On initial load with no custom views, there is no localStorage entry
      if (raw) {
        const parsed = JSON.parse(raw)
        const defaults = parsed.filter((v: { isDefault?: boolean }) => v.isDefault)
        expect(defaults).toHaveLength(0)
      }
    })

    it('should apply default filter values for missing properties on restore', () => {
      // Store a view with a partial/corrupt filter object
      const partial = [{
        id: 'custom-partial',
        name: 'Partial Filter',
        icon: 'P',
        filter: { searchText: 'hello' }, // missing labelNames, dueDateFilter, showBlockedOnly
        isDefault: false,
        createdAt: '2024-06-01T00:00:00.000Z',
      }]
      localStorage.setItem('taskdeck_saved_views', JSON.stringify(partial))

      const store = useSavedViewStore()
      const restored = store.customViews.find((v) => v.id === 'custom-partial')
      expect(restored).toBeDefined()
      expect(restored!.filter.searchText).toBe('hello')
      expect(restored!.filter.labelNames).toEqual([])
      expect(restored!.filter.dueDateFilter).toBe('all')
      expect(restored!.filter.showBlockedOnly).toBe(false)
    })

    it('should apply default icon when icon is missing on restore', () => {
      const noIcon = [{
        id: 'custom-no-icon',
        name: 'No Icon',
        filter: { searchText: '', labelNames: [], dueDateFilter: 'all', showBlockedOnly: false },
        isDefault: false,
        createdAt: '2024-06-01T00:00:00.000Z',
      }]
      localStorage.setItem('taskdeck_saved_views', JSON.stringify(noIcon))

      const store = useSavedViewStore()
      const restored = store.customViews.find((v) => v.id === 'custom-no-icon')
      expect(restored).toBeDefined()
      expect(restored!.icon).toBe('?')
    })

    it('should reject invalid dueDateFilter values on restore', () => {
      const badFilter = [{
        id: 'custom-bad-filter',
        name: 'Bad Filter',
        icon: 'B',
        filter: { searchText: '', labelNames: [], dueDateFilter: 'invalid-value', showBlockedOnly: false },
        isDefault: false,
        createdAt: '2024-06-01T00:00:00.000Z',
      }]
      localStorage.setItem('taskdeck_saved_views', JSON.stringify(badFilter))

      const store = useSavedViewStore()
      const restored = store.customViews.find((v) => v.id === 'custom-bad-filter')
      expect(restored).toBeDefined()
      expect(restored!.filter.dueDateFilter).toBe('all') // falls back to default
    })
  })

  describe('filterCards', () => {
    it('should return all cards when no active view', () => {
      const store = useSavedViewStore()
      const cards = [createMockCard({ id: '1' }), createMockCard({ id: '2' })]
      expect(store.filterCards(cards)).toHaveLength(2)
    })

    it('should filter cards by the specified view id', () => {
      const store = useSavedViewStore()
      const cards = [
        createMockCard({ id: '1', isBlocked: true }),
        createMockCard({ id: '2', isBlocked: false }),
      ]

      const result = store.filterCards(cards, 'default-blocked')
      expect(result).toHaveLength(1)
      expect(result[0].id).toBe('1')
    })

    it('should filter cards by active view when no explicit id', () => {
      const store = useSavedViewStore()
      store.setActiveView('default-blocked')

      const cards = [
        createMockCard({ id: '1', isBlocked: true }),
        createMockCard({ id: '2', isBlocked: false }),
        createMockCard({ id: '3', isBlocked: true }),
      ]

      const result = store.filterCards(cards)
      expect(result).toHaveLength(2)
    })
  })
})

describe('cardMatchesSavedViewFilter', () => {
  describe('search text', () => {
    it('should match cards by title', () => {
      const card = createMockCard({ title: 'Fix bug in parser' })
      const filter = createBaseFilter({ searchText: 'bug' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })

    it('should match cards by description', () => {
      const card = createMockCard({ description: 'Critical memory leak' })
      const filter = createBaseFilter({ searchText: 'memory' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })

    it('should be case-insensitive', () => {
      const card = createMockCard({ title: 'URGENT Bug' })
      const filter = createBaseFilter({ searchText: 'urgent' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })

    it('should exclude non-matching cards', () => {
      const card = createMockCard({ title: 'Feature request', description: 'Add login' })
      const filter = createBaseFilter({ searchText: 'parser' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(false)
    })
  })

  describe('label names', () => {
    it('should match cards with matching labels', () => {
      const card = createMockCard({
        labels: [{ id: 'l1', boardId: 'b1', name: 'review', colorHex: '#ff0000', createdAt: '', updatedAt: '' }],
      })
      const filter = createBaseFilter({ labelNames: ['review'] })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })

    it('should be case-insensitive for label matching', () => {
      const card = createMockCard({
        labels: [{ id: 'l1', boardId: 'b1', name: 'Review', colorHex: '#ff0000', createdAt: '', updatedAt: '' }],
      })
      const filter = createBaseFilter({ labelNames: ['review'] })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })

    it('should exclude cards without matching labels', () => {
      const card = createMockCard({
        labels: [{ id: 'l1', boardId: 'b1', name: 'bug', colorHex: '#ff0000', createdAt: '', updatedAt: '' }],
      })
      const filter = createBaseFilter({ labelNames: ['review'] })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(false)
    })

    it('should match if any label matches (OR logic)', () => {
      const card = createMockCard({
        labels: [{ id: 'l1', boardId: 'b1', name: 'urgent', colorHex: '#ff0000', createdAt: '', updatedAt: '' }],
      })
      const filter = createBaseFilter({ labelNames: ['review', 'urgent'] })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })
  })

  describe('due date filter', () => {
    it('should pass all cards when filter is "all"', () => {
      const card = createMockCard()
      const filter = createBaseFilter({ dueDateFilter: 'all' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })

    it('should match overdue cards (UTC date in the past)', () => {
      // Use a UTC date string that is clearly in the past
      const twoDaysAgo = new Date()
      twoDaysAgo.setUTCDate(twoDaysAgo.getUTCDate() - 2)
      const card = createMockCard({ dueDate: twoDaysAgo.toISOString() })
      const filter = createBaseFilter({ dueDateFilter: 'overdue' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })

    it('should not match cards without dueDate as overdue', () => {
      const card = createMockCard({ dueDate: null })
      const filter = createBaseFilter({ dueDateFilter: 'overdue' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(false)
    })

    it('should not match today as overdue', () => {
      // A card due today (UTC midnight) should NOT be overdue
      const now = new Date()
      const todayMidnightUTC = new Date(Date.UTC(
        now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()
      ))
      const card = createMockCard({ dueDate: todayMidnightUTC.toISOString() })
      const filter = createBaseFilter({ dueDateFilter: 'overdue' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(false)
    })

    it('should not match future cards as overdue', () => {
      const tomorrow = new Date()
      tomorrow.setUTCDate(tomorrow.getUTCDate() + 1)
      const card = createMockCard({ dueDate: tomorrow.toISOString() })
      const filter = createBaseFilter({ dueDateFilter: 'overdue' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(false)
    })

    it('should match cards due this week', () => {
      const threeDays = new Date()
      threeDays.setUTCDate(threeDays.getUTCDate() + 3)
      const card = createMockCard({ dueDate: threeDays.toISOString() })
      const filter = createBaseFilter({ dueDateFilter: 'due-week' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })

    it('should not match cards due beyond this week', () => {
      const twoWeeks = new Date()
      twoWeeks.setUTCDate(twoWeeks.getUTCDate() + 14)
      const card = createMockCard({ dueDate: twoWeeks.toISOString() })
      const filter = createBaseFilter({ dueDateFilter: 'due-week' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(false)
    })

    it('should match cards with no due date for no-date filter', () => {
      const card = createMockCard({ dueDate: null })
      const filter = createBaseFilter({ dueDateFilter: 'no-date' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })

    it('should exclude cards with a due date for no-date filter', () => {
      const card = createMockCard({ dueDate: new Date().toISOString() })
      const filter = createBaseFilter({ dueDateFilter: 'no-date' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(false)
    })

    it('should match card due today for due-today filter', () => {
      const now = new Date()
      const todayMidnightUTC = new Date(Date.UTC(
        now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), 12, 0, 0
      ))
      const card = createMockCard({ dueDate: todayMidnightUTC.toISOString() })
      const filter = createBaseFilter({ dueDateFilter: 'due-today' })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })
  })

  describe('blocked filter', () => {
    it('should match blocked cards when showBlockedOnly is true', () => {
      const card = createMockCard({ isBlocked: true })
      const filter = createBaseFilter({ showBlockedOnly: true })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })

    it('should exclude non-blocked cards when showBlockedOnly is true', () => {
      const card = createMockCard({ isBlocked: false })
      const filter = createBaseFilter({ showBlockedOnly: true })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(false)
    })

    it('should pass all cards when showBlockedOnly is false', () => {
      const card = createMockCard({ isBlocked: false })
      const filter = createBaseFilter({ showBlockedOnly: false })
      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })
  })

  describe('combined filters', () => {
    it('should apply all filters together (AND logic)', () => {
      const card = createMockCard({
        title: 'Urgent bug fix',
        isBlocked: true,
        labels: [{ id: 'l1', boardId: 'b1', name: 'review', colorHex: '#ff0000', createdAt: '', updatedAt: '' }],
      })

      const filter = createBaseFilter({
        searchText: 'urgent',
        showBlockedOnly: true,
        labelNames: ['review'],
      })

      expect(cardMatchesSavedViewFilter(card, filter)).toBe(true)
    })

    it('should fail if any filter does not match', () => {
      const card = createMockCard({
        title: 'Urgent bug fix',
        isBlocked: false, // not blocked
        labels: [{ id: 'l1', boardId: 'b1', name: 'review', colorHex: '#ff0000', createdAt: '', updatedAt: '' }],
      })

      const filter = createBaseFilter({
        searchText: 'urgent',
        showBlockedOnly: true, // requires blocked
        labelNames: ['review'],
      })

      expect(cardMatchesSavedViewFilter(card, filter)).toBe(false)
    })
  })
})
