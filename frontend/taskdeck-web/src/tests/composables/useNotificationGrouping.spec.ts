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

describe('typeBorderClass', () => {
  it('returns amber border for proposals', () => {
    expect(typeBorderClass('ProposalOutcome')).toContain('border-l-amber-500')
  })

  it('returns blue border for mentions', () => {
    expect(typeBorderClass('Mention')).toContain('border-l-blue-500')
  })

  it('returns green border for board changes', () => {
    expect(typeBorderClass('BoardChange')).toContain('border-l-green-500')
  })

  it('returns purple border for assignments', () => {
    expect(typeBorderClass('Assignment')).toContain('border-l-purple-500')
  })

  it('returns gray border for system', () => {
    expect(typeBorderClass('System')).toContain('border-l-gray-400')
  })

  it('all classes include border-l-4', () => {
    expect(typeBorderClass('Mention')).toContain('border-l-4')
    expect(typeBorderClass('System')).toContain('border-l-4')
  })
})

describe('typeBadgeClass', () => {
  it('returns amber badge for proposals', () => {
    expect(typeBadgeClass('ProposalOutcome')).toContain('bg-amber-100')
  })

  it('returns blue badge for mentions', () => {
    expect(typeBadgeClass('Mention')).toContain('bg-blue-100')
  })

  it('includes dark mode variant', () => {
    expect(typeBadgeClass('Mention')).toContain('dark:bg-blue-900')
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
