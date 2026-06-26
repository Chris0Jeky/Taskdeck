import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'

const {
  watchers,
  mockRouter,
  mockRoute,
  mockAutomationApi,
  mockBoardsApi,
  mockToast,
  mockOnScopeDispose,
} = vi.hoisted(() => {
  return {
    watchers: [] as Array<[unknown, () => void]>,
    mockRouter: { push: vi.fn().mockResolvedValue(undefined), replace: vi.fn().mockResolvedValue(undefined) },
    mockRoute: { hash: '', query: {} as Record<string, string> },
    mockAutomationApi: {
      getProposals: vi.fn(() => Promise.resolve([])),
      getProposal: vi.fn(),
    },
    mockBoardsApi: {
      getBoards: vi.fn(() => Promise.resolve([])),
    },
    mockToast: { error: vi.fn(), info: vi.fn() },
    mockOnScopeDispose: vi.fn(),
  }
})

vi.mock('vue', () => ({
  ref: (v: unknown) => ({ value: v }),
  computed: (fn: () => unknown) => ({ get value() { return fn() } }),
  watch: (source: unknown, cb: () => void) => { watchers.push([source, cb]) },
  nextTick: vi.fn(() => Promise.resolve()),
  onScopeDispose: mockOnScopeDispose,
}))

vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
  useRouter: () => mockRouter,
  isNavigationFailure: () => false,
  NavigationFailureType: { aborted: 4, cancelled: 8, duplicated: 16 },
}))

vi.mock('../../api/automationApi', () => ({
  automationApi: mockAutomationApi,
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: mockBoardsApi,
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => mockToast,
}))

vi.mock('../../utils/automation', () => ({
  normalizeProposalStatus: (v: unknown) => {
    if (typeof v === 'number') {
      return ['PendingReview', 'Approved', 'Rejected', 'Applied', 'Failed', 'Expired', 'Dismissed'][v] ?? 'PendingReview'
    }
    return v
  },
  normalizeProposalSourceType: (v: unknown) => {
    if (typeof v === 'number') return ['Queue', 'Chat', 'Manual'][v] ?? 'Manual'
    return v
  },
}))

vi.mock('../../utils/inputAssist', () => ({
  buildInputAssistOptions: (seeds: Array<{ value: string; label: string }>) =>
    seeds.map((s) => ({ value: s.value, label: s.label })),
}))

vi.mock('../../utils/navigation', () => ({
  normalizeBoardIdQueryParam: (v: unknown) => v ?? null,
}))

vi.mock('../../composables/usePerformanceMark', () => ({
  usePerformanceMark: () => ({ start: vi.fn(), end: vi.fn() }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_e: unknown, fallback: string) => ({ message: fallback }),
}))

vi.mock('../../utils/errorReporting', () => ({
  logError: vi.fn(),
}))

import {
  STALE_PROPOSAL_MS,
  isProposalApplyActionable,
  isProposalApproveActionable,
  isProposalRejectActionable,
  isProposalStale,
  useReviewProposals,
} from '../../composables/useReviewProposals'

// The apply/reject parity cases now assert against HARD-CODED truth tables (see
// the cases array below) rather than a re-derived mirror, so a flipped rule
// actually fails the test instead of self-confirming (ADR-0038 / #1124 drift
// class). The one remaining local mirror is for stale, which is paired with its
// own hard-coded .toBe() assertions in the stale test.
function localIsStaleProposal(status: string, createdAtMs: number, nowMs: number): boolean {
  if (status !== 'PendingReview') return false
  return nowMs - createdAtMs >= 24 * 60 * 60 * 1000
}

function watcherForCurrentSourceValue(expected: unknown) {
  const watcher = watchers.find(([source]) => typeof source === 'function' && (source as () => unknown)() === expected)
  expect(watcher).toBeDefined()
  return watcher!
}

function makeProposal(overrides: Partial<{
  id: string; status: string; sourceType: string; sourceReferenceId: string | null;
  boardId: string | null; createdAt: string; expiresAt: string;
}> = {}) {
  return {
    id: overrides.id ?? 'p-1',
    status: overrides.status ?? 'PendingReview',
    sourceType: overrides.sourceType ?? 'Manual',
    sourceReferenceId: overrides.sourceReferenceId ?? null,
    boardId: overrides.boardId ?? null,
    requestedByUserId: 'user-1',
    riskLevel: 'Low',
    summary: 'test',
    diffPreview: null,
    validationIssues: null,
    createdAt: overrides.createdAt ?? '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    expiresAt: overrides.expiresAt ?? '2099-01-01T00:00:00Z',
    decidedAt: null,
    decidedByUserId: null,
  }
}

describe('useReviewProposals', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    watchers.length = 0
    mockRoute.hash = ''
    mockRoute.query = {}
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  describe('visibleProposals filtering', () => {
    it('excludes dismissed proposals', () => {
      const rp = useReviewProposals()
      rp.proposals.value = [
        makeProposal({ id: '1', status: 'Dismissed' }),
        makeProposal({ id: '2', status: 'PendingReview' }),
      ] as any
      expect(rp.visibleProposals.value.map((p: any) => p.id)).toEqual(['2'])
    })

    it('hides completed proposals when showCompleted is false', () => {
      const rp = useReviewProposals()
      rp.showCompleted.value = false
      rp.proposals.value = [
        makeProposal({ id: '1', status: 'Applied' }),
        makeProposal({ id: '2', status: 'Rejected' }),
        makeProposal({ id: '3', status: 'PendingReview' }),
      ] as any
      expect(rp.visibleProposals.value.map((p: any) => p.id)).toEqual(['3'])
    })

    it('shows expired proposals regardless of showCompleted', () => {
      const rp = useReviewProposals()
      rp.showCompleted.value = false
      rp.nowMs.value = Date.now() + 1000000000
      rp.proposals.value = [
        makeProposal({ id: '1', status: 'PendingReview', expiresAt: '2020-01-01T00:00:00Z' }),
      ] as any
      expect(rp.visibleProposals.value.length).toBe(1)
    })

    it('filters by active board', () => {
      mockRoute.query = { boardId: 'board-A' }
      const rp = useReviewProposals()
      rp.proposals.value = [
        makeProposal({ id: '1', boardId: 'board-A' }),
        makeProposal({ id: '2', boardId: 'board-B' }),
      ] as any
      expect(rp.visibleProposals.value.map((p: any) => p.id)).toEqual(['1'])
    })

    it('hides a snoozed PendingReview proposal and resurfaces it after the clock passes deferredUntil', () => {
      const rp = useReviewProposals()
      const base = new Date('2026-06-13T12:00:00.000Z').getTime()
      rp.nowMs.value = base
      const deferredUntil = new Date(base + 60 * 60_000).toISOString() // 1h ahead
      rp.proposals.value = [
        { ...makeProposal({ id: 'snoozed', status: 'PendingReview' }), deferredUntil },
        makeProposal({ id: 'live', status: 'PendingReview' }),
      ] as any

      // While snoozed, the deferred proposal is hidden but the live one stays.
      expect(rp.isProposalDeferred(rp.proposals.value[0] as any)).toBe(true)
      expect(rp.visibleProposals.value.map((p: any) => p.id)).toEqual(['live'])

      // Advance the clock past deferredUntil → it resurfaces in-session.
      rp.nowMs.value = base + 61 * 60_000
      expect(rp.isProposalDeferred(rp.proposals.value[0] as any)).toBe(false)
      expect(rp.visibleProposals.value.map((p: any) => p.id)).toEqual(['snoozed', 'live'])
    })

    it('never treats a decided proposal with a stale deferredUntil as deferred', () => {
      const rp = useReviewProposals()
      const base = new Date('2026-06-13T12:00:00.000Z').getTime()
      rp.nowMs.value = base
      rp.showCompleted.value = true
      const deferredUntil = new Date(base + 60 * 60_000).toISOString()
      // An Approved proposal that somehow retained a future deferredUntil must
      // still be visible — the status gate keeps decided proposals shown.
      rp.proposals.value = [
        { ...makeProposal({ id: 'approved-stale', status: 'Approved' }), deferredUntil },
      ] as any

      expect(rp.isProposalDeferred(rp.proposals.value[0] as any)).toBe(false)
      expect(rp.visibleProposals.value.map((p: any) => p.id)).toEqual(['approved-stale'])
    })
  })

  describe('isProposalExpired', () => {
    it('returns true for Expired status', () => {
      const rp = useReviewProposals()
      const expired = makeProposal({ status: 'Expired' })
      expect(rp.isProposalExpired(expired as any)).toBe(true)
    })

    it('returns true when PendingReview proposal is past expiresAt', () => {
      const rp = useReviewProposals()
      rp.nowMs.value = new Date('2026-06-01').getTime()
      const p = makeProposal({ status: 'PendingReview', expiresAt: '2026-05-01T00:00:00Z' })
      expect(rp.isProposalExpired(p as any)).toBe(true)
    })

    it('returns false for PendingReview with future expiresAt', () => {
      const rp = useReviewProposals()
      rp.nowMs.value = new Date('2026-01-01').getTime()
      const p = makeProposal({ status: 'PendingReview', expiresAt: '2099-01-01T00:00:00Z' })
      expect(rp.isProposalExpired(p as any)).toBe(false)
    })

    it('returns false for Applied status', () => {
      const rp = useReviewProposals()
      const p = makeProposal({ status: 'Applied' })
      expect(rp.isProposalExpired(p as any)).toBe(false)
    })
  })

  // These pin the shared actionability helpers to HARD-CODED expected verdicts
  // across every status × expiry case (NOT a re-derived mirror of the same
  // rule). A meaningful regression net for the #1124 drift class (ADR-0038): if
  // a shared rule flips, the literal expectations below disagree and the test
  // fails. The local* helpers stay imported only for the concrete-verdict test.
  describe('shared actionability helpers parity', () => {
    const NOW = new Date('2026-06-04T00:00:00Z').getTime()
    const PAST = '2026-05-01T00:00:00Z' // before NOW -> expired when status permits
    const FUTURE = '2099-01-01T00:00:00Z' // after NOW -> not yet expired

    // Status × expiry matrix with the EXPECTED truth table baked in by hand, so
    // the assertions can catch a rule change instead of self-confirming.
    // Pending, Approved, Approved+expired (#1124), Applied, Rejected, Expired.
    const cases: Array<{
      label: string
      status: string
      expiresAt: string
      expectedExpired: boolean
      expectedApply: boolean
      expectedReject: boolean
    }> = [
      // Pending live: open for both apply and reject.
      { label: 'Pending (live)', status: 'PendingReview', expiresAt: FUTURE, expectedExpired: false, expectedApply: true, expectedReject: true },
      // Pending past expiresAt: clock-expired, so neither action is offered.
      { label: 'Pending (expired by clock)', status: 'PendingReview', expiresAt: PAST, expectedExpired: true, expectedApply: false, expectedReject: false },
      // Approved live: can still be applied/executed, but reject is gone.
      { label: 'Approved (live)', status: 'Approved', expiresAt: FUTURE, expectedExpired: false, expectedApply: true, expectedReject: false },
      // Approved + expired (#1124): can no longer be applied, and not rejectable.
      { label: 'Approved + expired (#1124)', status: 'Approved', expiresAt: PAST, expectedExpired: true, expectedApply: false, expectedReject: false },
      // Terminal states: never actionable, never (clock-)expired here.
      { label: 'Applied', status: 'Applied', expiresAt: PAST, expectedExpired: false, expectedApply: false, expectedReject: false },
      { label: 'Rejected', status: 'Rejected', expiresAt: PAST, expectedExpired: false, expectedApply: false, expectedReject: false },
      // Expired status is expired by definition; nothing actionable.
      { label: 'Expired', status: 'Expired', expiresAt: PAST, expectedExpired: true, expectedApply: false, expectedReject: false },
    ]

    it.each(cases)(
      'isApplyActionable returns the hard-coded verdict for $label',
      ({ status, expiresAt, expectedExpired, expectedApply }) => {
        const rp = useReviewProposals()
        rp.nowMs.value = NOW
        const p = makeProposal({ status, expiresAt }) as any
        const expired = rp.isProposalExpired(p)
        // Pin the expiry derivation too, so a broken clock rule is also caught.
        expect(expired).toBe(expectedExpired)
        expect(rp.isApplyActionable(p)).toBe(expectedApply)
        expect(isProposalApplyActionable(p, expired)).toBe(expectedApply)
      },
    )

    it.each(cases)(
      'isRejectActionable returns the hard-coded verdict for $label',
      ({ status, expiresAt, expectedExpired, expectedReject }) => {
        const rp = useReviewProposals()
        rp.nowMs.value = NOW
        const p = makeProposal({ status, expiresAt }) as any
        const expired = rp.isProposalExpired(p)
        expect(expired).toBe(expectedExpired)
        expect(rp.isRejectActionable(p)).toBe(expectedReject)
        expect(isProposalRejectActionable(p, expired)).toBe(expectedReject)
      },
    )

    it.each(cases)(
      'isProposalApproveActionable returns the hard-coded verdict for $label (shares Reject precondition)',
      ({ status, expiresAt, expectedExpired, expectedReject }) => {
        const rp = useReviewProposals()
        rp.nowMs.value = NOW
        const p = makeProposal({ status, expiresAt }) as any
        const expired = rp.isProposalExpired(p)
        expect(expired).toBe(expectedExpired)
        // Approve currently mirrors Reject (live, unexpired PendingReview) but is
        // asserted against the hard-coded table independently so a future divergence
        // in either helper is caught.
        expect(isProposalApproveActionable(p, expired)).toBe(expectedReject)
      },
    )

    // Concrete verdicts (not just self-parity) so a logic flip is caught even
    // if the local mirror is wrong too.
    it('apply is actionable only for live Pending/Approved; reject only for live Pending', () => {
      const rp = useReviewProposals()
      rp.nowMs.value = NOW

      const pendingLive = makeProposal({ status: 'PendingReview', expiresAt: FUTURE }) as any
      const approvedLive = makeProposal({ status: 'Approved', expiresAt: FUTURE }) as any
      const approvedExpired = makeProposal({ status: 'Approved', expiresAt: PAST }) as any
      const applied = makeProposal({ status: 'Applied' }) as any
      const rejected = makeProposal({ status: 'Rejected' }) as any
      const expired = makeProposal({ status: 'Expired', expiresAt: PAST }) as any

      expect(rp.isApplyActionable(pendingLive)).toBe(true)
      expect(rp.isApplyActionable(approvedLive)).toBe(true)
      expect(rp.isApplyActionable(approvedExpired)).toBe(false) // #1124: can't apply
      expect(rp.isApplyActionable(applied)).toBe(false)
      expect(rp.isApplyActionable(rejected)).toBe(false)
      expect(rp.isApplyActionable(expired)).toBe(false)

      expect(rp.isRejectActionable(pendingLive)).toBe(true)
      expect(rp.isRejectActionable(approvedLive)).toBe(false)
      expect(rp.isRejectActionable(approvedExpired)).toBe(false)
      expect(rp.isRejectActionable(applied)).toBe(false)
      expect(rp.isRejectActionable(rejected)).toBe(false)
      expect(rp.isRejectActionable(expired)).toBe(false)
    })

    it('isStaleProposal flags only Pending proposals at/after the 24h cutoff', () => {
      const rp = useReviewProposals()
      rp.nowMs.value = NOW
      const justUnder = new Date(NOW - (STALE_PROPOSAL_MS - 1000)).toISOString()
      const exactly = new Date(NOW - STALE_PROPOSAL_MS).toISOString()
      const wellOver = new Date(NOW - (STALE_PROPOSAL_MS + 60_000)).toISOString()

      const freshPending = makeProposal({ status: 'PendingReview', createdAt: justUnder }) as any
      const borderlinePending = makeProposal({ status: 'PendingReview', createdAt: exactly }) as any
      const oldPending = makeProposal({ status: 'PendingReview', createdAt: wellOver }) as any
      const oldApproved = makeProposal({ status: 'Approved', createdAt: wellOver }) as any

      // Reactive instance wrapper
      expect(rp.isStaleProposal(freshPending)).toBe(false)
      expect(rp.isStaleProposal(borderlinePending)).toBe(true) // >= cutoff is inclusive
      expect(rp.isStaleProposal(oldPending)).toBe(true)
      expect(rp.isStaleProposal(oldApproved)).toBe(false) // only Pending is stale

      // Parity with the prior local logic + the pure helper
      for (const p of [freshPending, borderlinePending, oldPending, oldApproved]) {
        const expected = localIsStaleProposal(p.status, new Date(p.createdAt).getTime(), NOW)
        expect(rp.isStaleProposal(p)).toBe(expected)
        expect(isProposalStale(p, NOW)).toBe(expected)
      }
    })

    it('isProposalStale is defensive against null proposal and bad createdAt', () => {
      // Malformed/partial data must not throw and must not mis-flag staleness.
      // Build raw objects so makeProposal's `?? default` cannot resupply a valid
      // createdAt and mask the guard.
      const base = makeProposal({ status: 'PendingReview' })
      expect(isProposalStale(null as any, NOW)).toBe(false)
      expect(isProposalStale({ ...base, createdAt: undefined } as any, NOW)).toBe(false)
      expect(isProposalStale({ ...base, createdAt: 'not-a-date' } as any, NOW)).toBe(false)
      // new Date(null) -> epoch would otherwise read as wildly stale; rejected.
      expect(isProposalStale({ ...base, createdAt: null } as any, NOW)).toBe(false)
    })
  })

  describe('summaryCards', () => {
    it('counts pending, ready, capture-linked, and applied', () => {
      const rp = useReviewProposals()
      rp.proposals.value = [
        makeProposal({ id: '1', status: 'PendingReview' }),
        makeProposal({ id: '2', status: 'Approved' }),
        makeProposal({ id: '3', status: 'Applied' }),
        makeProposal({ id: '4', status: 'PendingReview', sourceType: 'Queue', sourceReferenceId: 'cap-1' }),
      ] as any
      rp.showCompleted.value = true

      const cards = rp.summaryCards.value
      expect(cards.find((c: any) => c.id === 'pending-review')?.value).toBe(2)
      expect(cards.find((c: any) => c.id === 'ready-to-execute')?.value).toBe(1)
      expect(cards.find((c: any) => c.id === 'capture-linked')?.value).toBe(1)
      expect(cards.find((c: any) => c.id === 'applied')?.value).toBe(1)
    })
  })

  describe('dismissableProposalIds', () => {
    it('returns ids of Applied, Rejected, Failed, Expired proposals', () => {
      const rp = useReviewProposals()
      rp.proposals.value = [
        makeProposal({ id: '1', status: 'Applied' }),
        makeProposal({ id: '2', status: 'PendingReview' }),
        makeProposal({ id: '3', status: 'Failed' }),
        makeProposal({ id: '4', status: 'Expired' }),
        makeProposal({ id: '5', status: 'Rejected' }),
      ] as any
      expect(rp.dismissableProposalIds.value).toEqual(['1', '3', '4', '5'])
    })

    it('includes an Approved proposal that has expired but not a live one (#1124)', () => {
      const rp = useReviewProposals()
      rp.nowMs.value = new Date('2026-06-04T00:00:00Z').getTime()
      rp.proposals.value = [
        // Approved + already past expiresAt -> can no longer be applied, so dismissable
        // (mirrors backend AutomationProposal.CanBeDismissed).
        makeProposal({ id: 'a-expired', status: 'Approved', expiresAt: '2026-05-01T00:00:00Z' }),
        // Approved + still valid -> NOT dismissable, it can still be executed.
        makeProposal({ id: 'a-live', status: 'Approved', expiresAt: '2099-01-01T00:00:00Z' }),
        // Pending + live -> NOT dismissable.
        makeProposal({ id: 'p-live', status: 'PendingReview', expiresAt: '2099-01-01T00:00:00Z' }),
      ] as any
      expect(rp.dismissableProposalIds.value).toEqual(['a-expired'])
    })
  })

  describe('loadProposals', () => {
    it('calls automationApi.getProposals with board filter', async () => {
      mockRoute.query = { boardId: 'brd-1' }
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(mockAutomationApi.getProposals).toHaveBeenCalledWith(
        expect.objectContaining({ boardId: 'brd-1', limit: 200 }),
      )
    })

    it('sets proposals from response', async () => {
      const fakeProposals = [makeProposal({ id: 'fetched' })]
      mockAutomationApi.getProposals.mockResolvedValueOnce(fakeProposals)
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(rp.proposals.value).toEqual(fakeProposals)
    })

    it('shows toast on error', async () => {
      mockAutomationApi.getProposals.mockRejectedValueOnce(new Error('network'))
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(mockToast.error).toHaveBeenCalled()
    })

    it('sets proposalsLoading false after completion', async () => {
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(rp.proposalsLoading.value).toBe(false)
    })
  })

  describe('loadBoardOptions', () => {
    it('fetches boards from API', async () => {
      const boards = [{ id: 'b1', name: 'Board 1' }]
      mockBoardsApi.getBoards.mockResolvedValueOnce(boards)
      const rp = useReviewProposals()
      await rp.loadBoardOptions()
      expect(rp.availableBoards.value).toEqual(boards)
      expect(rp.loadingBoards.value).toBe(false)
    })

    it('swallows errors and resets loadingBoards', async () => {
      mockBoardsApi.getBoards.mockRejectedValueOnce(new Error('fail'))
      const rp = useReviewProposals()
      await expect(rp.loadBoardOptions()).resolves.toBeUndefined()
      expect(mockToast.error).not.toHaveBeenCalled()
      expect(rp.loadingBoards.value).toBe(false)
    })
  })

  describe('openProposalFromHash', () => {
    it('scrolls to existing proposal that matches board filter', async () => {
      mockRoute.hash = '#proposal-p-exist'
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-exist' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(mockRouter.replace).not.toHaveBeenCalled()
      expect(mockAutomationApi.getProposal).not.toHaveBeenCalled()
    })

    it('clears hash when existing proposal does not match board filter', async () => {
      mockRoute.query = { boardId: 'board-A' }
      mockRoute.hash = '#proposal-p-other'
      const mismatchedProposal = makeProposal({ id: 'p-other', boardId: 'board-B' })
      mockAutomationApi.getProposals.mockResolvedValueOnce([mismatchedProposal])
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(mockRouter.replace).toHaveBeenCalledWith(
        expect.objectContaining({ name: 'workspace-review' }),
      )
    })

    it('fetches unknown proposal from API and upserts it', async () => {
      mockRoute.hash = '#proposal-p-remote'
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockResolvedValueOnce(makeProposal({ id: 'p-remote' }))
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(mockAutomationApi.getProposal).toHaveBeenCalledWith('p-remote')
      expect(rp.proposals.value.find((p: any) => p.id === 'p-remote')).toBeDefined()
    })

    it('clears hash on 404 from API', async () => {
      mockRoute.hash = '#proposal-p-missing'
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status: 404 } })
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(mockRouter.replace).toHaveBeenCalledWith(
        expect.objectContaining({ name: 'workspace-review' }),
      )
    })

    it('shows toast on non-404 error', async () => {
      mockRoute.hash = '#proposal-p-fail'
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockRejectedValueOnce(new Error('server error'))
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(mockToast.error).toHaveBeenCalled()
    })
  })

  describe('navigation helpers', () => {
    it('openInbox pushes inbox path with board filter', () => {
      mockRoute.query = { boardId: 'board-x' }
      const rp = useReviewProposals()
      rp.openInbox()
      expect(mockRouter.push).toHaveBeenCalledWith(
        '/workspace/inbox?boardId=board-x',
      )
    })

    it('openBoard pushes to board route', () => {
      const rp = useReviewProposals()
      rp.openBoard('my-board')
      expect(mockRouter.push).toHaveBeenCalledWith('/workspace/boards/my-board')
    })

    it('openRoute pushes arbitrary path', () => {
      const rp = useReviewProposals()
      rp.openRoute('/settings')
      expect(mockRouter.push).toHaveBeenCalledWith('/settings')
    })

    it('applyBoardFilter navigates with boardId query', () => {
      const rp = useReviewProposals()
      rp.applyBoardFilter('board-z')
      expect(mockRouter.push).toHaveBeenCalledWith({
        name: 'workspace-review',
        query: { boardId: 'board-z' },
      })
    })

    it('applyBoardFilter without boardId navigates without query', () => {
      const rp = useReviewProposals()
      rp.applyBoardFilter('')
      expect(mockRouter.push).toHaveBeenCalledWith({ name: 'workspace-review' })
    })

    it('clearBoardFilter navigates to review without query', () => {
      const rp = useReviewProposals()
      rp.clearBoardFilter()
      expect(mockRouter.push).toHaveBeenCalledWith({ name: 'workspace-review' })
      expect(rp.boardFilterInput.value).toBe('')
    })
  })

  describe('proposalHref', () => {
    it('builds href with boardId from proposal', () => {
      const rp = useReviewProposals()
      const p = makeProposal({ id: 'abc', boardId: 'brd-1' })
      const href = rp.proposalHref(p as any)
      expect(href).toBe('/workspace/review?boardId=brd-1#proposal-abc')
    })

    it('builds href without boardId', () => {
      mockRoute.query = {}
      const rp = useReviewProposals()
      const p = makeProposal({ id: 'xyz', boardId: null })
      const href = rp.proposalHref(p as any)
      expect(href).toBe('/workspace/review#proposal-xyz')
    })
  })

  describe('captureHrefForProposal', () => {
    it('links to inbox with capture source reference', () => {
      const rp = useReviewProposals()
      const p = makeProposal({ sourceType: 'Queue', sourceReferenceId: 'cap-id', boardId: 'brd-1' })
      const href = rp.captureHrefForProposal(p as any)
      expect(href).toContain('capture-cap-id')
      expect(href).toContain('boardId=brd-1')
    })

    it('links to inbox without hash when no source reference', () => {
      const rp = useReviewProposals()
      const p = makeProposal({ sourceType: 'Manual', sourceReferenceId: null })
      const href = rp.captureHrefForProposal(p as any)
      expect(href).not.toContain('#capture-')
    })
  })

  describe('clock', () => {
    it('startClock sets interval and stopClock clears it', () => {
      vi.useFakeTimers()
      const rp = useReviewProposals()
      rp.startClock()
      const initialNow = rp.nowMs.value
      vi.advanceTimersByTime(60_000)
      expect(rp.nowMs.value).toBeGreaterThan(initialNow)
      rp.stopClock()
      const afterStop = rp.nowMs.value
      vi.advanceTimersByTime(60_000)
      expect(rp.nowMs.value).toBe(afterStop)
    })

    it('registers stopClock via onScopeDispose', () => {
      vi.useFakeTimers()
      mockOnScopeDispose.mockClear()
      const rp = useReviewProposals()
      expect(mockOnScopeDispose).toHaveBeenCalledWith(expect.any(Function))

      rp.startClock()
      const registered = mockOnScopeDispose.mock.calls[0][0] as () => void
      registered()
      const afterDispose = rp.nowMs.value
      vi.advanceTimersByTime(60_000)
      expect(rp.nowMs.value).toBe(afterDispose)
    })

    it('startClock is idempotent so a double call cannot leak an interval', () => {
      vi.useFakeTimers()
      const setIntervalSpy = vi.spyOn(globalThis, 'setInterval')
      const rp = useReviewProposals()

      rp.startClock()
      rp.startClock() // second call must be a no-op (guarded)
      expect(setIntervalSpy).toHaveBeenCalledTimes(1)

      // A single stopClock fully halts the clock — proving no orphaned interval.
      rp.stopClock()
      const afterStop = rp.nowMs.value
      vi.advanceTimersByTime(120_000)
      expect(rp.nowMs.value).toBe(afterStop)

      setIntervalSpy.mockRestore()
    })
  })

  describe('matchesActiveBoardFilter', () => {
    it('returns true when no filter is active', () => {
      mockRoute.query = {}
      const rp = useReviewProposals()
      expect(rp.matchesActiveBoardFilter('any-board')).toBe(true)
    })

    it('matches case-insensitively', () => {
      mockRoute.query = { boardId: 'Board-A' }
      const rp = useReviewProposals()
      expect(rp.matchesActiveBoardFilter('board-a')).toBe(true)
    })

    it('returns false for non-matching board', () => {
      mockRoute.query = { boardId: 'board-A' }
      const rp = useReviewProposals()
      expect(rp.matchesActiveBoardFilter('board-B')).toBe(false)
    })
  })

  describe('watchers', () => {
    it('route hash watcher triggers openProposalFromHash', async () => {
      mockRoute.hash = '#proposal-p-watch'
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      const rp = useReviewProposals()
      await rp.loadProposals()
      mockAutomationApi.getProposal.mockClear()
      mockRoute.hash = '#proposal-p-new'
      mockAutomationApi.getProposal.mockResolvedValueOnce(makeProposal({ id: 'p-new' }))
      await watcherForCurrentSourceValue('#proposal-p-new')[1]()
      expect(mockAutomationApi.getProposal).toHaveBeenCalledWith('p-new')
    })

    it('activeBoardFilter watcher triggers loadProposals', async () => {
      useReviewProposals()
      mockAutomationApi.getProposals.mockClear()
      mockRoute.query = { boardId: 'board-filter-probe' }
      await watcherForCurrentSourceValue('board-filter-probe')[1]()
      expect(mockAutomationApi.getProposals).toHaveBeenCalled()
    })
  })
})
