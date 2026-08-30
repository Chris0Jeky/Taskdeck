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
  const watcher = watchers.find(([source]) => typeof source === 'function' && (source as () => unknown)() === expected)
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

    it('keeps the deep-linked proposal at its own position when the page omits it', async () => {
      vi.useFakeTimers()
      mockRoute.hash = '#proposal-p-open'
      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-open', createdAt: '2026-01-02T00:00:00Z' }),
      ])
      const rp = useReviewProposals()
      await rp.loadProposals()
      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-open'])

      // The record the URL names has dropped out of the list page. It is fetched
      // by id, so a background refresh must not evict the proposal the reviewer
      // is actually looking at -- and must not relegate it either: the rail
      // renders array order, so appending would make the record being reviewed
      // jump to the bottom on the first tick.
      mockAutomationApi.getProposals.mockResolvedValueOnce([
        makeProposal({ id: 'p-newer', createdAt: '2026-01-03T00:00:00Z' }),
        makeProposal({ id: 'p-older', createdAt: '2026-01-01T00:00:00Z' }),
      ])
      rp.startQueueRefresh()
      await vi.advanceTimersByTimeAsync(REVIEW_QUEUE_REFRESH_MS)

      expect(rp.proposals.value.map((p: any) => p.id)).toEqual(['p-newer', 'p-open', 'p-older'])
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
})
