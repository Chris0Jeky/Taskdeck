import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'

const {
  watchers,
  mockRouter,
  mockRoute,
  mockAutomationApi,
  mockBoardsApi,
  mockToast,
} = vi.hoisted(() => {
  return {
    watchers: [] as Array<[unknown, () => void]>,
    mockRouter: { push: vi.fn(), replace: vi.fn() },
    mockRoute: { hash: '', query: {} as Record<string, string> },
    mockAutomationApi: {
      getProposals: vi.fn(() => Promise.resolve([])),
      getProposal: vi.fn(),
    },
    mockBoardsApi: {
      getBoards: vi.fn(() => Promise.resolve([])),
    },
    mockToast: { error: vi.fn(), info: vi.fn() },
  }
})

vi.mock('vue', () => ({
  ref: (v: unknown) => ({ value: v }),
  computed: (fn: () => unknown) => ({ get value() { return fn() } }),
  watch: (source: unknown, cb: () => void) => { watchers.push([source, cb]) },
  nextTick: vi.fn(() => Promise.resolve()),
}))

vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
  useRouter: () => mockRouter,
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

import { useReviewProposals } from '../../composables/useReviewProposals'

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
      // watchers[0] = watch(() => route.hash, ...) — first function-sourced watcher
      mockRoute.hash = '#proposal-p-new'
      mockAutomationApi.getProposal.mockResolvedValueOnce(makeProposal({ id: 'p-new' }))
      await watchers[0][1]()
      expect(mockAutomationApi.getProposal).toHaveBeenCalledWith('p-new')
    })

    it('activeBoardFilter watcher triggers loadProposals', async () => {
      const rp = useReviewProposals()
      mockAutomationApi.getProposals.mockClear()
      // watchers[1] = watch(() => activeBoardFilter.value, ...)
      await watchers[1][1]()
      expect(mockAutomationApi.getProposals).toHaveBeenCalled()
    })
  })
})
