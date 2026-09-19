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
  REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD,
  REVIEW_QUEUE_REFRESH_MS,
  useReviewProposals,
} from '../../composables/useReviewProposals'

function proposal(id: string, boardId = 'board-b'): Proposal {
  return {
    id,
    boardId,
    status: 'PendingReview',
    sourceType: 'Manual',
    summary: `Proposal ${id}`,
    operations: [],
    createdAt: '2026-09-10T11:00:00Z',
    expiresAt: '2099-01-01T00:00:00Z',
    deferredUntil: null,
    sourceReferenceId: null,
    requestedByUserId: 'owner-b',
    riskLevel: 'Low',
    diffPreview: null,
    validationIssues: null,
    updatedAt: '2026-09-10T11:00:00Z',
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'delayed-pin-recovery',
    approvedRevisionId: null,
    latestRevisionId: null,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

describe('Review delayed-pin recovery announcement (#2930)', () => {
  let scope: EffectScope
  const retained = proposal('b-1')

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

  async function raiseRefusal(review: ReturnType<typeof useReviewProposals>) {
    mocks.getProposals.mockRejectedValue({ response: { status: 400 } })
    review.startQueueRefresh()
    await vi.advanceTimersByTimeAsync(
      REVIEW_QUEUE_REFRESH_MS * REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD,
    )
    review.stopQueueRefresh()
    expect(review.queueRefreshRefused.value).toBe(true)
  }

  async function arrangeRetainedAndCurrentRefusal() {
    mocks.getProposals.mockResolvedValueOnce([retained])
    const review = scope.run(() => useReviewProposals())!
    await review.loadProposals()
    await raiseRefusal(review)

    // Widening parks board B's refusal with its landed row. The failed All-board
    // load retains that row and disclosure without claiming a new landing.
    mocks.getProposals.mockRejectedValue({ response: { status: 500 } })
    mocks.route.query = {}
    await nextTick()
    await flushPromises()
    expect(review.visibleProposals.value.map(item => item.id)).toEqual(['b-1'])
    expect(review.queueRefreshRefused.value).toBe(true)

    // Raise a separate refusal owned by the current All-board request run.
    await raiseRefusal(review)

    // Establish a missing hash pin whose first lookup fails. The next poll will
    // have to complete both a successful list and a delayed by-id read.
    mocks.getProposal.mockRejectedValueOnce({ response: { status: 500 } })
    mocks.route.hash = '#proposal-pin-1'
    await nextTick()
    await flushPromises()

    return review
  }

  it('re-announces recovery after the delayed pin completes the successful composite landing', async () => {
    const review = await arrangeRetainedAndCurrentRefusal()
    const pendingPin = deferred<Proposal>()
    mocks.getProposals.mockResolvedValue([retained])
    mocks.getProposal.mockImplementationOnce(() => pendingPin.promise)

    review.startQueueRefresh()
    vi.advanceTimersByTime(REVIEW_QUEUE_REFRESH_MS)
    await flushPromises()

    expect(mocks.getProposal).toHaveBeenLastCalledWith(
      'pin-1',
      expect.objectContaining({
        skipRetry: true,
        signal: expect.any(AbortSignal),
        expectedStatuses: [400, 403, 404],
      }),
    )
    // The successful list leg tentatively retracts the current refusal, but the
    // retained refusal is still what the reviewer sees while the pin is pending.
    // Its watcher therefore suppresses the preliminary recovery sentence.
    expect(review.queueRefreshRefused.value).toBe(true)
    expect(review.queueRefreshRecovered.value).toBe(false)

    pendingPin.resolve(proposal('pin-1', 'board-c'))
    await flushPromises()
    await nextTick()
    review.stopQueueRefresh()

    expect(review.queueRefreshRefused.value).toBe(false)
    expect(review.queueRefreshRecovered.value).toBe(true)
    expect(review.queueRefreshRecoveredKind.value).toBe('refused')
  })

  it('keeps the retained refusal and no recovery sentence when the delayed pin fails', async () => {
    const review = await arrangeRetainedAndCurrentRefusal()
    const pendingPin = deferred<Proposal>()
    mocks.getProposals.mockResolvedValue([retained])
    mocks.getProposal.mockImplementationOnce(() => pendingPin.promise)

    review.startQueueRefresh()
    vi.advanceTimersByTime(REVIEW_QUEUE_REFRESH_MS)
    await flushPromises()

    expect(mocks.getProposal).toHaveBeenLastCalledWith(
      'pin-1',
      expect.objectContaining({ skipRetry: true, signal: expect.any(AbortSignal) }),
    )
    pendingPin.reject({ response: { status: 500 } })
    await flushPromises()
    await nextTick()
    review.stopQueueRefresh()

    expect(review.queueRefreshRefused.value).toBe(true)
    expect(review.queueRefreshRecovered.value).toBe(false)
    expect(review.queueRefreshRecoveredKind.value).toBe(null)
  })
})
