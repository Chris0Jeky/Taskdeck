import { describe, expect, it } from 'vitest'
import {
  normalizeType,
  typeLabel,
  typeBorderClass,
  typeBadgeClass,
  timeGroup,
  groupNotifications,
} from '../../composables/useNotificationGrouping'
import type { NotificationItem } from '../../types/notifications'

function makeItem(overrides: Partial<NotificationItem> = {}): NotificationItem {
  return {
    id: 'n1',
    userId: 'u1',
    boardId: null,
    type: 'Mention',
    cadence: 'Immediate',
    title: 'Test',
    message: 'Test message',
    sourceEntityType: null,
    sourceEntityId: null,
    isRead: false,
    readAt: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  }
}

describe('normalizeType', () => {
  it('maps numeric values to type names', () => {
    expect(normalizeType(0)).toBe('Mention')
    expect(normalizeType(1)).toBe('Assignment')
    expect(normalizeType(2)).toBe('ProposalOutcome')
    expect(normalizeType(3)).toBe('BoardChange')
    expect(normalizeType(4)).toBe('System')
  })

  it('maps string values to type names', () => {
    expect(normalizeType('Mention')).toBe('Mention')
    expect(normalizeType('Assignment')).toBe('Assignment')
    expect(normalizeType('ProposalOutcome')).toBe('ProposalOutcome')
    expect(normalizeType('BoardChange')).toBe('BoardChange')
    expect(normalizeType('System')).toBe('System')
  })

  it('defaults unknown values to System', () => {
    expect(normalizeType('Unknown')).toBe('System')
    expect(normalizeType(99)).toBe('System')
  })
})

describe('typeLabel', () => {
  it('returns human-readable labels', () => {
    expect(typeLabel('Mention')).toBe('Mention')
    expect(typeLabel('ProposalOutcome')).toBe('Proposal')
    expect(typeLabel('BoardChange')).toBe('Board Change')
    expect(typeLabel('System')).toBe('System')
    expect(typeLabel('Assignment')).toBe('Assignment')
  })
})

/**
 * The stripe colours moved from the Tailwind palette to `--td-notify-*` tokens
 * (#1817) so they follow the active skin; this function is still the single
 * source of the type -> stripe mapping, which is what these tests pin.
 */
describe('typeBorderClass', () => {
  it('returns the proposal stripe for proposals', () => {
    expect(typeBorderClass('ProposalOutcome')).toContain('td-notify-stripe--proposal')
  })

  it('returns the mention stripe for mentions', () => {
    expect(typeBorderClass('Mention')).toContain('td-notify-stripe--mention')
  })

  it('returns the board-change stripe for board changes', () => {
    expect(typeBorderClass('BoardChange')).toContain('td-notify-stripe--board-change')
  })

  it('returns the assignment stripe for assignments', () => {
    expect(typeBorderClass('Assignment')).toContain('td-notify-stripe--assignment')
  })

  it('returns the system stripe for system', () => {
    expect(typeBorderClass('System')).toContain('td-notify-stripe--system')
  })

  it('all classes include the shared stripe geometry class', () => {
    expect(typeBorderClass('Mention')).toContain('td-notify-stripe ')
    expect(typeBorderClass('System')).toContain('td-notify-stripe ')
  })

  it('gives every type a distinct stripe modifier', () => {
    const types = ['ProposalOutcome', 'Mention', 'BoardChange', 'Assignment', 'System']
    const modifiers = types.map((t) => typeBorderClass(t).split(' ').find((c) => c.includes('--')))
    expect(new Set(modifiers).size).toBe(types.length)
  })
})

/**
 * The badge colours moved from the Tailwind palette to `--td-notify-*-bg` /
 * `--td-notify-*-fg` tokens (#1842), the same move #1840 made for the stripes,
 * so they follow the active skin. This function is still the single source of
 * the type -> badge mapping, which is what these tests pin.
 */
describe('typeBadgeClass', () => {
  it('returns the proposal badge for proposals', () => {
    expect(typeBadgeClass('ProposalOutcome')).toBe('td-notify-badge--proposal')
  })

  it('returns the mention badge for mentions', () => {
    expect(typeBadgeClass('Mention')).toBe('td-notify-badge--mention')
  })

  it('returns the board-change badge for board changes', () => {
    expect(typeBadgeClass('BoardChange')).toBe('td-notify-badge--board-change')
  })

  it('returns the assignment badge for assignments', () => {
    expect(typeBadgeClass('Assignment')).toBe('td-notify-badge--assignment')
  })

  it('returns the system badge for system', () => {
    expect(typeBadgeClass('System')).toBe('td-notify-badge--system')
  })

  it('gives every type a distinct badge class', () => {
    const types = ['ProposalOutcome', 'Mention', 'BoardChange', 'Assignment', 'System']
    expect(new Set(types.map(typeBadgeClass)).size).toBe(types.length)
  })

  it('emits no raw Tailwind palette hue and no dead dark: variant', () => {
    // `darkMode: 'class'` with nothing ever setting `dark` meant the four
    // `dark:` utilities never rendered; the light ones were Obsidian-era hues
    // inside the cream Paper shell (#1842).
    const types = ['ProposalOutcome', 'Mention', 'BoardChange', 'Assignment', 'System']
    for (const t of types) {
      expect(typeBadgeClass(t)).not.toMatch(/\bdark:/)
      expect(typeBadgeClass(t)).not.toMatch(/\b(bg|text)-(amber|blue|green|purple|gray)-\d{3}\b/)
    }
  })
})

describe('timeGroup', () => {
  const now = new Date('2026-03-31T14:00:00Z')

  it('classifies today', () => {
    expect(timeGroup('2026-03-31T10:00:00Z', now)).toBe('Today')
  })

  it('classifies yesterday', () => {
    expect(timeGroup('2026-03-30T20:00:00Z', now)).toBe('Yesterday')
  })

  it('classifies this week', () => {
    expect(timeGroup('2026-03-27T12:00:00Z', now)).toBe('This week')
  })

  it('classifies older', () => {
    expect(timeGroup('2026-03-20T12:00:00Z', now)).toBe('Older')
  })
})

describe('groupNotifications', () => {
  const now = new Date('2026-03-31T14:00:00Z')

  it('returns empty for empty input', () => {
    expect(groupNotifications([], now)).toEqual([])
  })

  it('single notification is not collapsed', () => {
    const items = [makeItem({ id: 'n1', type: 'Mention', createdAt: '2026-03-31T10:00:00Z' })]
    const groups = groupNotifications(items, now)
    expect(groups).toHaveLength(1)
    expect(groups[0].isCollapsed).toBe(false)
    expect(groups[0].summaryLabel).toBeNull()
    expect(groups[0].timeHeader).toBe('Today')
    expect(groups[0].items).toHaveLength(1)
  })

  it('collapses 2+ consecutive same-type notifications', () => {
    const items = [
      makeItem({ id: 'n1', type: 'Mention', createdAt: '2026-03-31T13:00:00Z' }),
      makeItem({ id: 'n2', type: 'Mention', createdAt: '2026-03-31T12:00:00Z' }),
      makeItem({ id: 'n3', type: 'Mention', createdAt: '2026-03-31T11:00:00Z' }),
    ]
    const groups = groupNotifications(items, now)
    expect(groups).toHaveLength(1)
    expect(groups[0].isCollapsed).toBe(true)
    expect(groups[0].summaryLabel).toBe('3 mention notifications')
    expect(groups[0].items).toHaveLength(3)
  })

  it('does not collapse different types', () => {
    const items = [
      makeItem({ id: 'n1', type: 'Mention', createdAt: '2026-03-31T13:00:00Z' }),
      makeItem({ id: 'n2', type: 'ProposalOutcome', createdAt: '2026-03-31T12:00:00Z' }),
    ]
    const groups = groupNotifications(items, now)
    expect(groups).toHaveLength(2)
    expect(groups[0].isCollapsed).toBe(false)
    expect(groups[1].isCollapsed).toBe(false)
  })

  it('splits groups across time boundaries', () => {
    const items = [
      makeItem({ id: 'n1', type: 'Mention', createdAt: '2026-03-31T10:00:00Z' }),
      makeItem({ id: 'n2', type: 'Mention', createdAt: '2026-03-30T20:00:00Z' }),
    ]
    const groups = groupNotifications(items, now)
    expect(groups).toHaveLength(2)
    expect(groups[0].timeHeader).toBe('Today')
    expect(groups[1].timeHeader).toBe('Yesterday')
  })

  it('mixes collapsed and single groups', () => {
    const items = [
      makeItem({ id: 'n1', type: 'Mention', createdAt: '2026-03-31T13:00:00Z' }),
      makeItem({ id: 'n2', type: 'Mention', createdAt: '2026-03-31T12:00:00Z' }),
      makeItem({ id: 'n3', type: 'ProposalOutcome', createdAt: '2026-03-31T11:00:00Z' }),
    ]
    const groups = groupNotifications(items, now)
    expect(groups).toHaveLength(2)
    expect(groups[0].isCollapsed).toBe(true)
    expect(groups[0].summaryLabel).toBe('2 mention notifications')
    expect(groups[1].isCollapsed).toBe(false)
  })
})
