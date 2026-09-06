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
  REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD,
  REVIEW_QUEUE_REQUEST_DEADLINE_MS,
  REVIEW_QUEUE_REFRESH_MS,
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
  const watcher = watchers.find(([source]) => {
    if (typeof source !== 'function') return false
    const current = (source as () => unknown)()
    return Array.isArray(current) ? current[0] === expected : current === expected
  })
  expect(watcher).toBeDefined()
  return watcher!
}

function makeProposal(overrides: Partial<{
  id: string; status: string; sourceType: string; sourceReferenceId: string | null;
  boardId: string | null; createdAt: string; expiresAt: string; isExpired: boolean;
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
    isExpired: overrides.isExpired,
    decidedAt: null,
    decidedByUserId: null,
    // One operation by default: approve-actionability additionally requires a
    // structurally applyable (non-zero-op) proposal (#1397); the status/expiry
    // truth tables below assert against a realistic applyable fixture, and the
    // zero-op arm is asserted explicitly in its own test.
    operations: [
      {
        id: 'op-1',
        proposalId: overrides.id ?? 'p-1',
        sequence: 0,
        actionType: 'CreateCard',
        targetType: 'Card',
        targetId: null,
        parameters: '{}',
        idempotencyKey: 'k-1',
        expectedVersion: null,
      },
    ],
  }
}

describe('useReviewProposals', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    // `clearAllMocks` retains queued `mockResolvedValueOnce` entries. A red
    // regression that proves a request was never issued must not leak that
    // unused answer into the next concurrency case.
    mockAutomationApi.getProposals.mockReset().mockResolvedValue([])
    mockAutomationApi.getProposal.mockReset()
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

    it('exposes board-scoped settled records in archived history while keeping the mode mutation-free', async () => {
      mockRoute.query = { boardId: 'board-A', history: 'archived', source: 'archive' }
      const rp = useReviewProposals()
      rp.showCompleted.value = false
      rp.proposals.value = [
        makeProposal({ id: 'pending', boardId: 'board-A', status: 'PendingReview' }),
        makeProposal({ id: 'approved', boardId: 'board-A', status: 'Approved' }),
        makeProposal({ id: 'applied', boardId: 'board-A', status: 'Applied', sourceType: 'Queue', sourceReferenceId: 'capture-1' }),
        makeProposal({ id: 'rejected', boardId: 'board-A', status: 'Rejected' }),
        makeProposal({ id: 'failed', boardId: 'board-A', status: 'Failed' }),
        makeProposal({ id: 'expired', boardId: 'board-A', status: 'Expired' }),
        makeProposal({ id: 'dismissed', boardId: 'board-A', status: 'Dismissed' }),
        makeProposal({ id: 'other-board', boardId: 'board-B', status: 'Applied' }),
      ] as any

      expect(rp.isArchivedHistory.value).toBe(true)
      expect(rp.visibleProposals.value.map((proposal: any) => proposal.id)).toEqual([
        'approved',
        'applied',
        'rejected',
        'failed',
        'expired',
        'dismissed',
      ])
      expect(rp.dismissableProposalIds.value).toEqual([])
      expect(rp.captureHrefForProposal(rp.proposals.value[2] as any)).toBe(
        '/workspace/inbox?boardId=board-A&history=archived#capture-capture-1',
      )
      expect(rp.proposalHref(rp.proposals.value[2] as any)).toBe(
        '/workspace/review?boardId=board-A&history=archived#proposal-applied',
      )

      rp.openInbox()
      expect(mockRouter.push).toHaveBeenCalledWith('/workspace/inbox?boardId=board-A&history=archived')

      await rp.clearBoardFilter()
      expect(mockRouter.replace).toHaveBeenCalledWith({
        name: 'workspace-review',
        query: { source: 'archive' },
        hash: '',
      })
    })

    // Regression for the archived-history escape hatch (#1973). Clearing scope
    // used to carry `route.hash` through untouched, which handed an archived
    // board's proposal to the UNSCOPED, mutation-enabled queue: the hash watcher
    // refetches it by id, no board filter is left to reject it, and Apply/Reject
    // reappear against an archived board. The exit must drop the deep link.
    it('drops a retained proposal deep link when leaving archived history', async () => {
      mockRoute.query = { boardId: 'board-A', history: 'archived' }
      mockRoute.hash = '#proposal-archived-approved'
      const rp = useReviewProposals()
      expect(rp.isArchivedHistory.value).toBe(true)

      await rp.clearBoardFilter()

      expect(mockRouter.replace).toHaveBeenCalledWith({
        name: 'workspace-review',
        query: {},
        hash: '',
      })
    })

    // The counterpart: an ORDINARY board clear is not a trust boundary, so its
    // deep link still survives. Without this arm the fix above could be
    // over-applied to every clear and silently break live deep links.
    it('keeps a proposal deep link when clearing an ordinary board filter', async () => {
      mockRoute.query = { boardId: 'board-A' }
      mockRoute.hash = '#proposal-live-1'
      const rp = useReviewProposals()
      expect(rp.isArchivedHistory.value).toBe(false)

      await rp.clearBoardFilter()

      expect(mockRouter.replace).toHaveBeenCalledWith({
        name: 'workspace-review',
        query: {},
        hash: '#proposal-live-1',
      })
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

    it('keeps a deep-linked (hash-targeted) snoozed proposal visible while still hiding other snoozed ones', () => {
      const rp = useReviewProposals()
      const base = new Date('2026-06-13T12:00:00.000Z').getTime()
      rp.nowMs.value = base
      const deferredUntil = new Date(base + 60 * 60_000).toISOString() // 1h ahead
      rp.proposals.value = [
        { ...makeProposal({ id: 'snoozed', status: 'PendingReview' }), deferredUntil },
        { ...makeProposal({ id: 'other-snoozed', status: 'PendingReview' }), deferredUntil },
        makeProposal({ id: 'live', status: 'PendingReview' }),
      ] as any

      // No hash → both snoozed proposals are hidden.
      mockRoute.hash = ''
      expect(rp.visibleProposals.value.map((p: any) => p.id)).toEqual(['live'])

      // Deep link to the snoozed proposal → it renders; the OTHER snoozed proposal stays hidden.
      mockRoute.hash = '#proposal-SNOOZED'
      expect(rp.visibleProposals.value.map((p: any) => p.id)).toEqual(['snoozed', 'live'])
    })

    it('clearProposalDeepLink drops the hash for the matching proposal so a just-snoozed deep link leaves the queue', async () => {
      const rp = useReviewProposals()
      const base = new Date('2026-06-13T12:00:00.000Z').getTime()
      rp.nowMs.value = base
      const deferredUntil = new Date(base + 60 * 60_000).toISOString()
      rp.proposals.value = [
        { ...makeProposal({ id: 'snoozed', status: 'PendingReview' }), deferredUntil },
        makeProposal({ id: 'live', status: 'PendingReview' }),
      ] as any

      // Deep-linked + snoozed → the carve-out keeps it visible.
      mockRoute.hash = '#proposal-snoozed'
      expect(rp.visibleProposals.value.map((p: any) => p.id)).toEqual(['snoozed', 'live'])

      // Snoozing it clears the hash (replace to the hashless review route)...
      await rp.clearProposalDeepLink('snoozed')
      expect(mockRouter.replace).toHaveBeenCalledTimes(1)
      expect(mockRouter.replace).toHaveBeenCalledWith({ name: 'workspace-review', query: {} })

      // ...and once the hash is gone the deferred filter hides it again.
      mockRoute.hash = ''
      expect(rp.visibleProposals.value.map((p: any) => p.id)).toEqual(['live'])
    })

    it('clearProposalDeepLink is a no-op when the hash points at a different proposal', async () => {
      const rp = useReviewProposals()
      mockRoute.hash = '#proposal-snoozed'

      await rp.clearProposalDeepLink('some-other-id')
      expect(mockRouter.replace).not.toHaveBeenCalled()
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

    it('honors the server isExpired flag for a PendingReview proposal whose client clock has not yet elapsed (#1414 P2 #1)', () => {
      // Clock lag/skew: the server has already expired the proposal (isExpired:
      // true) but the client's 60s clock still reads a moment before expiresAt.
      // Without honoring the flag the read-only guards would fire a live /diff
      // that 400s instead of presenting the stored preview.
      const rp = useReviewProposals()
      rp.nowMs.value = new Date('2026-06-01T00:00:00Z').getTime()
      const p = makeProposal({
        status: 'PendingReview',
        expiresAt: '2026-06-01T00:00:30Z', // 30s in the "future" per the lagging client clock
        isExpired: true,
      })
      expect(rp.isProposalExpired(p as any)).toBe(true)
    })

    it('honors the server isExpired flag for an Approved proposal whose client clock has not yet elapsed (#1414 P2 #1)', () => {
      const rp = useReviewProposals()
      rp.nowMs.value = new Date('2026-06-01T00:00:00Z').getTime()
      const p = makeProposal({ status: 'Approved', expiresAt: '2099-01-01T00:00:00Z', isExpired: true })
      expect(rp.isProposalExpired(p as any)).toBe(true)
    })

    it('does NOT flip a terminal Applied proposal to expired even when the server isExpired flag is true (#1414 P2 #1 boundary)', () => {
      // The backend isExpired flag is time-based and status-agnostic (true for
      // any past-expiry proposal). Consulting it must stay scoped to
      // PendingReview/Approved — a terminal proposal whose expiry later passed
      // must keep its terminal classification, or visibleProposals would
      // force-show completed items and the status labels would mislabel it.
      const rp = useReviewProposals()
      rp.nowMs.value = new Date('2026-06-04T00:00:00Z').getTime()
      const applied = makeProposal({ status: 'Applied', expiresAt: '2026-05-01T00:00:00Z', isExpired: true })
      const rejected = makeProposal({ status: 'Rejected', expiresAt: '2026-05-01T00:00:00Z', isExpired: true })
      expect(rp.isProposalExpired(applied as any)).toBe(false)
      expect(rp.isProposalExpired(rejected as any)).toBe(false)
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

    // #1397 LOW-3: Approve additionally requires a structurally applyable
    // proposal — Apply (and /diff) reject a zero-op proposal with 400, so the
    // rail must not offer Approve for it. A saved revision is the #1235 escape:
    // it carries operations the backend applies revision-aware.
    it('isProposalApproveActionable rejects a zero-op pending proposal unless a saved revision exists', () => {
      const zeroOp = { ...makeProposal({ status: 'PendingReview' }), operations: [] } as any
      expect(isProposalApproveActionable(zeroOp, false)).toBe(false)
      expect(isProposalApproveActionable(zeroOp, false, { hasSavedRevision: true })).toBe(true)
      // Reject/apply gating is NOT structural — the reviewer can still clear it.
      expect(isProposalRejectActionable(zeroOp, false)).toBe(true)

      const missingOps = { ...makeProposal({ status: 'PendingReview' }) } as any
      delete missingOps.operations
      expect(isProposalApproveActionable(missingOps, false)).toBe(false)
    })

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
    it('reports when the current explicit load lands authoritatively', async () => {
      const fakeProposals = [makeProposal({ id: 'fetched' })]
      mockAutomationApi.getProposals.mockResolvedValueOnce(fakeProposals)
      const rp = useReviewProposals()

      await expect(rp.loadProposalsWithOutcome()).resolves.toBe('landed')
      expect(rp.proposals.value).toEqual(fakeProposals)
      expect(rp.proposalsLoading.value).toBe(false)
    })

    it('reports a current explicit load failure without publishing stale truth', async () => {
      mockAutomationApi.getProposals.mockRejectedValueOnce(new Error('network'))
      const rp = useReviewProposals()

      await expect(rp.loadProposalsWithOutcome()).resolves.toBe('failed')
      expect(mockToast.error).toHaveBeenCalledOnce()
      expect(rp.proposalsLoading.value).toBe(false)
    })

    it('reports an older overlapping explicit load as superseded', async () => {
      let resolveOlder!: (proposals: ReturnType<typeof makeProposal>[]) => void
      const olderResponse = new Promise<ReturnType<typeof makeProposal>[]>((resolve) => {
        resolveOlder = resolve
      })
      mockAutomationApi.getProposals
        .mockReturnValueOnce(olderResponse)
        .mockResolvedValueOnce([makeProposal({ id: 'newer' })])
      const rp = useReviewProposals()

      const older = rp.loadProposalsWithOutcome()
      const newer = rp.loadProposalsWithOutcome()
      await expect(newer).resolves.toBe('landed')
      resolveOlder([makeProposal({ id: 'older' })])

      await expect(older).resolves.toBe('superseded')
      expect(rp.proposals.value.map((proposal: any) => proposal.id)).toEqual(['newer'])
      expect(rp.proposalsLoading.value).toBe(false)
    })

    // #2460 -- a caller that owns a deadline needs its own cancellation told
    // apart from a server failure, or a timeout would be blamed on the backend.
    it('reports a caller-aborted explicit load as aborted rather than failed', async () => {
      const controller = new AbortController()
      let rejectRead!: (error: Error) => void
      mockAutomationApi.getProposals.mockReturnValueOnce(
        new Promise((_resolve, reject) => {
          rejectRead = reject
        }),
      )
      const rp = useReviewProposals()

      const load = rp.loadProposalsWithOutcome({ signal: controller.signal })
      controller.abort()
      rejectRead(new Error('canceled'))

      await expect(load).resolves.toBe('aborted')
      expect(mockToast.error).not.toHaveBeenCalled()
      expect(mockAutomationApi.getProposals).toHaveBeenCalledWith(
        expect.objectContaining({ limit: 200 }),
        expect.objectContaining({ signal: controller.signal }),
      )
    })

    // A deadline-bounded caller must not spend its budget in the shared retry
    // interceptor's doubling backoff and then be reported as a timeout.
    it('forwards a caller opt-out of the shared retry interceptor', async () => {
      const controller = new AbortController()
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      const rp = useReviewProposals()

      await expect(
        rp.loadProposalsWithOutcome({ signal: controller.signal, skipRetry: true }),
      ).resolves.toBe('landed')
      expect(mockAutomationApi.getProposals).toHaveBeenCalledWith(
        expect.objectContaining({ limit: 200 }),
        expect.objectContaining({ signal: controller.signal, skipRetry: true }),
      )
    })

    it('does not issue an explicit load whose caller has already given up', async () => {
      const controller = new AbortController()
      controller.abort()
      const rp = useReviewProposals()

      await expect(
        rp.loadProposalsWithOutcome({ signal: controller.signal }),
      ).resolves.toBe('aborted')
      expect(mockAutomationApi.getProposals).not.toHaveBeenCalled()
      expect(mockToast.error).not.toHaveBeenCalled()
    })

    it('reports an aborted deep-link leg as aborted and raises no lookup error', async () => {
      mockRoute.hash = '#proposal-p-remote'
      const controller = new AbortController()
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      let rejectLookup!: (error: Error) => void
      mockAutomationApi.getProposal.mockReturnValueOnce(
        new Promise((_resolve, reject) => {
          rejectLookup = reject
        }),
      )
      const rp = useReviewProposals()

      const load = rp.loadProposalsWithOutcome({
        signal: controller.signal,
        skipRetry: true,
      })
      await Promise.resolve()
      await Promise.resolve()
      controller.abort()
      rejectLookup(new Error('canceled'))

      await expect(load).resolves.toBe('aborted')
      expect(mockToast.error).not.toHaveBeenCalled()
      // The deep-link leg is part of the same explicit read, so it carries the
      // same cancellation and retry contract rather than running unbounded.
      expect(mockAutomationApi.getProposal).toHaveBeenCalledWith(
        'p-remote',
        expect.objectContaining({ signal: controller.signal, skipRetry: true }),
      )
      // It must NOT carry the background poll's `expectedStatuses` (#2214 item
      // 7). This read was asked for, so its failures stay loggable; only the
      // poll's pin leg turns those statuses into a handled outcome.
      expect(mockAutomationApi.getProposal.mock.calls.at(-1)?.[1]).not.toHaveProperty(
        'expectedStatuses',
      )
    })

    it('does not report landed until its deep-link lookup completes', async () => {
      mockRoute.hash = '#proposal-p-remote'
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      let resolveProposal!: (proposal: ReturnType<typeof makeProposal>) => void
      mockAutomationApi.getProposal.mockReturnValueOnce(
        new Promise<ReturnType<typeof makeProposal>>((resolve) => {
          resolveProposal = resolve
        }),
      )
      const rp = useReviewProposals()

      let settled = false
      const load = rp.loadProposalsWithOutcome().then((outcome) => {
        settled = true
        return outcome
      })
      await Promise.resolve()
      expect(settled).toBe(false)

      resolveProposal(makeProposal({ id: 'p-remote' }))
      await expect(load).resolves.toBe('landed')
      expect(rp.proposals.value.map((proposal: any) => proposal.id)).toEqual(['p-remote'])
    })

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
    it('finds an existing proposal case-insensitively without replacing its canonical id', async () => {
      mockRoute.hash = '#proposal-P-EXIST'
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-exist' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(mockRouter.replace).not.toHaveBeenCalled()
      expect(mockAutomationApi.getProposal).not.toHaveBeenCalled()
      expect(rp.proposals.value[0]?.id).toBe('p-exist')
    })

    it('retains the exact hash when an existing proposal does not match board scope', async () => {
      mockRoute.query = { boardId: 'board-A' }
      mockRoute.hash = '#proposal-p-other'
      const mismatchedProposal = makeProposal({ id: 'p-other', boardId: 'board-B' })
      mockAutomationApi.getProposals.mockResolvedValueOnce([mismatchedProposal])
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(mockRouter.replace).not.toHaveBeenCalled()
    })

    it('hydrates a mixed-case hash with the canonical API proposal id', async () => {
      mockRoute.hash = '#proposal-P-REMOTE'
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockResolvedValueOnce(makeProposal({ id: 'p-remote' }))
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(mockAutomationApi.getProposal).toHaveBeenCalledWith('P-REMOTE')
      expect(rp.proposals.value.find((p: any) => p.id === 'p-remote')).toBeDefined()
    })

    it('does not hydrate a response whose proposal id differs from the hash', async () => {
      mockRoute.hash = '#proposal-p-requested'
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockResolvedValueOnce(makeProposal({ id: 'p-different' }))
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(rp.proposals.value).toEqual([])
      expect(rp.unavailableProposalId.value).toBe('p-requested')
      expect(mockRouter.replace).not.toHaveBeenCalled()
    })

    it('retains a genuine missing hash as unavailable on 404', async () => {
      mockRoute.hash = '#proposal-p-missing'
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status: 404 } })
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(mockRouter.replace).not.toHaveBeenCalled()
      expect(rp.proposals.value).toEqual([])
      expect(rp.unavailableProposalId.value).toBe('p-missing')
    })

    it('clears an unavailable deep link when the hash changes and after a successful lookup', async () => {
      mockRoute.hash = '#proposal-p-missing'
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status: 404 } })
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(rp.unavailableProposalId.value).toBe('p-missing')

      mockRoute.hash = ''
      await watcherForCurrentSourceValue('')[1]()
      expect(rp.unavailableProposalId.value).toBeNull()

      mockRoute.hash = '#proposal-p-recovered'
      mockAutomationApi.getProposal.mockResolvedValueOnce(makeProposal({ id: 'p-recovered' }))
      await watcherForCurrentSourceValue('#proposal-p-recovered')[1]()

      expect(rp.unavailableProposalId.value).toBeNull()
      expect(rp.proposals.value.map((proposal: { id: string }) => proposal.id)).toEqual(['p-recovered'])
    })

    it('shows toast on non-404 error', async () => {
      mockRoute.hash = '#proposal-p-fail'
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockRejectedValueOnce(new Error('server error'))
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(mockToast.error).toHaveBeenCalled()
    })

    /**
     * The explicit path used to answer one fact two ways depending on which
     * leg observed it. A 400 or a 403 on the reviewer's own deep-link read
     * raised a generic "Failed to load proposal" toast and left no state, so
     * the surface showed the ordinary empty queue; the very next background
     * tick turned the same 400 or 403 into the pin-unavailable panel. The
     * toast said nothing true about which of the two happened, and it was gone
     * a few seconds later while the panel it contradicted stayed.
     *
     * The explicit path now uses exactly the three predicates the background
     * pin leg uses, and reports one outcome per status CLASS. 404 already
     * behaved this way; 400 and 403 join it. Everything else — 405, 410, 5xx,
     * no response — stays a toast, because those are not facts about the
     * target and a later tick can still resolve the pin.
     */
    describe('explicit-path outcome per status class (#2214)', () => {
      async function openHash(hash: string, failure: unknown) {
        mockRoute.hash = hash
        mockAutomationApi.getProposals.mockResolvedValueOnce([])
        mockAutomationApi.getProposal.mockRejectedValueOnce(failure)
        const rp = useReviewProposals()
        await rp.loadProposals()
        return rp
      }

      it('turns a 400 into the malformed-link state without a toast', async () => {
        const rp = await openHash('#proposal-not-a-guid', { response: { status: 400 } })
        expect(rp.unavailableProposalId.value).toBe('not-a-guid')
        expect(rp.unavailableProposalMalformed.value).toBe(true)
        expect(mockToast.error).not.toHaveBeenCalled()
      })

      it('turns a 403 into the unavailable-pin state without a toast', async () => {
        const rp = await openHash('#proposal-p-forbidden', { response: { status: 403 } })
        // The by-id 403 is authority over ONE target. The queue-level 403 and
        // its `queueAccessRevoked` teardown are a different leg and untouched.
        expect(rp.unavailableProposalId.value).toBe('p-forbidden')
        expect(rp.unavailableProposalMalformed.value).toBe(false)
        expect(rp.queueAccessRevoked.value).toBe(false)
        expect(mockToast.error).not.toHaveBeenCalled()
      })

      it('keeps the 404 outcome it already had', async () => {
        const rp = await openHash('#proposal-p-gone', { response: { status: 404 } })
        expect(rp.unavailableProposalId.value).toBe('p-gone')
        expect(rp.unavailableProposalMalformed.value).toBe(false)
        expect(mockToast.error).not.toHaveBeenCalled()
      })

      it.each([
        ['405', { response: { status: 405 } }],
        ['410', { response: { status: 410 } }],
        ['500', { response: { status: 500 } }],
        ['no response', new Error('network down')],
      ])('keeps the toast and pins nothing for %s', async (_label, failure) => {
        const rp = await openHash('#proposal-p-transient', failure)
        // Nothing here is a settled fact about the target: 405 and 410 are the
        // route misbehaving rather than the id being refused (#2658 draws the
        // same line on the pin leg), and 5xx or no response may resolve next
        // tick. Claiming the pin is unavailable would be a false negative.
        expect(rp.unavailableProposalId.value).toBeNull()
        expect(rp.unavailableProposalMalformed.value).toBe(false)
        expect(mockToast.error).toHaveBeenCalled()
      })

      // Review finding, round 2 (LOW): asserting both flags after ONE explicit
      // failure could not fail, since both runs need three. This drives the
      // threshold count, and drives it through the ROUTE-HASH watcher, which
      // reaches `openProposalFromHash` without a list read -- otherwise each
      // `loadProposals` success would reset the runs and mask a leak anyway.
      it.each([
        ['transient', 500],
        ['refusal', 404],
      ])('never feeds the background %s run from the explicit path', async (_class, status) => {
        mockRoute.hash = '#proposal-p-flaky'
        mockAutomationApi.getProposals.mockResolvedValueOnce([])
        mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status } })
        const rp = useReviewProposals()
        await rp.loadProposals()

        const openHashAgain = watcherForCurrentSourceValue('#proposal-p-flaky')[1]
        for (let attempt = 1; attempt < REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD; attempt += 1) {
          mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status } })
          await openHashAgain()
        }

        // A deep link the reviewer followed says nothing about whether the
        // QUEUE poll is healthy, so neither run may advance on it.
        expect(rp.queueRefreshStale.value).toBe(false)
        expect(rp.queueRefreshRefused.value).toBe(false)
        expect(rp.queueAccessRevoked.value).toBe(false)
      })
    })
  })

  describe('explicit list read refused with 403 (#2214, round 2)', () => {
    /**
     * Review finding, round 2. Only the POLL's outer catch handled a 403 on the
     * list read. On a cold entry to a board whose access had been revoked, the
     * explicit load 403'd into the generic failure toast, and then
     * `openProposalFromHash` 403'd on the by-id read and -- since this slice
     * made that a pin-level outcome -- rendered "no longer available to review;
     * it may have been applied, archived, or removed" about a proposal that was
     * neither applied nor archived nor removed. The board was simply not this
     * reviewer's any more. It stood until the next tick set the authority state.
     */
    it('sets the same authority state the poll would, and never marks the pin', async () => {
      mockRoute.query = { boardId: 'board-revoked' }
      mockRoute.hash = '#proposal-p-pinned'
      mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 403 } })
      const rp = useReviewProposals()
      await rp.loadProposals()

      expect(rp.queueAccessRevoked.value).toBe(true)
      expect(rp.proposals.value).toEqual([])
      // The whole board is refused, so re-authorising one row inside it can
      // only produce a second, wrong explanation of the same fact.
      expect(rp.unavailableProposalId.value).toBeNull()
      expect(rp.unavailableProposalMalformed.value).toBe(false)
      expect(mockAutomationApi.getProposal).not.toHaveBeenCalled()
    })

    it('leaves an explicit non-403 list failure on the ordinary path', async () => {
      mockRoute.hash = '#proposal-p-pinned'
      mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 500 } })
      mockAutomationApi.getProposal.mockResolvedValueOnce(makeProposal({ id: 'p-pinned' }))
      const rp = useReviewProposals()
      await rp.loadProposals()

      // A 5xx is not an authority answer: it must not tear the queue down, and
      // the hash lookup still runs.
      expect(rp.queueAccessRevoked.value).toBe(false)
      expect(mockAutomationApi.getProposal).toHaveBeenCalled()
      expect(mockToast.error).toHaveBeenCalled()
    })

    it('keeps the pin-leg 403 as the single-proposal outcome #2593 shipped', async () => {
      // A readable board with one proposal this reviewer may not open is the
      // opposite case, and it must stay the unavailable pin rather than tearing
      // down a queue the server just served.
      mockRoute.hash = '#proposal-p-forbidden'
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status: 403 } })
      const rp = useReviewProposals()
      await rp.loadProposals()

      expect(rp.queueAccessRevoked.value).toBe(false)
      expect(rp.unavailableProposalId.value).toBe('p-forbidden')
    })

    it('reports the revocation once, without the generic load-failure toast', async () => {
      // Two reports for one fact -- the shape #2694 removed on the pin leg and
      // left standing here. `recordQueueAccessRevoked` raises a DURABLE panel
      // that is the first branch of both skins' empty chains and names both the
      // fact and the remedy; the generic "Failed to load proposals" toast beside
      // it names neither and is gone seconds later, contradicted by a panel that
      // stays.
      mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 403 } })
      const rp = useReviewProposals()

      // The OUTCOME contract is untouched: every action composable that calls
      // `loadProposals` still gets its failure signal.
      await expect(rp.loadProposalsWithOutcome()).resolves.toBe('failed')

      expect(rp.queueAccessRevoked.value).toBe(true)
      expect(mockToast.error).not.toHaveBeenCalled()
    })

    it('does not let a later hash change mark a pin unavailable under the revoked panel', async () => {
      // The route-hash watcher had no `queueAccessRevoked` guard, while the
      // explicit load's own `openProposalFromHash` call site has had one since
      // #2694. So a `#proposal-` link followed while the revoked panel is up --
      // a stale rail row, a bookmark, the back button -- still asked the by-id
      // route about a target inside a board the server had refused wholesale,
      // and wrote its refusal into `unavailableProposalId` as a second,
      // narrower and wrong account of the same fact.
      mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 403 } })
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(rp.queueAccessRevoked.value).toBe(true)

      mockRoute.hash = '#proposal-p-inside-revoked'
      mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status: 403 } })
      await watcherForCurrentSourceValue('#proposal-p-inside-revoked')[1]()

      expect(mockAutomationApi.getProposal).not.toHaveBeenCalled()
      expect(rp.unavailableProposalId.value).toBeNull()
      expect(rp.unavailableProposalMalformed.value).toBe(false)
    })
  })

  // #2214 item 4. Both skins derived the queue live region's sentence from the
  // pending COUNT alone, so a poll that removed one pending proposal and added
  // another rendered a byte-identical "3 proposals awaiting review.": no DOM
  // mutation, nothing announced, the queue moved under the reviewer in silence.
  // The ordered awaiting ids are the identity the announcement is keyed on.
  describe('queue announcement identity (#2214 item 4)', () => {
    it('changes on a count-neutral replacement and not on a byte-identical queue', () => {
      const rp = useReviewProposals()
      rp.proposals.value = [
        makeProposal({ id: 'p-a', createdAt: '2026-01-02T00:00:00Z' }),
        makeProposal({ id: 'p-b', createdAt: '2026-01-01T00:00:00Z' }),
      ] as any
      expect(rp.awaitingProposalIds.value).toEqual(['p-a', 'p-b'])
      const identity = rp.queueAnnouncementKey.value

      // A poll answering with the same queue in a new array is not news.
      rp.proposals.value = [
        makeProposal({ id: 'p-a', createdAt: '2026-01-02T00:00:00Z' }),
        makeProposal({ id: 'p-b', createdAt: '2026-01-01T00:00:00Z' }),
      ] as any
      expect(rp.queueAnnouncementKey.value).toBe(identity)

      // One pending proposal decided elsewhere, one created in its place: the
      // count is unchanged, so the SENTENCE is byte-identical and only the
      // identity can carry the change.
      rp.proposals.value = [
        makeProposal({ id: 'p-a', createdAt: '2026-01-02T00:00:00Z' }),
        makeProposal({ id: 'p-c', createdAt: '2026-01-01T00:00:00Z' }),
      ] as any
      expect(rp.awaitingProposalIds.value.length).toBe(2)
      expect(rp.queueAnnouncementKey.value).not.toBe(identity)

      // Order is part of the identity: the rail renders the queue in order, so
      // a reordered queue is a queue that moved.
      const swapped = rp.queueAnnouncementKey.value
      rp.proposals.value = [
        makeProposal({ id: 'p-c', createdAt: '2026-01-02T00:00:00Z' }),
        makeProposal({ id: 'p-a', createdAt: '2026-01-01T00:00:00Z' }),
      ] as any
      expect(rp.queueAnnouncementKey.value).not.toBe(swapped)
    })

    it('tracks exactly the proposals the awaiting count is made of', () => {
      // The count and its identity must come from ONE predicate or they drift
      // (#1124 / ADR-0038): announcing on a change the number cannot show would
      // speak the same sentence for no visible reason.
      const rp = useReviewProposals()
      rp.showCompleted.value = true
      rp.proposals.value = [
        makeProposal({ id: 'p-pending', createdAt: '2026-01-03T00:00:00Z' }),
        makeProposal({ id: 'p-applied', status: 'Applied', createdAt: '2026-01-02T00:00:00Z' }),
        makeProposal({
          id: 'p-expired',
          createdAt: '2026-01-01T00:00:00Z',
          expiresAt: '2026-01-01T00:00:01Z',
        }),
      ] as any
      rp.nowMs.value = new Date('2026-02-01T00:00:00Z').getTime()

      expect(rp.awaitingProposalIds.value).toEqual(['p-pending'])
      const identity = rp.queueAnnouncementKey.value

      // A settled row changing does not move the awaiting queue.
      rp.proposals.value = [
        makeProposal({ id: 'p-pending', createdAt: '2026-01-03T00:00:00Z' }),
        makeProposal({ id: 'p-rejected', status: 'Rejected', createdAt: '2026-01-02T00:00:00Z' }),
        makeProposal({
          id: 'p-expired',
          createdAt: '2026-01-01T00:00:00Z',
          expiresAt: '2026-01-01T00:00:01Z',
        }),
      ] as any
      expect(rp.queueAnnouncementKey.value).toBe(identity)
    })
  })

  // #2599 item 1. Both skins gated the announcement on "no read is in flight",
  // so every EXPLICIT reload unmounted the sentence and remounted it: the live
  // region wrote count -> '' -> count and the restore was spoken even when the
  // queue had not moved (the header Refresh, and filing away a settled proposal
  // that leaves the pending-review set identical, both reach it). The gate
  // actually needs "a read has landed for the board scope on screen", which is
  // what this signal is: a reload of the SAME scope keeps it settled, a scope
  // change unsettles it until the new scope's read lands, and a failed read
  // never settles it at all.
  describe('queue scope load signal (#2599 item 1)', () => {
    it('settles on the first landed read and stays settled across a same-scope reload', async () => {
      mockRoute.query = { boardId: 'board-a' }
      const rp = useReviewProposals()
      // Nothing has been read yet, so the count is 0 for that reason rather
      // than because nothing awaits review.
      expect(rp.queueScopeLoaded.value).toBe(false)

      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-a', boardId: 'board-a' }),
      ])
      await rp.loadProposals()
      expect(rp.queueScopeLoaded.value).toBe(true)

      // An explicit reload raises `proposalsLoading` WITHOUT clearing
      // `proposals`, so the rendered count is still the last landed read's and
      // still true of the board on screen.
      let releaseReload!: (value: unknown[]) => void
      mockAutomationApi.getProposals.mockReturnValueOnce(
        new Promise((resolve) => {
          releaseReload = resolve as (value: unknown[]) => void
        }),
      )
      const reload = rp.loadProposals()
      expect(rp.proposalsLoading.value).toBe(true)
      expect(rp.queueScopeLoaded.value).toBe(true)

      releaseReload([makeProposal({ id: 'p-a', boardId: 'board-a' })])
      await reload
      expect(rp.proposalsLoading.value).toBe(false)
      expect(rp.queueScopeLoaded.value).toBe(true)
    })

    it('unsettles the moment the board scope changes and settles again when that scope lands', async () => {
      mockRoute.query = { boardId: 'board-a' }
      const rp = useReviewProposals()
      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-a', boardId: 'board-a' }),
      ])
      await rp.loadProposals()
      expect(rp.queueScopeLoaded.value).toBe(true)

      // The rendered queue is still board-a's, so under board-b it counts
      // nothing that is on screen -- the one case where withholding is right.
      mockRoute.query = { boardId: 'board-b' }
      expect(rp.queueScopeLoaded.value).toBe(false)
      // The composable's own scope watcher is what issues the reload below.
      expect(watcherForCurrentSourceValue('board-b')).toBeDefined()

      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-b', boardId: 'board-b' }),
      ])
      await rp.loadProposals()
      expect(rp.queueScopeLoaded.value).toBe(true)

      // Case is the query string's, not the scope's: the same board asked for
      // twice is one scope, and `matchesActiveBoardFilter` compares it the same
      // way.
      mockRoute.query = { boardId: 'BOARD-B' }
      expect(rp.queueScopeLoaded.value).toBe(true)
    })

    it('stays unsettled after a failed entry load and settles on the next successful read', async () => {
      const rp = useReviewProposals()
      mockAutomationApi.getProposals.mockRejectedValueOnce(new Error('network down'))
      await rp.loadProposals()

      // The read is over, so the loading term is false -- and a gate built on
      // it would announce "0 proposals awaiting review." for a queue nobody has
      // read.
      expect(rp.proposalsLoading.value).toBe(false)
      expect(rp.queueScopeLoaded.value).toBe(false)

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-a' })])
      await rp.loadProposals()
      expect(rp.queueScopeLoaded.value).toBe(true)
    })

    it('settles from a background poll, so a failed entry load recovers without an explicit reload', async () => {
      const rp = useReviewProposals()
      mockAutomationApi.getProposals.mockRejectedValueOnce(new Error('network down'))
      await rp.loadProposals()
      expect(rp.queueScopeLoaded.value).toBe(false)

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-a' })])
      await rp.refreshProposals()
      expect(rp.queueScopeLoaded.value).toBe(true)
    })

    it('unsettles when a refusal clears the queue and settles again when access returns', async () => {
      const rp = useReviewProposals()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-a' })])
      await rp.loadProposals()
      expect(rp.queueScopeLoaded.value).toBe(true)

      // `recordQueueAccessRevoked` empties the queue: what is rendered is no
      // longer any read's answer.
      mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 403 } })
      await rp.loadProposals()
      expect(rp.queueAccessRevoked.value).toBe(true)
      expect(rp.queueScopeLoaded.value).toBe(false)

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-a' })])
      await rp.loadProposals()
      expect(rp.queueAccessRevoked.value).toBe(false)
      expect(rp.queueScopeLoaded.value).toBe(true)
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

    it('openProposal preserves query context and emits the supplied canonical id', () => {
      mockRoute.query = { boardId: 'board-x' }
      const rp = useReviewProposals()
      rp.openProposal('proposal-canonical')
      expect(mockRouter.push).toHaveBeenCalledWith({
        name: 'workspace-review',
        query: { boardId: 'board-x' },
        hash: '#proposal-proposal-canonical',
      })
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

    it('clearBoardFilter replaces only the board query without a reload', async () => {
      mockRoute.query = { boardId: 'board-z', source: 'proposal' }
      mockRoute.hash = '#proposal-p-1'
      const rp = useReviewProposals()
      await rp.clearBoardFilter()
      expect(mockRouter.replace).toHaveBeenCalledWith({
        name: 'workspace-review',
        query: { source: 'proposal' },
        hash: '#proposal-p-1',
      })
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

    it('history-mode watcher reloads the same board as a new read identity', async () => {
      mockRoute.query = { boardId: 'board-history' }
      const rp = useReviewProposals()
      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'live', boardId: 'board-history' }),
      ])
      await rp.loadProposals()
      expect(rp.queueScopeLoaded.value).toBe(true)

      mockRoute.query = { boardId: 'board-history', history: 'archived' }
      expect(rp.isArchivedHistory.value).toBe(true)
      expect(rp.queueScopeLoaded.value).toBe(false)

      const watcher = watchers.find(([source]) => {
        if (typeof source !== 'function') return false
        const value = (source as () => unknown)()
        return Array.isArray(value) && value[0] === 'board-history' && value[1] === true
      })
      expect(watcher).toBeDefined()

      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'archived', boardId: 'board-history', status: 'Applied' }),
      ])
      await watcher![1]()

      expect(mockAutomationApi.getProposals).toHaveBeenLastCalledWith({
        limit: 200,
        boardId: 'board-history',
      })
      expect(rp.proposals.value.map((proposal: any) => proposal.id)).toEqual(['archived'])
      expect(rp.queueScopeLoaded.value).toBe(true)

      mockRoute.query = { boardId: 'board-history' }
      expect(rp.isArchivedHistory.value).toBe(false)
      expect(rp.queueScopeLoaded.value).toBe(false)
      const reverseWatcher = watchers.find(([source]) => {
        if (typeof source !== 'function') return false
        const value = (source as () => unknown)()
        return Array.isArray(value) && value[0] === 'board-history' && value[1] === false
      })
      expect(reverseWatcher).toBeDefined()

      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'live-again', boardId: 'board-history' }),
      ])
      await reverseWatcher![1]()
      expect(rp.proposals.value.map((proposal: any) => proposal.id)).toEqual(['live-again'])
      expect(rp.queueScopeLoaded.value).toBe(true)
    })

    it('discards a late live response after a same-board history transition', async () => {
      mockRoute.query = { boardId: 'board-history' }
      let resolveLive!: (proposals: ReturnType<typeof makeProposal>[]) => void
      mockAutomationApi.getProposals.mockReturnValueOnce(
        new Promise((resolve) => {
          resolveLive = resolve
        }),
      )
      const rp = useReviewProposals()
      const liveLoad = rp.loadProposalsWithOutcome()

      mockRoute.query = { boardId: 'board-history', history: 'archived' }
      const historyWatcher = watchers.find(([source]) => {
        if (typeof source !== 'function') return false
        const value = (source as () => unknown)()
        return Array.isArray(value) && value[0] === 'board-history' && value[1] === true
      })
      expect(historyWatcher).toBeDefined()
      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'archived', boardId: 'board-history', status: 'Applied' }),
      ])

      await historyWatcher![1]()
      expect(rp.proposals.value.map((proposal: any) => proposal.id)).toEqual(['archived'])

      resolveLive([makeProposal({ id: 'late-live', boardId: 'board-history' })])
      await expect(liveLoad).resolves.toBe('superseded')
      expect(rp.proposals.value.map((proposal: any) => proposal.id)).toEqual(['archived'])
    })
  })

  // #2194 - with Review open, a proposal created server-side never appeared:
  // measured 115 s of "QUEUE - 0 AWAITING" while the API reported 1 pending, and
  // only re-navigation surfaced it. There is no proposal event on the wire (the
  // sole hub is per-board and silent on proposals), so the queue is kept live by
  // a bounded, visibility-aware poll.
  describe('background queue refresh (#2194)', () => {
    function setVisibility(state: 'visible' | 'hidden') {
      Object.defineProperty(document, 'visibilityState', {
        configurable: true,
        get: () => state,
      })
    }

    beforeEach(() => {
      setVisibility('visible')
    })

    it('surfaces a proposal created after load, without a navigation', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(rp.visibleProposals.value).toHaveLength(0)

      // Ask AI ran on a capture elsewhere; the server now has one pending item.
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-new' })])
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      expect(rp.visibleProposals.value.map((p: any) => p.id)).toEqual(['p-new'])
      // Re-navigation is the defect's only workaround today; it must not be the fix.
      expect(mockRouter.push).not.toHaveBeenCalled()
      expect(mockRouter.replace).not.toHaveBeenCalled()
      rp.stopQueueRefresh()
    })

    it('stops polling on route leave', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValue([])
      const rp = useReviewProposals()
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(mockAutomationApi.getProposals).toHaveBeenCalledTimes(1)

      rp.stopQueueRefresh() // what onUnmounted does when Review is left
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS * 4)
      expect(mockAutomationApi.getProposals).toHaveBeenCalledTimes(1)
    })

    it('skips ticks while the tab is hidden, and reads immediately on re-entry', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValue([])
      const rp = useReviewProposals()
      setVisibility('hidden')
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS * 3)
      expect(mockAutomationApi.getProposals).not.toHaveBeenCalled()

      setVisibility('visible')
      document.dispatchEvent(new Event('visibilitychange'))
      await vi.advanceTimersByTimeAsync(0)
      expect(mockAutomationApi.getProposals).toHaveBeenCalledTimes(1)
      rp.stopQueueRefresh()
    })

    it('holds a tick while the surface reports a decision in progress', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValue([])
      const rp = useReviewProposals()
      let deciding = true
      rp.startQueueRefresh(() => !deciding)

      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS * 2)
      expect(mockAutomationApi.getProposals).not.toHaveBeenCalled()

      deciding = false // the confirm dialog closed
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(mockAutomationApi.getProposals).toHaveBeenCalledTimes(1)
      rp.stopQueueRefresh()
    })

    it('re-authorizes an omitted deep-link and inserts its current DTO at its createdAt position', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-open'
      const stalePinned = {
        ...makeProposal({ id: 'p-open', createdAt: '2026-01-02T00:00:00Z' }),
        summary: 'stale summary',
        latestRevisionId: 'revision-1',
      }
      mockAutomationApi.getProposals.mockResolvedValueOnce([
        stalePinned,
      ])
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-open'])

      // The record the URL names has dropped out of the list page. Its by-id DTO
      // has also moved to a newer revision, so restoring the cached row would
      // leave the reviewer looking at stale effective content.
      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-newer', createdAt: '2026-01-03T00:00:00Z' }),
        makeProposal({ id: 'p-older', createdAt: '2026-01-01T00:00:00Z' }),
      ])
      mockAutomationApi.getProposal.mockResolvedValueOnce({
        ...stalePinned,
        summary: 'current summary',
        latestRevisionId: 'revision-2',
      })
      const onQueueReplaced = vi.fn()
      rp.startQueueRefresh(undefined, { onQueueReplaced })
      await rp.refreshProposals()

      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-newer', 'p-open', 'p-older'])
      expect(rp.proposals.value[1]).toEqual(expect.objectContaining({
        summary: 'current summary',
        latestRevisionId: 'revision-2',
      }))
      // Exact options, so a silently dropped one is caught here. The
      // `expectedStatuses` entry is what keeps a refused or unbindable pin --
      // an outcome this leg handles explicitly -- from being logged as an API
      // error on every tick (#2214 item 7).
      expect(mockAutomationApi.getProposal).toHaveBeenCalledWith('p-open', {
        skipRetry: true,
        signal: expect.any(AbortSignal),
        expectedStatuses: [400, 403, 404],
      })
      expect(onQueueReplaced).toHaveBeenCalledTimes(1)
      expect(rp.unavailableProposalId.value).toBeNull()
      rp.stopQueueRefresh()
    })

    // 400 shares this outcome exactly (#2214 item 8): the id in the hash is not
    // one the by-id route can bind, which is as permanent a fact about that
    // target as a refusal or a deletion. Running it through the same case keeps
    // the three statuses provably identical, recovery half included.
    it.each([400, 403, 404])(
      'drops and marks only an omitted deep-link unavailable when its by-id read returns %s',
      async (status) => {
        vi.useFakeTimers()
        mockRoute.hash = '#proposal-p-open'
        mockAutomationApi.getProposals.mockResolvedValueOnce([
          makeProposal({ id: 'p-open', createdAt: '2026-01-02T00:00:00Z' }),
        ])
        const rp = useReviewProposals()
        await rp.loadProposals()

        mockAutomationApi.getProposals.mockResolvedValueOnce([
          makeProposal({ id: 'p-survivor', createdAt: '2026-01-01T00:00:00Z' }),
        ])
        mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status } })
        const onQueueReplaced = vi.fn()
        rp.startQueueRefresh(undefined, { onQueueReplaced })
        await rp.refreshProposals()

        expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-survivor'])
        expect(rp.unavailableProposalId.value).toBe('p-open')
        // The list succeeded, so a refused pin does not revoke the readable
        // queue or suspend its interval.
        expect(rp.queueAccessRevoked.value).toBe(false)
        // The explicit unavailable state owns this transition. Signalling an
        // ordinary queue replacement would make Paper render its settled-row
        // notice first and strand the unavailable hash behind that notice.
        expect(onQueueReplaced).not.toHaveBeenCalled()
        expect(mockToast.error).not.toHaveBeenCalled()

        const recoveredTarget = {
          ...makeProposal({ id: 'p-open', createdAt: '2026-01-02T00:00:00Z' }),
          summary: 'readable again',
        }
        mockAutomationApi.getProposals.mockResolvedValueOnce([recoveredTarget])
        await rp.refreshProposals()

        expect(rp.proposals.value).toEqual([recoveredTarget])
        expect(rp.unavailableProposalId.value).toBeNull()
        expect(onQueueReplaced).toHaveBeenCalledTimes(1)
        rp.stopQueueRefresh()
      },
    )

    // #2214 item 8: a malformed `#proposal-<id>` (anything that is not a GUID)
    // makes the by-id route answer with a model-binding 400. That 400 used to
    // fall through to the transient-failure branch, which RETURNS before
    // `proposals.value = next` -- so the list answer that had already arrived
    // was discarded, and because a 400 is not transient the failure counter
    // reset instead of climbing to the degraded threshold. The queue froze with
    // no indication, on every tick, for as long as the bad link stayed in the
    // URL. The list read succeeded, so its answer is the queue; only the pin is
    // unusable, which is exactly the 403/404 outcome.
    it('lands the readable queue and marks only the pin unavailable when a malformed deep-link target answers 400', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-not-a-guid'
      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-existing', createdAt: '2026-01-02T00:00:00Z' }),
      ])
      mockAutomationApi.getProposal.mockRejectedValue({ response: { status: 400 } })
      const rp = useReviewProposals()
      await rp.loadProposals()
      // The explicit deep-link path reaches the SAME conclusion from the same
      // 400, and does so first. It used to raise a generic toast and set no
      // state, which was the asymmetry #2658 recorded and this slice removed:
      // one fact, one outcome, whichever leg observed it.
      expect(rp.unavailableProposalId.value).toBe('not-a-guid')
      expect(rp.unavailableProposalMalformed.value).toBe(true)
      expect(mockToast.error).not.toHaveBeenCalled()
      mockToast.error.mockClear()

      const queueBeforePoll = rp.proposals.value
      mockAutomationApi.getProposals.mockResolvedValue([
        makeProposal({ id: 'p-server-new', createdAt: '2026-01-03T00:00:00Z' }),
      ])
      const onQueueReplaced = vi.fn()
      rp.startQueueRefresh(undefined, { onQueueReplaced })
      await rp.refreshProposals()

      expect(rp.proposals.value).not.toBe(queueBeforePoll)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-server-new'])
      expect(rp.unavailableProposalId.value).toBe('not-a-guid')
      // A proposal-level refusal is not whole-queue revocation, and the queue on
      // screen is the freshly read one, so nothing is degraded.
      expect(rp.queueAccessRevoked.value).toBe(false)
      expect(rp.queueRefreshStale.value).toBe(false)
      // The explicit unavailable state owns this transition; the settled-row
      // notice would otherwise win the render branch and hide it.
      expect(onQueueReplaced).not.toHaveBeenCalled()
      expect(mockToast.error).not.toHaveBeenCalled()

      // The consecutive-failure counter is private. The only thing it drives is
      // the degraded warning at THRESHOLD consecutive transient failures, so
      // polling that many more times with the same 400 pin proves it is not
      // being incremented.
      const pinReadsAfterFirstPoll = mockAutomationApi.getProposal.mock.calls.length
      for (let i = 0; i < REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD; i += 1) {
        await rp.refreshProposals()
      }
      expect(rp.queueRefreshStale.value).toBe(false)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-server-new'])
      // Matching the shipped 403/404 behaviour: nothing suppresses the pin read
      // while the target stays outside the list, so every later tick retries it.
      expect(mockAutomationApi.getProposal.mock.calls.length).toBe(
        pinReadsAfterFirstPoll + REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD,
      )
      rp.stopQueueRefresh()
    })

    it('rechecks an unavailable deep-link and restores it when it becomes readable outside the list page', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-open'
      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-existing', createdAt: '2026-01-01T00:00:00Z' }),
      ])
      mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status: 404 } })
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(rp.unavailableProposalId.value).toBe('p-open')

      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-newer', createdAt: '2026-01-03T00:00:00Z' }),
        makeProposal({ id: 'p-older', createdAt: '2026-01-01T00:00:00Z' }),
      ])
      const recoveredTarget = {
        ...makeProposal({ id: 'p-open', createdAt: '2026-01-02T00:00:00Z' }),
        summary: 'readable outside the capped queue',
      }
      mockAutomationApi.getProposal.mockResolvedValueOnce(recoveredTarget)
      rp.startQueueRefresh()
      await rp.refreshProposals()

      expect(mockAutomationApi.getProposal).toHaveBeenCalledTimes(2)
      expect(rp.proposals.value.map((proposal: any) => proposal.id)).toEqual([
        'p-newer',
        'p-open',
        'p-older',
      ])
      expect(rp.proposals.value[1]).toEqual(recoveredTarget)
      expect(rp.unavailableProposalId.value).toBeNull()
      rp.stopQueueRefresh()
    })

    it('preserves the exact prior queue and availability when an omitted deep-link read fails transiently', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-open'
      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-open' }),
        makeProposal({ id: 'p-existing' }),
      ])
      const rp = useReviewProposals()
      await rp.loadProposals()
      const queueBeforePoll = rp.proposals.value

      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-server-new' }),
      ])
      mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status: 503 } })
      const onQueueReplaced = vi.fn()
      rp.startQueueRefresh(undefined, { onQueueReplaced })
      await rp.refreshProposals()

      expect(rp.proposals.value).toBe(queueBeforePoll)
      expect(rp.unavailableProposalId.value).toBeNull()
      expect(onQueueReplaced).not.toHaveBeenCalled()
      expect(mockToast.error).not.toHaveBeenCalled()
      rp.stopQueueRefresh()
    })

    it.each([
      {
        label: 'a response for another proposal',
        proposal: makeProposal({ id: 'p-wrong', boardId: 'board-a' }),
      },
      {
        label: 'a proposal outside the active board scope',
        proposal: makeProposal({ id: 'p-open', boardId: 'board-b' }),
      },
    ])('fails closed when the omitted deep-link read returns $label', async ({ proposal }) => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-open'
      mockRoute.query = { boardId: 'board-a' }
      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-open', boardId: 'board-a' }),
      ])
      const rp = useReviewProposals()
      await rp.loadProposals()

      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-survivor', boardId: 'board-a' }),
      ])
      mockAutomationApi.getProposal.mockResolvedValueOnce(proposal)
      rp.startQueueRefresh()
      await rp.refreshProposals()

      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-survivor'])
      expect(rp.unavailableProposalId.value).toBe('p-open')
      rp.stopQueueRefresh()
    })

    it('never raises proposalsLoading, so no skeleton flashes under a reviewer', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      const rp = useReviewProposals()
      await rp.loadProposals()

      let release: (value: unknown[]) => void = () => {}
      mockAutomationApi.getProposals.mockReturnValueOnce(
        new Promise((resolve) => {
          release = resolve as (value: unknown[]) => void
        }),
      )
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      expect(rp.proposalsLoading.value).toBe(false) // in flight, and still quiet
      release([])
      rp.stopQueueRefresh()
    })

    it('keeps the queue and stays silent when a background read fails', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-1' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      mockToast.error.mockClear()

      mockAutomationApi.getProposals.mockRejectedValueOnce(new Error('offline'))
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      // A poll the reviewer never asked for must not raise a toast, and must not
      // empty the queue it failed to re-read.
      expect(mockToast.error).not.toHaveBeenCalled()
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-1'])
      rp.stopQueueRefresh()
    })

    function deferredProposals() {
      let release: (value: unknown[]) => void = () => {}
      mockAutomationApi.getProposals.mockReturnValueOnce(
        new Promise((resolve) => {
          release = resolve as (value: unknown[]) => void
        }),
      )
      return {
        release: (value: unknown[]) => release(value),
      }
    }

    function deferredProposal() {
      let release: (value: unknown) => void = () => {}
      mockAutomationApi.getProposal.mockReturnValueOnce(
        new Promise((resolve) => {
          release = resolve
        }),
      )
      return {
        release: (value: unknown) => release(value),
      }
    }

    it('discards an omitted-pin detail that resolves after the board scope changed', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-open'
      mockRoute.query = { boardId: 'board-a' }
      const pinned = makeProposal({ id: 'p-open', boardId: 'board-a' })
      mockAutomationApi.getProposals.mockResolvedValueOnce([pinned])
      const rp = useReviewProposals()
      await rp.loadProposals()
      const queueBeforePoll = rp.proposals.value

      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      const detail = deferredProposal()
      const onQueueReplaced = vi.fn()
      rp.startQueueRefresh(undefined, { onQueueReplaced })
      const refresh = rp.refreshProposals()
      await vi.advanceTimersByTimeAsync(0)
      expect(mockAutomationApi.getProposal).toHaveBeenCalledOnce()

      mockRoute.query = { boardId: 'board-b' }
      detail.release({ ...pinned, summary: 'late board-a detail' })
      await refresh

      expect(rp.proposals.value).toBe(queueBeforePoll)
      expect(rp.unavailableProposalId.value).toBeNull()
      expect(onQueueReplaced).not.toHaveBeenCalled()
      rp.stopQueueRefresh()
    })

    it('discards an omitted-pin detail when a newer explicit load replaced the queue', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-open'
      const pinned = makeProposal({ id: 'p-open' })
      mockAutomationApi.getProposals.mockResolvedValueOnce([pinned])
      const rp = useReviewProposals()
      await rp.loadProposals()

      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      const detail = deferredProposal()
      const onQueueReplaced = vi.fn()
      rp.startQueueRefresh(undefined, { onQueueReplaced })
      const refresh = rp.refreshProposals()
      await vi.advanceTimersByTimeAsync(0)

      const explicitlyLoaded = { ...pinned, summary: 'newer explicit load' }
      mockAutomationApi.getProposals.mockResolvedValueOnce([explicitlyLoaded])
      await rp.loadProposals()
      const queueAfterLoad = rp.proposals.value
      detail.release({ ...pinned, summary: 'late poll detail' })
      await refresh

      expect(rp.proposals.value).toBe(queueAfterLoad)
      expect(rp.proposals.value[0]).toEqual(expect.objectContaining({ summary: 'newer explicit load' }))
      expect(rp.unavailableProposalId.value).toBeNull()
      expect(onQueueReplaced).not.toHaveBeenCalled()
      rp.stopQueueRefresh()
    })

    it('discards an omitted-pin detail after a decision replaced the queue', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-open'
      const pending = makeProposal({ id: 'p-open', status: 'PendingReview' })
      mockAutomationApi.getProposals.mockResolvedValueOnce([pending])
      const rp = useReviewProposals()
      await rp.loadProposals()

      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      const detail = deferredProposal()
      const onQueueReplaced = vi.fn()
      rp.startQueueRefresh(undefined, { onQueueReplaced })
      const refresh = rp.refreshProposals()
      await vi.advanceTimersByTimeAsync(0)

      rp.proposals.value = rp.proposals.value.map((proposal: any) => ({
        ...proposal,
        status: 'Approved',
      }))
      const queueAfterDecision = rp.proposals.value
      detail.release(pending)
      await refresh

      expect(rp.proposals.value).toBe(queueAfterDecision)
      expect(rp.proposals.value[0]?.status).toBe('Approved')
      expect(onQueueReplaced).not.toHaveBeenCalled()
      rp.stopQueueRefresh()
    })

    it('discards an omitted-pin detail after an out-of-band revision write', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-open'
      const pinned = makeProposal({ id: 'p-open' })
      mockAutomationApi.getProposals.mockResolvedValueOnce([pinned])
      const rp = useReviewProposals()
      await rp.loadProposals()
      const queueBeforeWrite = rp.proposals.value

      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      const detail = deferredProposal()
      const onQueueReplaced = vi.fn()
      rp.startQueueRefresh(undefined, { onQueueReplaced })
      const refresh = rp.refreshProposals()
      await vi.advanceTimersByTimeAsync(0)

      rp.invalidateQueueReads()
      detail.release({ ...pinned, summary: 'pre-write detail' })
      await refresh

      expect(rp.proposals.value).toBe(queueBeforeWrite)
      expect(onQueueReplaced).not.toHaveBeenCalled()
      rp.stopQueueRefresh()
    })

    it('discards an omitted-pin detail after the hash target changed', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-open'
      const pinned = makeProposal({ id: 'p-open' })
      mockAutomationApi.getProposals.mockResolvedValueOnce([pinned])
      const rp = useReviewProposals()
      await rp.loadProposals()
      const queueBeforeHashChange = rp.proposals.value

      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      const detail = deferredProposal()
      const onQueueReplaced = vi.fn()
      rp.startQueueRefresh(undefined, { onQueueReplaced })
      const refresh = rp.refreshProposals()
      await vi.advanceTimersByTimeAsync(0)

      mockRoute.hash = '#proposal-p-new'
      detail.release({ ...pinned, summary: 'late old-target detail' })
      await refresh

      expect(rp.proposals.value).toBe(queueBeforeHashChange)
      expect(rp.unavailableProposalId.value).toBeNull()
      expect(onQueueReplaced).not.toHaveBeenCalled()
      rp.stopQueueRefresh()
    })

    it('aborts an omitted-pin detail on stop and ignores a late resolution', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-open'
      const pinned = makeProposal({ id: 'p-open' })
      mockAutomationApi.getProposals.mockResolvedValueOnce([pinned])
      const rp = useReviewProposals()
      await rp.loadProposals()
      const queueBeforeStop = rp.proposals.value

      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      const detail = deferredProposal()
      const onQueueReplaced = vi.fn()
      rp.startQueueRefresh(undefined, { onQueueReplaced })
      const refresh = rp.refreshProposals()
      await vi.advanceTimersByTimeAsync(0)
      const detailOptions = mockAutomationApi.getProposal.mock.calls.at(-1)?.[1] as {
        signal?: AbortSignal
      }
      expect(detailOptions.signal?.aborted).toBe(false)

      rp.stopQueueRefresh()
      expect(detailOptions.signal?.aborted).toBe(true)
      detail.release({ ...pinned, summary: 'late after stop' })
      await refresh

      expect(rp.proposals.value).toBe(queueBeforeStop)
      expect(rp.unavailableProposalId.value).toBeNull()
      expect(onQueueReplaced).not.toHaveBeenCalled()
    })

    it('discards a read that resolves after a decision patched the queue', async () => {
      vi.useFakeTimers()
      const pending = makeProposal({ id: 'p-1', status: 'PendingReview' })
      mockAutomationApi.getProposals.mockResolvedValueOnce([pending])
      const rp = useReviewProposals()
      await rp.loadProposals()

      // A background read is issued BEFORE the reviewer clicks.
      const inFlight = deferredProposals()
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      // The approve lands first and patches the row locally, assigning a NEW
      // array -- exactly what useReviewActions does, without touching the load
      // counter.
      rp.proposals.value = rp.proposals.value.map((p: any) =>
        p.id === 'p-1' ? { ...p, status: 'Approved' } : p,
      )

      // Only now does the stale read answer, still carrying PendingReview.
      inFlight.release([pending])
      await vi.advanceTimersByTimeAsync(0)

      // Writing it would revert the row under a receipt that says approved.
      expect(rp.proposals.value.map((p: any) => p.status)).toEqual(['Approved'])
      rp.stopQueueRefresh()
    })

    it('discards a read that resolves after a confirm dialog opened', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-1' })])
      const rp = useReviewProposals()
      await rp.loadProposals()

      let deciding = false
      const inFlight = deferredProposals()
      rp.startQueueRefresh(() => !deciding)
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      // The Reject dialog opens while the read is in flight. Its proposal is a
      // computed over the queue, so landing this answer would close the dialog
      // and discard a half-typed reason.
      deciding = true
      inFlight.release([makeProposal({ id: 'p-2' })])
      await vi.advanceTimersByTimeAsync(0)

      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-1'])
      rp.stopQueueRefresh()
    })

    it('discards a read that resolves after a proposal revision was saved', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-1', summary: 'pre-revision' }),
      ])
      const rp = useReviewProposals()
      await rp.loadProposals()
      const queueBeforeSave = rp.proposals.value

      // A queue read goes in flight BEFORE the reviewer saves an edit.
      const inFlight = deferredProposals()
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      // The revision POST lands first. Saving a revision updates
      // useProposalRevisions' own state and NEVER replaces the proposals array,
      // so the identity guard cannot see it -- the write generation is what makes
      // this read stale.
      rp.invalidateQueueReads()

      // The pre-save answer arrives, still carrying the pre-revision record.
      inFlight.release([makeProposal({ id: 'p-1', summary: 'pre-revision' })])
      await vi.advanceTimersByTimeAsync(0)

      // Writing it would restore the pre-revision summary/operations under the
      // saved-revision state the reviewer is looking at.
      expect(rp.proposals.value).toBe(queueBeforeSave)
      rp.stopQueueRefresh()
    })

    it('ignores a 403 answering a board scope the reviewer already left', async () => {
      vi.useFakeTimers()
      mockRoute.query = { boardId: 'board-a' }
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'a-1' })])
      const rp = useReviewProposals()
      await rp.loadProposals()

      // A scoped poll for board A goes in flight.
      let rejectA: (reason: unknown) => void = () => {}
      mockAutomationApi.getProposals.mockReturnValueOnce(
        new Promise((_resolve, reject) => {
          rejectA = reject
        }),
      )
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      // The reviewer switches to board B, which they can read fine.
      mockRoute.query = { boardId: 'board-b' }
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'b-1' })])
      await rp.loadProposals()

      // Only now does board A's 403 arrive.
      rejectA({ response: { status: 403 } })
      await vi.advanceTimersByTimeAsync(0)

      // B is authorized: its queue must survive and its polling must continue.
      expect(rp.queueAccessRevoked.value).toBe(false)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['b-1'])

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'b-2' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['b-2'])
      rp.stopQueueRefresh()
    })

    it('still accepts a current-scope list 403 when only the hash target changed', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-1'
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-1' })])
      const rp = useReviewProposals()
      await rp.loadProposals()

      let rejectList: (reason: unknown) => void = () => {}
      mockAutomationApi.getProposals.mockReturnValueOnce(
        new Promise((_resolve, reject) => {
          rejectList = reject
        }),
      )
      rp.startQueueRefresh()
      const refresh = rp.refreshProposals()
      await vi.advanceTimersByTimeAsync(0)

      // Hash navigation changes only the selected proposal. The list refusal
      // still answers the same readable-queue scope and remains authoritative.
      mockRoute.hash = '#proposal-p-2'
      rejectList({ response: { status: 403 } })
      await refresh

      expect(rp.queueAccessRevoked.value).toBe(true)
      expect(rp.proposals.value).toEqual([])
    })

    it('stops polling and reports revoked access when a read is refused with 403', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-1' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(rp.queueAccessRevoked.value).toBe(false)

      mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 403 } })
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      expect(rp.queueAccessRevoked.value).toBe(true)
      expect(rp.proposals.value).toEqual([])

      // And it must not keep hammering an endpoint that will keep refusing.
      const calls = mockAutomationApi.getProposals.mock.calls.length
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS * 4)
      expect(mockAutomationApi.getProposals.mock.calls.length).toBe(calls)
    })

    it('restarts one poll after permission recovery with the original guard and replacement hook', async () => {
      vi.useFakeTimers()
      const intervalSpy = vi.spyOn(globalThis, 'setInterval')
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-1' })])
      const rp = useReviewProposals()
      await rp.loadProposals()

      let decisionInProgress = false
      const onQueueReplaced = vi.fn()
      rp.startQueueRefresh(() => !decisionInProgress, { onQueueReplaced })
      expect(intervalSpy).toHaveBeenCalledTimes(1)

      mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 403 } })
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueAccessRevoked.value).toBe(true)

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-recovered' })])
      await rp.loadProposals()

      expect(rp.queueAccessRevoked.value).toBe(false)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-recovered'])
      // The suspended interval is replaced once; an explicit load is not a
      // poll-driven queue replacement and must not fire the hook itself.
      expect(intervalSpy).toHaveBeenCalledTimes(2)
      expect(onQueueReplaced).not.toHaveBeenCalled()

      mockAutomationApi.getProposals.mockClear()
      decisionInProgress = true
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS * 2)
      expect(mockAutomationApi.getProposals).not.toHaveBeenCalled()

      decisionInProgress = false
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-after-recovery' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      expect(mockAutomationApi.getProposals).toHaveBeenCalledTimes(1)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-after-recovery'])
      expect(onQueueReplaced).toHaveBeenCalledTimes(1)
      rp.stopQueueRefresh()
    })

    it('does not resurrect polling when a recovery load resolves after permanent stop', async () => {
      vi.useFakeTimers()
      const intervalSpy = vi.spyOn(globalThis, 'setInterval')
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-1' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 403 } })
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueAccessRevoked.value).toBe(true)
      expect(intervalSpy).toHaveBeenCalledTimes(1)

      let releaseRecovery: (value: unknown[]) => void = () => {}
      mockAutomationApi.getProposals.mockReturnValueOnce(
        new Promise((resolve) => {
          releaseRecovery = resolve as (value: unknown[]) => void
        }),
      )
      const recoveryLoad = rp.loadProposals()
      await vi.advanceTimersByTimeAsync(0)

      // Mirrors route unmount / scope disposal while the explicit read is open.
      rp.stopQueueRefresh()
      releaseRecovery([makeProposal({ id: 'p-late' })])
      await recoveryLoad

      expect(rp.queueAccessRevoked.value).toBe(false)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-late'])
      expect(intervalSpy).toHaveBeenCalledTimes(1)

      mockAutomationApi.getProposals.mockClear()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS * 3)
      expect(mockAutomationApi.getProposals).not.toHaveBeenCalled()
    })

    it('treats a transient failure as transient, not as revoked access', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-1' })])
      const rp = useReviewProposals()
      await rp.loadProposals()

      mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 500 } })
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      // A 500 must not tear down a queue the user still has access to.
      expect(rp.queueAccessRevoked.value).toBe(false)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-1'])

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-2' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-2'])
      rp.stopQueueRefresh()
    })

    it('bounds a hung poll and ignores the response that arrives after its deadline', async () => {
      vi.useFakeTimers()
      const current = makeProposal({ id: 'current' })
      mockAutomationApi.getProposals.mockResolvedValueOnce([current])
      const rp = useReviewProposals()
      await rp.loadProposals()

      const hungRead = deferredProposals()
      mockAutomationApi.getProposals.mockReturnValueOnce(hungRead.promise)
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      const options = mockAutomationApi.getProposals.mock.calls.at(-1)?.[1] as {
        signal?: AbortSignal
      }
      expect(options.signal?.aborted).toBe(false)

      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REQUEST_DEADLINE_MS)
      expect(options.signal?.aborted).toBe(true)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['current'])
      expect(rp.queueRefreshStale.value).toBe(false)

      hungRead.release([makeProposal({ id: 'late' })])
      await vi.advanceTimersByTimeAsync(0)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['current'])
      rp.stopQueueRefresh()
    })

    it('marks consecutive transient failures as stale and clears the state after a successful poll', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      for (let failure = 1; failure <= REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD; failure += 1) {
        mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 500 } })
        await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
        expect(rp.queueRefreshStale.value).toBe(
          failure === REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD,
        )
      }
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['current'])

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'recovered' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshStale.value).toBe(false)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['recovered'])
      rp.stopQueueRefresh()
    })

    it('resets the transient failure run after a non-transient failure without fabricating recovery', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      const pollWith = async (failure: unknown) => {
        mockAutomationApi.getProposals.mockRejectedValueOnce(failure)
        await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      }

      await pollWith({ response: { status: 500 } })
      await pollWith({ response: { status: 500 } })
      expect(rp.queueRefreshStale.value).toBe(false)

      // A malformed/non-transient response breaks the uninterrupted transient
      // run, but it cannot prove that the retained queue is fresh.
      await pollWith({ response: { status: 400 } })
      expect(rp.queueRefreshStale.value).toBe(false)

      await pollWith({ response: { status: 500 } })
      await pollWith({ response: { status: 500 } })
      expect(rp.queueRefreshStale.value).toBe(false)

      await pollWith({ response: { status: 500 } })
      expect(rp.queueRefreshStale.value).toBe(true)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['current'])

      await pollWith({ response: { status: 400 } })
      expect(rp.queueRefreshStale.value).toBe(true)

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'recovered' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshStale.value).toBe(false)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['recovered'])
      rp.stopQueueRefresh()
    })

    it('does not count teardown or a superseded board read as a queue failure', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'a-1' })])
      const stopped = useReviewProposals()
      await stopped.loadProposals()
      let rejectTeardownRead: (reason: unknown) => void = () => {}
      mockAutomationApi.getProposals.mockReturnValueOnce(
        new Promise((_resolve, reject) => { rejectTeardownRead = reject }),
      )
      stopped.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      stopped.stopQueueRefresh()
      rejectTeardownRead(new Error('aborted during teardown'))
      await vi.advanceTimersByTimeAsync(0)
      expect(stopped.queueRefreshStale.value).toBe(false)

      mockRoute.query = { boardId: 'board-a' }
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'a-2', boardId: 'board-a' })])
      const superseded = useReviewProposals()
      await superseded.loadProposals()
      let rejectOldScopeRead: (reason: unknown) => void = () => {}
      mockAutomationApi.getProposals.mockReturnValueOnce(
        new Promise((_resolve, reject) => { rejectOldScopeRead = reject }),
      )
      superseded.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      mockRoute.query = { boardId: 'board-b' }
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'b-1', boardId: 'board-b' })])
      await superseded.loadProposals()
      rejectOldScopeRead(new Error('old board became unavailable'))
      await vi.advanceTimersByTimeAsync(0)
      expect(superseded.queueRefreshStale.value).toBe(false)
      expect(superseded.proposals.value.map((p: any) => p.id)).toEqual(['b-1'])
      superseded.stopQueueRefresh()
    })

    it('opts out of the retry interceptor and cancels an in-flight read on stop', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      const rp = useReviewProposals()
      await rp.loadProposals()

      const inFlight = deferredProposals()
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      const lastCall = mockAutomationApi.getProposals.mock.calls.at(-1) as [
        unknown,
        { skipRetry?: boolean; signal?: AbortSignal },
      ]
      const options = lastCall[1]
      // The shared interceptor would retry three times with backoff, keeping a
      // dead poll alive for seconds past the tick that asked for it.
      expect(options.skipRetry).toBe(true)
      expect(options.signal?.aborted).toBe(false)

      rp.stopQueueRefresh()
      expect(options.signal?.aborted).toBe(true)

      // A late answer from the cancelled read must not write into a left surface.
      inFlight.release([makeProposal({ id: 'ghost' })])
      await vi.advanceTimersByTimeAsync(0)
      expect(rp.proposals.value).toEqual([])
    })

    it('startQueueRefresh is idempotent so a double call cannot leak an interval', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValue([])
      const rp = useReviewProposals()
      rp.startQueueRefresh()
      rp.startQueueRefresh() // guarded no-op
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(mockAutomationApi.getProposals).toHaveBeenCalledTimes(1)

      // One stop fully halts it - proving no orphaned second interval.
      rp.stopQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS * 3)
      expect(mockAutomationApi.getProposals).toHaveBeenCalledTimes(1)
    })

    it('registers stopQueueRefresh via onScopeDispose', async () => {
      vi.useFakeTimers()
      mockOnScopeDispose.mockClear()
      mockAutomationApi.getProposals.mockResolvedValue([])
      const rp = useReviewProposals()
      rp.startQueueRefresh()

      const disposers = mockOnScopeDispose.mock.calls.map((call: any[]) => call[0] as () => void)
      disposers.forEach((dispose) => dispose())
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS * 3)
      expect(mockAutomationApi.getProposals).not.toHaveBeenCalled()
    })
  })

  describe('degraded-queue recovery signal (#2214)', () => {
    /**
     * `queueRefreshStale` is the degraded STATE, and it is cleared silently: the
     * warning is simply gone on the next render, so a reviewer who is not
     * looking at that corner is never told the queue is trustworthy again. The
     * recovery signal is the EVENT the surfaces announce, and it must fire only
     * on a real degraded -> recovered transition. A signal that also fired on an
     * ordinary success would announce "up to date again" every 15 seconds.
     */
    async function pollTransientFailures(count: number) {
      for (let failure = 0; failure < count; failure += 1) {
        mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 500 } })
        await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      }
    }

    it('fires when a successful poll clears a degraded queue', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()
      expect(rp.queueRefreshRecovered.value).toBe(false)

      await pollTransientFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD)
      expect(rp.queueRefreshStale.value).toBe(true)
      // Still degraded: there is nothing to announce yet.
      expect(rp.queueRefreshRecovered.value).toBe(false)

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'recovered' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshStale.value).toBe(false)
      expect(rp.queueRefreshRecovered.value).toBe(true)
      rp.stopQueueRefresh()
    })

    it('stays silent when a successful poll follows a successful poll', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValue([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      // Nothing was ever degraded, so nothing recovered. An ungated signal here
      // would make both skins announce on every poll.
      expect(rp.queueRefreshStale.value).toBe(false)
      expect(rp.queueRefreshRecovered.value).toBe(false)
      rp.stopQueueRefresh()
    })

    it('fires when an explicit load clears a degraded queue', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      await pollTransientFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD)
      expect(rp.queueRefreshStale.value).toBe(true)

      // The explicit-load clear is the second way out of the degraded state and
      // is just as invisible to a reviewer who is not watching the warning.
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'explicit' })])
      await rp.loadProposals()
      expect(rp.queueRefreshStale.value).toBe(false)
      expect(rp.queueRefreshRecovered.value).toBe(true)
      rp.stopQueueRefresh()
    })

    it('stays silent when a non-transient failure resets the run with no visible stale state', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      // Two transient failures is below the threshold, so no warning was ever
      // shown; the 400 then resets the run. The success that follows recovers
      // nothing the reviewer was ever told about.
      await pollTransientFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD - 1)
      mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 400 } })
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshStale.value).toBe(false)

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'quiet' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshStale.value).toBe(false)
      expect(rp.queueRefreshRecovered.value).toBe(false)
      rp.stopQueueRefresh()
    })

    it('retires the recovered sentence on the following success and does not re-fire', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      await pollTransientFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD)
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'recovered' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshRecovered.value).toBe(true)

      // An announcement is an event. Left standing it becomes a claim about the
      // present that nothing is re-checking, so the next healthy poll retires
      // it -- about one poll interval of life, with no timer to tear down.
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'still-fine' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshStale.value).toBe(false)
      expect(rp.queueRefreshRecovered.value).toBe(false)

      // And it stays retired: clearing must not oscillate the live region.
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'still-fine' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshRecovered.value).toBe(false)
      rp.stopQueueRefresh()
    })

    it('is not retired by an explicit load inside the poll interval (#2638 item 2)', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      await pollTransientFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD)
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'recovered' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshRecovered.value).toBe(true)

      // The reviewer's already-clicked Approve completes a few hundred
      // milliseconds after the recovering poll and reloads the queue. Every
      // explicit reload took the same success path, so it emptied the region
      // before a polite live region had any chance to speak the sentence --
      // the #2638 defect. The load itself is unchanged: the fresh queue lands.
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'after-decision' })])
      await rp.loadProposals()
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['after-decision'])
      expect(rp.queueRefreshRecovered.value).toBe(true)
      expect(rp.queueRefreshRecoveredKind.value).toBe('degraded')

      // Nor does a second one age it: explicit loads never retire, however many
      // of them land inside the interval.
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'and-again' })])
      await rp.loadProposals()
      expect(rp.queueRefreshRecovered.value).toBe(true)

      // The next BACKGROUND success is what retires it, exactly as #2630
      // intended -- about one poll interval of life.
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'still-fine' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshRecovered.value).toBe(false)
      expect(rp.queueRefreshRecoveredKind.value).toBe(null)
      rp.stopQueueRefresh()
    })

    it('is not retired by an explicit load that follows a FAILED background tick (#2638 item 2)', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      await pollTransientFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD)
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'recovered' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshRecovered.value).toBe(true)

      // A later background read alone is not the rule: that read has to SUCCEED
      // and be the one recording the success. This tick fails (below the
      // threshold, so nothing is disclosed), and the explicit load that follows
      // is still an explicit load.
      mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 500 } })
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'explicit' })])
      await rp.loadProposals()
      expect(rp.queueRefreshStale.value).toBe(false)
      expect(rp.queueRefreshRecovered.value).toBe(true)
      rp.stopQueueRefresh()
    })

    it('gives an EXPLICIT-load recovery a full interval before a poll can retire it (#2638 round 2)', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      await pollTransientFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD)
      expect(rp.queueRefreshStale.value).toBe(true)

      // The post-decision reload is the read that ends the degraded state here,
      // and it lands BETWEEN ticks -- 14.9 s into a 15 s cycle in the worst
      // case. Stamping that raise with the ordinal already on the counter names
      // a read that has finished, so the tick 100 ms later would retire the
      // sentence: the same defect this rule exists to close, with the roles
      // swapped (round-2 review finding).
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'explicit' })])
      await rp.loadProposals()
      expect(rp.queueRefreshStale.value).toBe(false)
      expect(rp.queueRefreshRecovered.value).toBe(true)
      expect(rp.queueRefreshRecoveredKind.value).toBe('degraded')

      // The next poll success is the one the sentence lives THROUGH.
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'first-poll' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshRecovered.value).toBe(true)

      // The one after retires it, so it is bounded exactly as a poll-raised
      // sentence is -- at least one full interval, never the session.
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'second-poll' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshRecovered.value).toBe(false)
      expect(rp.queueRefreshRecoveredKind.value).toBe(null)
      rp.stopQueueRefresh()
    })

    it('still retires an explicit-load recovery immediately at a degraded onset (#2638 round 2)', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      await pollTransientFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD)
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'explicit' })])
      await rp.loadProposals()
      expect(rp.queueRefreshRecovered.value).toBe(true)

      // The extra interval of life is about the sentence being OLD. An onset
      // makes it FALSE, and that retirement stays immediate for either stamp,
      // or the next real recovery would be silent.
      await pollTransientFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD)
      expect(rp.queueRefreshStale.value).toBe(true)
      expect(rp.queueRefreshRecovered.value).toBe(false)
      rp.stopQueueRefresh()
    })

    it('clears at the next degraded onset so a second recovery announces again', async () => {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      await pollTransientFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD)
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'first' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshRecovered.value).toBe(true)

      // A live region only announces when its TEXT changes, so the signal has to
      // fall back to false while the queue is degraded again or the second
      // recovery would be silent.
      await pollTransientFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD)
      expect(rp.queueRefreshStale.value).toBe(true)
      expect(rp.queueRefreshRecovered.value).toBe(false)

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'second' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshRecovered.value).toBe(true)
      rp.stopQueueRefresh()
    })
  })

  describe('refused list-read disclosure (#2214 item 2)', () => {
    /**
     * The transient counter (`queueRefreshStale`) deliberately IGNORES a
     * non-transient answer: a 400/404/405/410 on the LIST read resets its run
     * and returns. That leaves the worst case of all completely silent — a
     * `?boardId=not-a-guid` query 400s every single tick, the poll keeps
     * running, the counter keeps resetting, no degraded state ever rises, and
     * the surface shows an ordinary queue (or an ordinary empty state)
     * indefinitely while the server has not confirmed a single one of those
     * rows since the reviewer arrived.
     *
     * `queueRefreshRefused` is that second, separate threshold. Same threshold
     * and same #2445 ruling as the transient one, but its own uninterrupted
     * run, because the two facts are different: "the network keeps blipping"
     * versus "the server is answering and refusing".
     */
    async function pollListFailures(count: number, failure: unknown) {
      for (let attempt = 0; attempt < count; attempt += 1) {
        mockAutomationApi.getProposals.mockRejectedValueOnce(failure)
        await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      }
    }

    async function startedWithCurrentQueue() {
      vi.useFakeTimers()
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'current' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()
      expect(rp.queueRefreshRefused.value).toBe(false)
      return rp
    }

    it('rises only at the threshold, keeps the last trustworthy queue, and keeps polling', async () => {
      const rp = await startedWithCurrentQueue()

      for (let failure = 1; failure <= REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD; failure += 1) {
        await pollListFailures(1, { response: { status: 400 } })
        expect(rp.queueRefreshRefused.value).toBe(
          failure === REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD,
        )
      }
      // The retained queue is exactly what the server last confirmed.
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['current'])
      // And this is a disclosure, not a stop: the next tick still asks.
      const callsBefore = mockAutomationApi.getProposals.mock.calls.length
      await pollListFailures(1, { response: { status: 400 } })
      expect(mockAutomationApi.getProposals.mock.calls.length).toBe(callsBefore + 1)
      // The transient counter is a different fact and stays where it was.
      expect(rp.queueRefreshStale.value).toBe(false)
      rp.stopQueueRefresh()
    })

    it('counts 404, 405 and 410 as refusals too', async () => {
      const rp = await startedWithCurrentQueue()

      await pollListFailures(1, { response: { status: 404 } })
      await pollListFailures(1, { response: { status: 405 } })
      expect(rp.queueRefreshRefused.value).toBe(false)
      await pollListFailures(1, { response: { status: 410 } })
      expect(rp.queueRefreshRefused.value).toBe(true)
      rp.stopQueueRefresh()
    })

    it('does not rise on two refusals plus a success', async () => {
      const rp = await startedWithCurrentQueue()

      await pollListFailures(2, { response: { status: 400 } })
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'fresh' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshRefused.value).toBe(false)

      await pollListFailures(2, { response: { status: 400 } })
      expect(rp.queueRefreshRefused.value).toBe(false)
      rp.stopQueueRefresh()
    })

    it('lets an intervening transient failure reset the run without raising it', async () => {
      const rp = await startedWithCurrentQueue()

      await pollListFailures(2, { response: { status: 400 } })
      // A 500 is not a refusal. It breaks the uninterrupted run the disclosure
      // claims, so the next two 400s cannot complete a three-long streak.
      await pollListFailures(1, { response: { status: 500 } })
      await pollListFailures(2, { response: { status: 400 } })
      expect(rp.queueRefreshRefused.value).toBe(false)

      await pollListFailures(1, { response: { status: 400 } })
      expect(rp.queueRefreshRefused.value).toBe(true)
      rp.stopQueueRefresh()
    })

    it('does not let an intervening transient failure clear a risen disclosure', async () => {
      const rp = await startedWithCurrentQueue()

      await pollListFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD, { response: { status: 400 } })
      expect(rp.queueRefreshRefused.value).toBe(true)

      // Symmetric to the #2445 ruling on the transient side: an interruption
      // resets the RUN, it does not prove the retained queue is current, so it
      // cannot take a standing disclosure off the screen.
      await pollListFailures(1, { response: { status: 500 } })
      expect(rp.queueRefreshRefused.value).toBe(true)
      rp.stopQueueRefresh()
    })

    it('clears on the next successful list read and announces the recovery', async () => {
      const rp = await startedWithCurrentQueue()

      await pollListFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD, { response: { status: 400 } })
      expect(rp.queueRefreshRefused.value).toBe(true)
      expect(rp.queueRefreshRecovered.value).toBe(false)

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'recovered' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshRefused.value).toBe(false)
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['recovered'])
      // Clearing the warning is silent on its own, exactly as it is for the
      // transient state (#2630): the recovery sentence is the announcement.
      expect(rp.queueRefreshRecovered.value).toBe(true)
      rp.stopQueueRefresh()
    })

    it('announces the retraction with the refusal sentence, not the queue sentence (#2638 item 2)', async () => {
      const rp = await startedWithCurrentQueue()

      await pollListFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD, { response: { status: 400 } })
      expect(rp.queueRefreshRefused.value).toBe(true)

      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'recovered' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      // The retraction is raised by the LIST leg, and the composite success
      // that follows it in the same read must not swap the sentence for the
      // queue one: this signal's job is to retract the refusal claim, and the
      // surfaces say only that refreshes are being accepted again.
      expect(rp.queueRefreshRecovered.value).toBe(true)
      expect(rp.queueRefreshRecoveredKind.value).toBe('refused')
      rp.stopQueueRefresh()
    })

    it('leaves the 403 authority path to its own owner', async () => {
      const rp = await startedWithCurrentQueue()

      await pollListFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD, { response: { status: 403 } })
      // 403 is revoked access, not a refused refresh: it clears the queue, says
      // so through `queueAccessRevoked`, and suspends the poll. Two owners for
      // one fact would render two contradictory panels.
      expect(rp.queueRefreshRefused.value).toBe(false)
      expect(rp.queueAccessRevoked.value).toBe(true)
      rp.stopQueueRefresh()
    })

    it('excludes 401, which belongs to the HTTP interceptor', async () => {
      const rp = await startedWithCurrentQueue()

      await pollListFailures(REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD, { response: { status: 401 } })
      // A 401 is the session ending; `api/http.ts` clears it and redirects to
      // login. Telling the reviewer to check the board filter on the way out
      // would be false.
      expect(rp.queueRefreshRefused.value).toBe(false)
      rp.stopQueueRefresh()
    })

    it('retracts a risen refusal as soon as the LIST read succeeds, even when the pin leg fails', async () => {
      // Review finding, round 2. `queueRefreshRefused` was cleared only by
      // `recordQueueRefreshSuccess`, which needs the WHOLE composite read; a
      // pin-leg failure returns before it. So once the API recovered but the
      // by-id read for a hash-pinned row kept failing, the surface went on
      // saying "the server is refusing the refresh rather than failing
      // temporarily" every tick, which was no longer true, and because the
      // refusal copy has precedence the honest degraded copy could never
      // appear. It survived until an explicit load.
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-pinned'
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-pinned' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      for (let failure = 0; failure < REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD; failure += 1) {
        mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 404 } })
        await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      }
      expect(rp.queueRefreshRefused.value).toBe(true)
      expect(rp.queueRefreshRecovered.value).toBe(false)

      // The list read answers again; only the pinned row's by-id read is down.
      const pinLegDown = async () => {
        mockAutomationApi.getProposals.mockResolvedValueOnce([])
        mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status: 500 } })
        await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      }
      await pinLegDown()

      // The claim is about the LIST read, and the list read demonstrably
      // succeeded, so the claim is retracted on that evidence alone.
      expect(rp.queueRefreshRefused.value).toBe(false)
      // And retracting it silently is the #2630 defect, so it announces once.
      expect(rp.queueRefreshRecovered.value).toBe(true)
      // The composite read still bailed, so the queue was NOT replaced and the
      // transient state is untouched by the retraction.
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-pinned'])
      expect(rp.queueRefreshStale.value).toBe(false)

      // The transient counter keeps its #2445 composite semantics: this pin-leg
      // 500 was its first, and two more take it to the threshold.
      await pinLegDown()
      expect(rp.queueRefreshStale.value).toBe(false)
      await pinLegDown()
      expect(rp.queueRefreshStale.value).toBe(true)
      // The degraded onset retires the recovery sentence, as it always has.
      expect(rp.queueRefreshRecovered.value).toBe(false)
      rp.stopQueueRefresh()
    })

    it('says nothing about the queue when the pin leg strands the composite read (#2638 item 2)', async () => {
      // The copy defect PR #2694's round-2 verification recorded on #2214. On a
      // list-success/pin-fail tick the composite read returns before
      // `proposals.value = next`, so the rows on screen are exactly the ones
      // that were there before -- and the shared #2630 sentence's second clause
      // ("Showing current proposals") stood for up to two further poll
      // intervals, because the next tick's list success returns early too and
      // only the degraded onset after it retires the sentence.
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-pinned'
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-pinned' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      for (let failure = 0; failure < REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD; failure += 1) {
        mockAutomationApi.getProposals.mockRejectedValueOnce({ response: { status: 404 } })
        await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      }
      expect(rp.queueRefreshRefused.value).toBe(true)

      // The list read answers again; only the pinned row's by-id read is down.
      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status: 500 } })
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      expect(rp.queueRefreshRefused.value).toBe(false)
      expect(rp.queueRefreshRecovered.value).toBe(true)
      // The kind is what the surfaces read to pick the sentence, and this is
      // the tick that proves why the two cannot share one: the queue was NOT
      // replaced.
      expect(rp.queueRefreshRecoveredKind.value).toBe('refused')
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-pinned'])

      // A LATER background success retires it, the same rule the queue sentence
      // follows. This tick's list carries the pinned row, so there is no by-id
      // leg and the composite read completes.
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-pinned' })])
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.queueRefreshRecovered.value).toBe(false)
      expect(rp.queueRefreshRecoveredKind.value).toBe(null)
      rp.stopQueueRefresh()
    })

    it('does not count a pin-leg failure, whose tick read the list successfully', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-pinned'
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-pinned' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      // The list answer arrives every tick; only the by-id re-authorization
      // read fails. The disclosure claims the LIST read is being refused, and
      // it demonstrably is not.
      for (let attempt = 0; attempt < REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD; attempt += 1) {
        mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'other' })])
        mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status: 405 } })
        await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      }
      expect(rp.queueRefreshRefused.value).toBe(false)
      rp.stopQueueRefresh()
    })
  })

  describe('malformed vs unavailable pin (#2214)', () => {
    /**
     * `unavailableProposalId` collapses two different truths. "This proposal is
     * no longer available to review; it may have been applied, archived, or
     * removed" is right for a 403 or a 404 and wrong for a 400: an id the by-id
     * route cannot bind never named a proposal at all, and no amount of waiting
     * or retrying will make the link work. The reason rides alongside the id so
     * both skins can say which one happened.
     */
    it('marks a pin the by-id route cannot bind as malformed', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-not-a-guid'
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'not-a-guid' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()
      expect(rp.unavailableProposalMalformed.value).toBe(false)

      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status: 400 } })
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      expect(rp.unavailableProposalId.value).toBe('not-a-guid')
      expect(rp.unavailableProposalMalformed.value).toBe(true)
      rp.stopQueueRefresh()
    })

    it.each([403, 404])('does not call a %s pin malformed', async (status) => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-pinned'
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-pinned' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status } })
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      // The link named a real proposal. Whether it is gone or forbidden, it is
      // not a broken address, and telling the reviewer their link is malformed
      // would send them to fix something that is correct.
      expect(rp.unavailableProposalId.value).toBe('p-pinned')
      expect(rp.unavailableProposalMalformed.value).toBe(false)
      rp.stopQueueRefresh()
    })

    it('does not call a wrong-identity or cross-scope answer malformed', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-pinned'
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'p-pinned' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockResolvedValueOnce(makeProposal({ id: 'p-different' }))
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      expect(rp.unavailableProposalId.value).toBe('p-pinned')
      expect(rp.unavailableProposalMalformed.value).toBe(false)
      rp.stopQueueRefresh()
    })

    it('retires the malformed reason with the pin it describes', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-not-a-guid'
      mockAutomationApi.getProposals.mockResolvedValueOnce([makeProposal({ id: 'not-a-guid' })])
      const rp = useReviewProposals()
      await rp.loadProposals()
      rp.startQueueRefresh()

      mockAutomationApi.getProposals.mockResolvedValueOnce([])
      mockAutomationApi.getProposal.mockRejectedValueOnce({ response: { status: 400 } })
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)
      expect(rp.unavailableProposalMalformed.value).toBe(true)
      rp.stopQueueRefresh()

      // A reason that outlived its id would label the NEXT unavailable pin.
      mockRoute.hash = ''
      await watcherForCurrentSourceValue('')[1]()
      expect(rp.unavailableProposalId.value).toBeNull()
      expect(rp.unavailableProposalMalformed.value).toBe(false)
    })
  })
})
