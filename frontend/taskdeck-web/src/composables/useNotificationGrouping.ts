import type { NotificationItem, NotificationTypeName } from '../types/notifications'

/**
 * Normalize a notification type (number or string) to a canonical string name.
 */
export function normalizeType(value: number | string): NotificationTypeName {
  const s = String(value)
  if (s === '0' || s === 'Mention') return 'Mention'
  if (s === '1' || s === 'Assignment') return 'Assignment'
  if (s === '2' || s === 'ProposalOutcome') return 'ProposalOutcome'
  if (s === '3' || s === 'BoardChange') return 'BoardChange'
  if (s === '4' || s === 'System') return 'System'
  return 'System'
}

/**
 * Human-readable label for a notification type.
 */
export function typeLabel(value: number | string): string {
  const t = normalizeType(value)
  switch (t) {
    case 'Mention': return 'Mention'
    case 'Assignment': return 'Assignment'
    case 'ProposalOutcome': return 'Proposal'
    case 'BoardChange': return 'Board Change'
    case 'System': return 'System'
  }
}

/**
 * Tailwind border-left color class for a notification type.
 * Returns a left-border class. Also includes an aria-compatible
 * label via the type badge so color is not the sole differentiator.
 */
export function typeBorderClass(value: number | string): string {
  const t = normalizeType(value)
  switch (t) {
    case 'ProposalOutcome': return 'border-l-4 border-l-amber-500'
    case 'Mention': return 'border-l-4 border-l-blue-500'
    case 'BoardChange': return 'border-l-4 border-l-green-500'
    case 'Assignment': return 'border-l-4 border-l-purple-500'
    case 'System': return 'border-l-4 border-l-gray-400'
  }
}

/**
 * Tailwind badge classes for a notification type.
 */
export function typeBadgeClass(value: number | string): string {
  const t = normalizeType(value)
  switch (t) {
    case 'ProposalOutcome': return 'bg-amber-100 text-amber-800 dark:bg-amber-900 dark:text-amber-200'
    case 'Mention': return 'bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200'
    case 'BoardChange': return 'bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200'
    case 'Assignment': return 'bg-purple-100 text-purple-800 dark:bg-purple-900 dark:text-purple-200'
    case 'System': return 'bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300'
  }
}

// ---------- Time-based grouping ----------

export type TimeGroup = 'Today' | 'Yesterday' | 'This week' | 'Older'

/**
 * Assign a notification to a time-based group relative to the given "now" date.
 */
export function timeGroup(createdAt: string, now: Date = new Date()): TimeGroup {
  const created = new Date(createdAt)
  const todayStart = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const yesterdayStart = new Date(todayStart)
  yesterdayStart.setDate(yesterdayStart.getDate() - 1)
  const weekStart = new Date(todayStart)
  weekStart.setDate(weekStart.getDate() - 6)

  if (created >= todayStart) return 'Today'
  if (created >= yesterdayStart) return 'Yesterday'
  if (created >= weekStart) return 'This week'
  return 'Older'
}

// ---------- Smart grouping ----------

export interface NotificationGroup {
  /** Unique key for v-for */
  key: string
  /** The time-based header this group belongs to */
  timeHeader: TimeGroup
  /** If true, this group is a collapsed summary of multiple same-type notifications */
  isCollapsed: boolean
  /** Summary label when collapsed, e.g. "3 automation proposals updated" */
  summaryLabel: string | null
  /** The individual notifications in this group */
  items: NotificationItem[]
}

/**
 * Group notifications by time header and collapse consecutive same-type
 * notifications into summary groups.
 *
 * Notifications are expected to be sorted by createdAt descending (newest first).
 */
export function groupNotifications(
  notifications: NotificationItem[],
  now: Date = new Date(),
): NotificationGroup[] {
  if (notifications.length === 0) return []

  const groups: NotificationGroup[] = []
  let currentTimeHeader: TimeGroup | null = null
  let pendingItems: NotificationItem[] = []
  let pendingType: NotificationTypeName | null = null

  function flushPending() {
    if (pendingItems.length === 0 || currentTimeHeader === null) return

    if (pendingItems.length >= 2) {
      const label = `${pendingItems.length} ${typeLabel(pendingItems[0].type).toLowerCase()} notifications`
      groups.push({
        key: `group-${pendingItems[0].id}`,
        timeHeader: currentTimeHeader,
        isCollapsed: true,
        summaryLabel: label,
        items: [...pendingItems],
      })
    } else {
      groups.push({
        key: `single-${pendingItems[0].id}`,
        timeHeader: currentTimeHeader,
        isCollapsed: false,
        summaryLabel: null,
        items: [...pendingItems],
      })
    }
    pendingItems = []
    pendingType = null
  }

  for (const notification of notifications) {
    const header = timeGroup(notification.createdAt, now)
    const nType = normalizeType(notification.type)

    // Time header changed — flush and start new section
    if (header !== currentTimeHeader) {
      flushPending()
      currentTimeHeader = header
    }

    // Same type as pending — accumulate
    if (nType === pendingType) {
      pendingItems.push(notification)
      continue
    }

    // Different type — flush previous and start new pending
    flushPending()
    pendingType = nType
    pendingItems = [notification]
  }

  flushPending()
  return groups
}
