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
 * Left-border accent stripe class for a notification type.
 *
 * This function stays the single source of truth for WHICH stripe each type
 * gets. The colour itself moved out of the Tailwind palette and into
 * `--td-notify-*` tokens (#1817): `.td-notify-stripe--*` in `src/style.css`
 * paints them, `design-tokens.css` holds the original Tailwind hues at `:root`
 * so Legacy is unchanged, and `paper-legacy-bridge.css` re-tints all five onto
 * Paper hues inside the Paper shell.
 *
 * Colour is never the sole differentiator — each row also carries a text type
 * badge (`typeBadgeClass` / `typeLabel`).
 */
export function typeBorderClass(value: number | string): string {
  const t = normalizeType(value)
  switch (t) {
    case 'ProposalOutcome': return 'td-notify-stripe td-notify-stripe--proposal'
    case 'Mention': return 'td-notify-stripe td-notify-stripe--mention'
    case 'BoardChange': return 'td-notify-stripe td-notify-stripe--board-change'
    case 'Assignment': return 'td-notify-stripe td-notify-stripe--assignment'
    case 'System': return 'td-notify-stripe td-notify-stripe--system'
  }
}

/**
 * Type badge class for a notification type.
 *
 * Same treatment as `typeBorderClass`, one slice later (#1842): this function
 * stays the single source of truth for WHICH badge each type gets, while the
 * colour lives in `--td-notify-*-bg` / `--td-notify-*-fg` tokens that
 * `.td-notify-badge--*` in `src/style.css` paints. `design-tokens.css` holds
 * the original Tailwind hues at `:root` so Legacy is unchanged, and
 * `paper-legacy-bridge.css` re-tints all ten onto Paper values inside the
 * Paper shell.
 *
 * The badge span keeps its own layout utilities at the call site; only colour
 * comes from here. The previous `dark:` variants are gone — `darkMode` is
 * `'class'` and nothing in the app ever sets `dark` on an element, so they
 * never rendered; Paper's night skin is `.paper-night`, handled by the bridge.
 */
export function typeBadgeClass(value: number | string): string {
  const t = normalizeType(value)
  switch (t) {
    case 'ProposalOutcome': return 'td-notify-badge--proposal'
    case 'Mention': return 'td-notify-badge--mention'
    case 'BoardChange': return 'td-notify-badge--board-change'
    case 'Assignment': return 'td-notify-badge--assignment'
    case 'System': return 'td-notify-badge--system'
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
