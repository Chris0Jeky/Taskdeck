import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope, nextTick, reactive, type EffectScope } from 'vue'
import { flushPromises } from '@vue/test-utils'
import type { Proposal } from '../../types/automation'

const mocks = vi.hoisted(() => ({
  route: { query: {} as Record<string, string>, hash: '' },
  getProposals: vi.fn(),
  getProposal: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => mocks.route,
  useRouter: () => ({ replace: vi.fn().mockResolvedValue(undefined) }),
  isNavigationFailure: () => false,
  NavigationFailureType: { aborted: 4, cancelled: 8, duplicated: 16 },
}))
vi.mock('../../api/automationApi', () => ({ automationApi: mocks }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoards: vi.fn().mockResolvedValue([]) } }))
vi.mock('../../store/toastStore', () => ({ useToastStore: () => ({ error: vi.fn(), info: vi.fn() }) }))
vi.mock('../../utils/errorReporting', () => ({ logError: vi.fn() }))
vi.mock('../../composables/usePerformanceMark', () => ({
  usePerformanceMark: () => ({ start: vi.fn(), end: vi.fn() }),
}))

import {
  REVIEW_QUEUE_REFRESH_MS,
  REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD,
  useReviewProposals,
} from '../../composables/useReviewProposals'

describe('Review retained health with real Vue reactivity (#2915)', () => {
  let scope: EffectScope

  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-09-10T12:00:00Z'))
    vi.clearAllMocks()
    mocks.route = reactive({ query: { boardId: 'board-b' }, hash: '' })
    scope = effectScope()
  })

  afterEach(() => {
    scope.stop()
    vi.useRealTimers()
  })

  async function prime(health: 'stale' | 'refused', hidden?: 'completed' | 'deferred') {
    const proposal: Proposal = {
      id: 'b-1', boardId: 'board-b', status: hidden === 'completed' ? 'Applied' : 'PendingReview',
      sourceType: 'Manual', summary: 'Retained proposal', operations: [],
      createdAt: '2026-09-10T11:00:00Z', expiresAt: '2099-01-01T00:00:00Z',
      deferredUntil: hidden === 'deferred' ? '2026-09-10T12:02:00Z' : null,
      sourceReferenceId: null, requestedByUserId: 'owner-b', riskLevel: 'Low',
      diffPreview: null, validationIssues: null, updatedAt: '2026-09-10T11:00:00Z',
      decidedAt: null, decidedByUserId: null, appliedAt: null, failureReason: null,
      correlationId: 'health-regression', approvedRevisionId: null, latestRevisionId: null,
    }
    mocks.getProposals.mockResolvedValueOnce([proposal])
    const review = scope.run(() => useReviewProposals())!
    await review.loadProposals()
    mocks.getProposals.mockRejectedValue({ response: { status: health === 'stale' ? 500 : 400 } })
    review.startQueueRefresh()
    await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS * REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD)
    review.stopQueueRefresh()
    expect(review.queueRefreshStale.value).toBe(health === 'stale')
    expect(review.queueRefreshRefused.value).toBe(health === 'refused')
    return review
  }

  async function widen() {
    mocks.getProposals.mockRejectedValue({ response: { status: 500 } })
    mocks.route.query = {}
    await nextTick()
    await flushPromises()
  }

  it.each(['stale', 'refused'] as const)('restores %s disclosure when completed rows become visible', async (health) => {
    const review = await prime(health, 'completed')
    await widen()
    expect(review.visibleProposals.value).toHaveLength(0)
    expect(review.queueRefreshStale.value || review.queueRefreshRefused.value).toBe(false)
    const reads = mocks.getProposals.mock.calls.length

    review.showCompleted.value = true
    await nextTick()

    expect(review.visibleProposals.value.map(p => p.id)).toEqual(['b-1'])
    expect(review.queueRefreshStale.value).toBe(health === 'stale')
    expect(review.queueRefreshRefused.value).toBe(health === 'refused')
    expect(mocks.getProposals).toHaveBeenCalledTimes(reads)
  })

  it.each(['clock', 'hash'] as const)('restores disclosure when a deferred row reappears through %s', async (trigger) => {
    const review = await prime('stale', 'deferred')
    await widen()
    expect(review.visibleProposals.value).toHaveLength(0)
    expect(review.queueRefreshStale.value).toBe(false)
    const reads = mocks.getProposals.mock.calls.length

    if (trigger === 'clock') {
      review.startClock()
      await vi.advanceTimersByTimeAsync(120_000)
    } else {
      mocks.route.hash = '#proposal-b-1'
      await nextTick()
    }

    expect(review.visibleProposals.value.map(p => p.id)).toEqual(['b-1'])
    expect(review.queueRefreshStale.value).toBe(true)
    expect(mocks.getProposals).toHaveBeenCalledTimes(reads)
  })

  it('retires recovery on widening even while the retained rows keep their health owner', async () => {
    const review = await prime('stale')
    mocks.getProposals.mockResolvedValueOnce([...review.proposals.value])
    await review.loadProposals()
    expect(review.queueRefreshRecovered.value).toBe(true)
    review.startQueueRefresh()
    await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
    review.stopQueueRefresh()
    expect(review.queueRefreshRecovered.value).toBe(true)

    await widen()

    expect(review.visibleProposals.value).toHaveLength(1)
    expect(review.queueRefreshRecovered.value).toBe(false)
    expect(review.queueRefreshRecoveredKind.value).toBe(null)
  })

  it('preserves a newer warning when retained rows restore their earlier disclosure', async () => {
    const review = await prime('refused', 'completed')
    await widen()
    review.startQueueRefresh()
    await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS * REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD)
    review.stopQueueRefresh()
    expect(review.queueRefreshStale.value).toBe(true)

    review.showCompleted.value = true
    await nextTick()

    expect(review.queueRefreshRefused.value).toBe(true)
    expect(review.queueRefreshStale.value).toBe(true)
  })

  it('keeps health for an empty same-scope queue and never restores it after a fresh landing', async () => {
    const review = await prime('stale', 'completed')
    expect(review.visibleProposals.value).toHaveLength(0)
    expect(review.queueRefreshStale.value).toBe(true)
    await widen()
    mocks.route.query = { boardId: 'board-b' }
    await nextTick()
    await flushPromises()
    expect(review.queueRefreshStale.value).toBe(true)

    mocks.getProposals.mockResolvedValueOnce([])
    await review.loadProposals()
    review.showCompleted.value = true
    await nextTick()
    expect(review.visibleProposals.value).toHaveLength(0)
    expect(review.queueRefreshStale.value).toBe(false)
  })
})
