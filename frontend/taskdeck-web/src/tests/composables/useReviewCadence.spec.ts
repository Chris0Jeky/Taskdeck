import { describe, expect, it } from 'vitest'
import { effectScope, ref } from 'vue'
import {
  CADENCE_WINDOW_DAYS,
  buildWeeklyCadence,
  useReviewCadence,
} from '../../composables/useReviewCadence'
import type { Proposal as ApiProposal } from '../../types/automation'

const USER = 'user-1'
const OTHER_USER = 'user-2'

/** Local midnight of the day containing `ms`, so fixtures land mid-day. */
function localNoon(offsetDays: number, base: number): number {
  const d = new Date(base)
  d.setHours(12, 0, 0, 0)
  d.setDate(d.getDate() - offsetDays)
  return d.getTime()
}

function makeProposal(overrides: Partial<ApiProposal> = {}): ApiProposal {
  return {
    id: 'p-1',
    sourceType: 'Queue',
    sourceReferenceId: null,
    boardId: 'board-1',
    requestedByUserId: USER,
    status: 'Applied',
    riskLevel: 'Low',
    summary: 'A proposal',
    diffPreview: null,
    validationIssues: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    expiresAt: new Date().toISOString(),
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'c-1',
    operations: [],
    approvedRevisionId: null,
    ...overrides,
  }
}

/** A proposal decided by `userId` `daysAgo` calendar days before `nowMs`. */
function decided(daysAgo: number, nowMs: number, overrides: Partial<ApiProposal> = {}): ApiProposal {
  return makeProposal({
    id: `p-${daysAgo}-${Math.random().toString(36).slice(2, 8)}`,
    decidedAt: new Date(localNoon(daysAgo, nowMs)).toISOString(),
    decidedByUserId: USER,
    ...overrides,
  })
}

describe('buildWeeklyCadence', () => {
  const now = new Date('2026-08-19T15:30:00').getTime()

  it('counts the current user\'s decisions into one bucket per calendar day, newest last', () => {
    const proposals = [
      decided(0, now),
      decided(0, now),
      decided(0, now),
      decided(2, now),
      decided(6, now),
    ]

    const cadence = buildWeeklyCadence(proposals, { nowMs: now, userId: USER })

    expect(cadence).toHaveLength(CADENCE_WINDOW_DAYS)
    // index 6 = today, index 4 = two days ago, index 0 = six days ago
    expect(cadence).toEqual([1, 0, 0, 0, 1, 0, 3])
  })

  it('counts only decisions made by the current user', () => {
    const proposals = [
      decided(0, now),
      decided(0, now, { decidedByUserId: OTHER_USER }),
      decided(1, now, { decidedByUserId: OTHER_USER }),
    ]

    expect(buildWeeklyCadence(proposals, { nowMs: now, userId: USER })).toEqual([
      0, 0, 0, 0, 0, 0, 1,
    ])
  })

  it('honours the board-scope predicate so the bars match the filtered queue', () => {
    const proposals = [
      decided(0, now, { boardId: 'board-1' }),
      decided(1, now, { boardId: 'board-2' }),
    ]

    const cadence = buildWeeklyCadence(proposals, {
      nowMs: now,
      userId: USER,
      includeBoard: (boardId) => boardId === 'board-1',
    })

    expect(cadence).toEqual([0, 0, 0, 0, 0, 0, 1])
  })

  it('drops decisions outside the seven-day window in either direction', () => {
    const proposals = [
      decided(7, now), // one day too old
      decided(30, now),
      decided(-1, now), // future-dated (clock skew) — never folded into today
    ]

    expect(buildWeeklyCadence(proposals, { nowMs: now, userId: USER })).toBeUndefined()
  })

  it('ignores undecided proposals and unparseable decision timestamps', () => {
    const proposals = [
      makeProposal({ id: 'undecided', decidedAt: null, decidedByUserId: USER }),
      makeProposal({ id: 'garbage', decidedAt: 'not-a-date', decidedByUserId: USER }),
    ]

    expect(buildWeeklyCadence(proposals, { nowMs: now, userId: USER })).toBeUndefined()
  })

  it('returns undefined rather than a fabricated array when there is no history', () => {
    expect(buildWeeklyCadence([], { nowMs: now, userId: USER })).toBeUndefined()
    expect(buildWeeklyCadence(null, { nowMs: now, userId: USER })).toBeUndefined()
    expect(buildWeeklyCadence(undefined, { nowMs: now, userId: USER })).toBeUndefined()
  })

  it('returns undefined when there is no session user to attribute decisions to', () => {
    const proposals = [decided(0, now)]

    expect(buildWeeklyCadence(proposals, { nowMs: now, userId: null })).toBeUndefined()
    expect(buildWeeklyCadence(proposals, { nowMs: now, userId: '' })).toBeUndefined()
    expect(buildWeeklyCadence(proposals, { nowMs: now, userId: undefined })).toBeUndefined()
  })

  it('keeps a real all-but-one-empty week rather than collapsing it', () => {
    // A single real decision six days ago is history: the zeroes around it are
    // measured, not invented, so the strip must still render.
    const cadence = buildWeeklyCadence([decided(6, now)], { nowMs: now, userId: USER })
    expect(cadence).toEqual([1, 0, 0, 0, 0, 0, 0])
  })
})

describe('useReviewCadence', () => {
  const now = new Date('2026-08-19T15:30:00').getTime()

  it('recomputes when proposals, the clock, or the session user change', () => {
    const scope = effectScope()
    const proposals = ref<ApiProposal[]>([])
    const nowMs = ref(now)
    const userId = ref<string | null>(null)

    const cadence = scope.run(() => useReviewCadence(proposals, nowMs, userId))!

    // No user, no proposals -> nothing to draw.
    expect(cadence.value).toBeUndefined()

    proposals.value = [decided(0, now), decided(0, now)]
    expect(cadence.value).toBeUndefined()

    userId.value = USER
    expect(cadence.value).toEqual([0, 0, 0, 0, 0, 0, 2])

    // Advance the clock a day: the same decisions slide one column left.
    nowMs.value = localNoon(-1, now)
    expect(cadence.value).toEqual([0, 0, 0, 0, 0, 2, 0])

    scope.stop()
  })

  it('accepts a getter for the session user id', () => {
    const scope = effectScope()
    const proposals = ref<ApiProposal[]>([decided(0, now)])
    const nowMs = ref(now)

    const cadence = scope.run(() => useReviewCadence(proposals, nowMs, () => USER))!
    expect(cadence.value).toEqual([0, 0, 0, 0, 0, 0, 1])

    scope.stop()
  })
})
