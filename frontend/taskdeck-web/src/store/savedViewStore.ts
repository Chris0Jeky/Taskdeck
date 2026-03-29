import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { SAVED_VIEWS_STORAGE_KEY } from '../utils/storageKeys'
import type { Card } from '../types/board'

// ── Types ──

export type SavedViewDueDateFilter = 'all' | 'overdue' | 'due-today' | 'due-week' | 'no-date'

export interface SavedViewFilter {
  /** Free-text search across card title and description */
  searchText: string
  /** Only show cards with matching label names (case-insensitive) */
  labelNames: string[]
  /** Due-date window filter */
  dueDateFilter: SavedViewDueDateFilter
  /** Show only blocked cards */
  showBlockedOnly: boolean
}

export interface SavedView {
  id: string
  name: string
  icon: string
  filter: SavedViewFilter
  /** Built-in views cannot be deleted or renamed */
  isDefault: boolean
  createdAt: string
}

// ── Default starter views ──

function createDefaultViews(): SavedView[] {
  const now = new Date().toISOString()
  return [
    {
      id: 'default-blocked',
      name: 'Blocked Work',
      icon: 'X',
      filter: {
        searchText: '',
        labelNames: [],
        dueDateFilter: 'all',
        showBlockedOnly: true,
      },
      isDefault: true,
      createdAt: now,
    },
    {
      id: 'default-due-week',
      name: 'Due This Week',
      icon: 'W',
      filter: {
        searchText: '',
        labelNames: [],
        dueDateFilter: 'due-week',
        showBlockedOnly: false,
      },
      isDefault: true,
      createdAt: now,
    },
    {
      id: 'default-needs-review',
      name: 'Needs Review',
      icon: 'R',
      filter: {
        searchText: '',
        labelNames: ['review', 'needs review', 'needs-review'],
        dueDateFilter: 'all',
        showBlockedOnly: false,
      },
      isDefault: true,
      createdAt: now,
    },
    {
      id: 'default-overdue',
      name: 'Overdue',
      icon: '!',
      filter: {
        searchText: '',
        labelNames: [],
        dueDateFilter: 'overdue',
        showBlockedOnly: false,
      },
      isDefault: true,
      createdAt: now,
    },
  ]
}

// ── Filter matching ──

export function cardMatchesSavedViewFilter(card: Card, filter: SavedViewFilter): boolean {
  // Search text
  if (filter.searchText) {
    const searchLower = filter.searchText.toLowerCase()
    const matchesTitle = card.title.toLowerCase().includes(searchLower)
    const matchesDescription = card.description?.toLowerCase().includes(searchLower)
    if (!matchesTitle && !matchesDescription) return false
  }

  // Label name filter (case-insensitive)
  if (filter.labelNames.length > 0) {
    const cardLabelNames = card.labels.map((l) => l.name.toLowerCase())
    const hasMatchingLabel = filter.labelNames.some((name) =>
      cardLabelNames.includes(name.toLowerCase()),
    )
    if (!hasMatchingLabel) return false
  }

  // Due date filter
  if (filter.dueDateFilter !== 'all') {
    const now = new Date()
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
    const weekFromNow = new Date(today)
    weekFromNow.setDate(weekFromNow.getDate() + 7)

    switch (filter.dueDateFilter) {
      case 'overdue':
        if (!card.dueDate || new Date(card.dueDate) >= today) return false
        break
      case 'due-today': {
        if (!card.dueDate) return false
        const dueDate = new Date(card.dueDate)
        const dueDateDay = new Date(dueDate.getFullYear(), dueDate.getMonth(), dueDate.getDate())
        if (dueDateDay.getTime() !== today.getTime()) return false
        break
      }
      case 'due-week': {
        if (!card.dueDate) return false
        const due = new Date(card.dueDate)
        if (due < today || due > weekFromNow) return false
        break
      }
      case 'no-date':
        if (card.dueDate) return false
        break
    }
  }

  // Blocked
  if (filter.showBlockedOnly && !card.isBlocked) {
    return false
  }

  return true
}

// ── Store ──

export const useSavedViewStore = defineStore('savedViews', () => {
  const views = ref<SavedView[]>(createDefaultViews())
  const activeViewId = ref<string | null>(null)

  // ── Computed ──

  const activeView = computed<SavedView | null>(() =>
    activeViewId.value
      ? views.value.find((v) => v.id === activeViewId.value) ?? null
      : null,
  )

  const defaultViews = computed(() => views.value.filter((v) => v.isDefault))
  const customViews = computed(() => views.value.filter((v) => !v.isDefault))

  // ── Persistence ──

  function persist() {
    try {
      const custom = views.value.filter((v) => !v.isDefault)
      localStorage.setItem(SAVED_VIEWS_STORAGE_KEY, JSON.stringify(custom))
    } catch {
      // localStorage full or unavailable — silently skip
    }
  }

  function restore() {
    try {
      const raw = localStorage.getItem(SAVED_VIEWS_STORAGE_KEY)
      if (!raw) return

      const parsed = JSON.parse(raw)
      if (!Array.isArray(parsed)) return

      const defaults = createDefaultViews()
      const customViews: SavedView[] = parsed
        .filter(
          (v: unknown): v is SavedView =>
            typeof v === 'object' &&
            v !== null &&
            typeof (v as SavedView).id === 'string' &&
            typeof (v as SavedView).name === 'string' &&
            typeof (v as SavedView).filter === 'object',
        )
        .map((v: SavedView) => ({ ...v, isDefault: false }))

      views.value = [...defaults, ...customViews]
    } catch {
      // Invalid JSON — start fresh with defaults only
      views.value = createDefaultViews()
    }
  }

  // ── Actions ──

  function setActiveView(viewId: string | null) {
    activeViewId.value = viewId
  }

  function createView(name: string, icon: string, filter: SavedViewFilter): SavedView {
    const view: SavedView = {
      id: `custom-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      name,
      icon,
      filter: { ...filter },
      isDefault: false,
      createdAt: new Date().toISOString(),
    }
    views.value = [...views.value, view]
    persist()
    return view
  }

  function updateView(viewId: string, updates: Partial<Pick<SavedView, 'name' | 'icon' | 'filter'>>) {
    const idx = views.value.findIndex((v) => v.id === viewId)
    if (idx === -1) return
    const existing = views.value[idx]
    if (existing.isDefault) return // cannot edit default views

    views.value = views.value.map((v) =>
      v.id === viewId
        ? {
            ...v,
            ...(updates.name !== undefined ? { name: updates.name } : {}),
            ...(updates.icon !== undefined ? { icon: updates.icon } : {}),
            ...(updates.filter !== undefined ? { filter: { ...updates.filter } } : {}),
          }
        : v,
    )
    persist()
  }

  function deleteView(viewId: string) {
    const existing = views.value.find((v) => v.id === viewId)
    if (!existing || existing.isDefault) return // cannot delete default views

    views.value = views.value.filter((v) => v.id !== viewId)
    if (activeViewId.value === viewId) {
      activeViewId.value = null
    }
    persist()
  }

  function filterCards(cards: Card[], viewId?: string): Card[] {
    const id = viewId ?? activeViewId.value
    if (!id) return cards

    const view = views.value.find((v) => v.id === id)
    if (!view) return cards

    return cards.filter((card) => cardMatchesSavedViewFilter(card, view.filter))
  }

  // Initialize from localStorage
  restore()

  return {
    // State
    views,
    activeViewId,

    // Computed
    activeView,
    defaultViews,
    customViews,

    // Actions
    setActiveView,
    createView,
    updateView,
    deleteView,
    filterCards,
    persist,
    restore,
  }
})
