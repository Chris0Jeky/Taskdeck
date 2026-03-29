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

// ── Helpers ──

/** Strip time component and return UTC-midnight for date-only comparisons.
 *  Using UTC throughout avoids local-timezone midnight-boundary mismatches
 *  when card.dueDate arrives as an ISO/UTC string. */
function toUTCDateOnly(d: Date): Date {
  return new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()))
}

/** Today as UTC midnight */
function todayUTC(): Date {
  return toUTCDateOnly(new Date())
}

/** Stable timestamp for built-in default views (avoids changing on every reload) */
const DEFAULT_VIEW_CREATED_AT = '2024-01-01T00:00:00.000Z'

const DEFAULT_FILTER: SavedViewFilter = {
  searchText: '',
  labelNames: [],
  dueDateFilter: 'all',
  showBlockedOnly: false,
}

// ── Default starter views ──

function createDefaultViews(): SavedView[] {
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
      createdAt: DEFAULT_VIEW_CREATED_AT,
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
      createdAt: DEFAULT_VIEW_CREATED_AT,
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
      createdAt: DEFAULT_VIEW_CREATED_AT,
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
      createdAt: DEFAULT_VIEW_CREATED_AT,
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

  // Due date filter — all comparisons use UTC date-only to avoid
  // timezone mismatches between local new Date() and ISO/UTC dueDate strings.
  if (filter.dueDateFilter !== 'all') {
    const today = todayUTC()
    const weekFromNow = new Date(today)
    weekFromNow.setUTCDate(weekFromNow.getUTCDate() + 7)

    switch (filter.dueDateFilter) {
      case 'overdue': {
        if (!card.dueDate) return false
        const dueDay = toUTCDateOnly(new Date(card.dueDate))
        if (dueDay >= today) return false
        break
      }
      case 'due-today': {
        if (!card.dueDate) return false
        const dueDay = toUTCDateOnly(new Date(card.dueDate))
        if (dueDay.getTime() !== today.getTime()) return false
        break
      }
      case 'due-week': {
        if (!card.dueDate) return false
        const dueDay = toUTCDateOnly(new Date(card.dueDate))
        if (dueDay < today || dueDay > weekFromNow) return false
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

// ── Validation helpers ──

/** Validate and normalize a filter object restored from localStorage,
 *  applying defaults for any missing or invalid properties. */
function normalizeFilter(raw: unknown): SavedViewFilter {
  if (typeof raw !== 'object' || raw === null) return { ...DEFAULT_FILTER }

  const obj = raw as Record<string, unknown>
  return {
    searchText: typeof obj.searchText === 'string' ? obj.searchText : DEFAULT_FILTER.searchText,
    labelNames:
      Array.isArray(obj.labelNames) && obj.labelNames.every((n: unknown) => typeof n === 'string')
        ? (obj.labelNames as string[])
        : [...DEFAULT_FILTER.labelNames],
    dueDateFilter: isValidDueDateFilter(obj.dueDateFilter)
      ? obj.dueDateFilter
      : DEFAULT_FILTER.dueDateFilter,
    showBlockedOnly:
      typeof obj.showBlockedOnly === 'boolean'
        ? obj.showBlockedOnly
        : DEFAULT_FILTER.showBlockedOnly,
  }
}

function isValidDueDateFilter(value: unknown): value is SavedViewDueDateFilter {
  return (
    value === 'all' ||
    value === 'overdue' ||
    value === 'due-today' ||
    value === 'due-week' ||
    value === 'no-date'
  )
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
      const restoredViews: SavedView[] = parsed
        .filter(
          (v: unknown): v is Record<string, unknown> =>
            typeof v === 'object' &&
            v !== null &&
            typeof (v as Record<string, unknown>).id === 'string' &&
            typeof (v as Record<string, unknown>).name === 'string' &&
            typeof (v as Record<string, unknown>).filter === 'object',
        )
        .map((v: Record<string, unknown>) => ({
          id: v.id as string,
          name: v.name as string,
          icon: typeof v.icon === 'string' ? v.icon : '?',
          filter: normalizeFilter(v.filter),
          isDefault: false,
          createdAt: typeof v.createdAt === 'string' ? v.createdAt : new Date().toISOString(),
        }))

      views.value = [...defaults, ...restoredViews]
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
