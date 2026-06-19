import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises, enableAutoUnmount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Proposal } from '../../../../types/automation'
import PaperReviewView from '../../../../views/paper/PaperReviewView.vue'

const mocks = vi.hoisted(() => ({
  getProposals: vi.fn(),
  getProposal: vi.fn(),
  approveProposal: vi.fn(),
  rejectProposal: vi.fn(),
  executeProposal: vi.fn(),
  getProposalDiff: vi.fn(),
  dismissProposals: vi.fn(),
  getProvenance: vi.fn(),
  getConfidence: vi.fn(),
  getSideEffects: vi.fn(),
  getConflicts: vi.fn(),
  getHistory: vi.fn(),
  getSimilarPast: vi.fn(),
  getBoards: vi.fn(),
  createRevision: vi.fn(),
  getRevisions: vi.fn(),
  getLatestRevision: vi.fn(),
  successToast: vi.fn(),
  errorToast: vi.fn(),
  infoToast: vi.fn(),
  sessionState: { userId: 'u-1' as string | null },
}))

vi.mock('../../../../api/automationApi', () => ({
  automationApi: {
    getProposals: mocks.getProposals,
    getProposal: mocks.getProposal,
    approveProposal: mocks.approveProposal,
    rejectProposal: mocks.rejectProposal,
    executeProposal: mocks.executeProposal,
    getProposalDiff: mocks.getProposalDiff,
    dismissProposals: mocks.dismissProposals,
  },
}))

vi.mock('../../../../api/boardsApi', () => ({
  boardsApi: { getBoards: mocks.getBoards },
}))

vi.mock('../../../../api/proposalDeepReviewApi', () => ({
  proposalDeepReviewApi: {
    getProvenance: mocks.getProvenance,
    getConfidence: mocks.getConfidence,
    getSideEffects: mocks.getSideEffects,
    getConflicts: mocks.getConflicts,
    getHistory: mocks.getHistory,
    getSimilarPast: mocks.getSimilarPast,
  },
}))

vi.mock('../../../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.successToast,
    error: mocks.errorToast,
    info: mocks.infoToast,
  }),
}))

vi.mock('../../../../store/sessionStore', () => ({
  useSessionStore: () => mocks.sessionState,
}))

vi.mock('../../../../api/proposalRevisionsApi', () => ({
  proposalRevisionsApi: {
    createRevision: mocks.createRevision,
    getRevisions: mocks.getRevisions,
    getLatestRevision: mocks.getLatestRevision,
  },
}))

vi.mock('../../../../api/proposalDeepReviewApi', () => ({
  proposalDeepReviewApi: {
    getProvenance: vi.fn().mockResolvedValue([]),
    getConfidence: vi.fn().mockResolvedValue({ overall: 0.8, components: [], note: null, threshold: 0.5, meetsThreshold: true }),
    getSideEffects: vi.fn().mockResolvedValue({ rows: [], reversibility: { summary: '', tone: 'safe' } }),
    getConflicts: vi.fn().mockResolvedValue([]),
    getHistory: vi.fn().mockResolvedValue([]),
    getSimilarPast: vi.fn().mockResolvedValue({ decisions: [], applyRate: 0 }),
  },
}))

function makeProposal(overrides: Partial<Proposal> = {}): Proposal {
  const now = new Date().toISOString()
  return {
    id: 'proposal-001',
    sourceType: 'Chat',
    sourceReferenceId: null,
    boardId: 'board-1',
    requestedByUserId: 'u-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: 'Split "dark mode" into 3 cards',
    diffPreview: null,
    validationIssues: null,
    createdAt: now,
    updatedAt: now,
    expiresAt: new Date(Date.now() + 60 * 60_000).toISOString(),
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'corr-1',
    operations: [
      {
        id: 'op-1',
        proposalId: 'proposal-001',
        sequence: 0,
        actionType: 'CreateCard',
        targetType: 'Card',
        targetId: null,
        parameters: '{}',
        idempotencyKey: 'k-1',
        expectedVersion: null,
      },
    ],
    ...overrides,
  }
}

async function mountView(proposals: Proposal[], path = '/workspace/review') {
  mocks.getProposals.mockResolvedValueOnce(proposals)
  mocks.getBoards.mockResolvedValueOnce([])
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/workspace/review', name: 'workspace-review', component: PaperReviewView }],
  })
  router.push(path)
  await router.isReady()

  const wrapper = mount(PaperReviewView, {
    global: {
      plugins: [router],
      stubs: {
        // Avoid PaperUndoTimeline triggering rAF/matchMedia paths in jsdom.
        PaperUndoTimeline: true,
      },
    },
  })
  await flushPromises()
  return wrapper
}

describe('PaperReviewView', () => {
  // Unmount every mounted wrapper after each test. PaperReviewView attaches a
  // window keydown listener (review keymap) and a 60s clock interval; without
  // teardown those leak across tests, so a keydown dispatched in one test
  // reaches leftover components from earlier tests (e.g. firing stray File
  // away dismissals). #1161 / #1128
  enableAutoUnmount(afterEach)

  beforeEach(() => {
    vi.clearAllMocks()
    mocks.sessionState.userId = 'u-1'
    mocks.getRevisions.mockResolvedValue([])
    mocks.getLatestRevision.mockResolvedValue(null)
    mocks.getProvenance.mockResolvedValue([])
    mocks.getConfidence.mockResolvedValue({
      overall: 0.84,
      components: [],
      note: null,
      threshold: 0.7,
      meetsThreshold: true,
    })
    mocks.getSideEffects.mockResolvedValue({
      rows: [],
      reversibility: {
        summary: '6 hours',
        description: 'Undo restores affected cards.',
        windowMs: 6 * 60 * 60 * 1000,
      },
    })
    mocks.getConflicts.mockResolvedValue([])
    mocks.getHistory.mockResolvedValue([])
    mocks.getSimilarPast.mockResolvedValue({ decisions: [], applyRate: 0 })
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('smoke renders the three rails with a stubbed proposal', async () => {
    const wrapper = await mountView([makeProposal()])
    expect(wrapper.find('[data-testid="paper-review-view"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-review-queue-rail"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-review-main"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-review-right-rail"]').exists()).toBe(true)
    // Active proposal title surfaces in the main column header.
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('dark mode')
  })

  it('renders the empty state when the queue is empty', async () => {
    const wrapper = await mountView([])
    expect(wrapper.find('[data-testid="paper-review-empty"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-review-main"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="paper-review-right-rail"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Nothing waiting')
  })

  it('emphasizes every quoted phrase in the proposal title', async () => {
    const wrapper = await mountView([
      makeProposal({ summary: 'Split "dark mode" and "QA pass" into cards' }),
    ])

    const emphasized = wrapper.find('[data-testid="paper-review-main"] h1').findAll('em')
    expect(emphasized.map((node) => node.text())).toEqual(['“dark mode”', '“QA pass”'])
  })

  it('renders proposal operation details instead of the demo change preview', async () => {
    const wrapper = await mountView([
      makeProposal({
        summary: 'Move invoice card',
        operations: [
          {
            id: 'op-move',
            proposalId: 'proposal-001',
            sequence: 0,
            actionType: 'MoveCard',
            targetType: 'Card',
            targetId: 'card-99',
            parameters: JSON.stringify({ columnId: 'done', position: 2 }),
            idempotencyKey: 'move-1',
            expectedVersion: null,
          },
        ],
      }),
    ])

    const mainText = wrapper.find('[data-testid="paper-review-main"]').text()
    expect(mainText).toContain('Move Card · Card')
    expect(mainText).toContain('columnId: done')
    expect(mainText).not.toContain('Implement dark mode')
    expect(mainText).not.toContain('No data left this device')

    const viewText = wrapper.text()
    expect(viewText).not.toContain('Haiku · local')
    expect(viewText).not.toContain('crossed your "split this" threshold')
  })

  it('uses proposal ownership for the Mine queue filter', async () => {
    const wrapper = await mountView([
      makeProposal({
        id: 'mine-001',
        requestedByUserId: 'u-1',
        summary: 'Mine proposal',
      }),
      makeProposal({
        id: 'theirs-001',
        requestedByUserId: 'u-2',
        summary: 'Theirs proposal',
      }),
    ])

    const mineButton = wrapper.findAll('button').find((button) => button.text() === 'Mine')
    await mineButton?.trigger('click')

    const railText = wrapper.find('[data-testid="paper-review-queue-rail"]').text()
    expect(railText).toContain('Mine proposal')
    expect(railText).not.toContain('Theirs proposal')
  })

  it('normalizes numeric chat source types for queue attribution', async () => {
    const wrapper = await mountView([
      makeProposal({
        sourceType: 1,
        summary: 'Numeric chat proposal',
      }),
    ])

    const railText = wrapper.find('[data-testid="paper-review-queue-rail"]').text()
    expect(railText).toContain('haiku')
    expect(railText).not.toContain('capture')
  })

  it('renders a filter-empty state when another queue filter still has work', async () => {
    const wrapper = await mountView([
      makeProposal({
        id: 'theirs-001',
        requestedByUserId: 'u-2',
        summary: 'Theirs proposal',
      }),
    ])

    const mineButton = wrapper.findAll('button').find((button) => button.text() === 'Mine')
    await mineButton?.trigger('click')

    const emptyText = wrapper.find('[data-testid="paper-review-empty"]').text()
    expect(emptyText).toContain('No matches in Mine.')
    expect(emptyText).toContain('Queue · 1 awaiting')
    expect(emptyText).not.toContain('Nothing waiting')
  })

  it('does not start the undo timeline for pending proposals', async () => {
    const wrapper = await mountView([makeProposal()])

    expect(wrapper.text()).toContain('Undo window starts after apply.')
    expect(wrapper.find('paper-undo-timeline-stub').exists()).toBe(false)
  })

  it('retargets decision actions to the visible proposal after queue filtering', async () => {
    mocks.approveProposal.mockResolvedValueOnce(makeProposal({ id: 'mine-001' }))
    const wrapper = await mountView([
      makeProposal({
        id: 'theirs-001',
        requestedByUserId: 'u-2',
        summary: 'Theirs proposal',
      }),
      makeProposal({
        id: 'mine-001',
        requestedByUserId: 'u-1',
        summary: 'Mine proposal',
      }),
    ])

    const mineButton = wrapper.findAll('button').find((button) => button.text() === 'Mine')
    await mineButton?.trigger('click')

    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Mine proposal')

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await flushPromises()

    expect(mocks.approveProposal).toHaveBeenCalledWith('mine-001')
  })

  it('uses the hash-targeted proposal as the active decision target', async () => {
    mocks.approveProposal.mockResolvedValueOnce(makeProposal({ id: 'proposal-target' }))
    const wrapper = await mountView(
      [
        makeProposal({ id: 'proposal-first', summary: 'First proposal' }),
        makeProposal({ id: 'proposal-target', summary: 'Target proposal' }),
      ],
      '/workspace/review#proposal-proposal-target',
    )

    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Target proposal')

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await flushPromises()

    expect(mocks.approveProposal).toHaveBeenCalledWith('proposal-target')
  })

  it('lets manual queue selection override a hash-targeted proposal', async () => {
    mocks.approveProposal.mockResolvedValueOnce(makeProposal({ id: 'proposal-first' }))
    const wrapper = await mountView(
      [
        makeProposal({ id: 'proposal-first', summary: 'First proposal' }),
        makeProposal({ id: 'proposal-target', summary: 'Target proposal' }),
      ],
      '/workspace/review#proposal-proposal-target',
    )

    await wrapper.findAll('.paper-review-q')[0].trigger('click')

    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('First proposal')

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await flushPromises()

    expect(mocks.approveProposal).toHaveBeenCalledWith('proposal-first')
  })

  it('retargets active proposal when the route hash changes', async () => {
    mocks.approveProposal.mockResolvedValueOnce(makeProposal({ id: 'proposal-target' }))
    const wrapper = await mountView(
      [
        makeProposal({ id: 'proposal-first', summary: 'First proposal' }),
        makeProposal({ id: 'proposal-target', summary: 'Target proposal' }),
      ],
      '/workspace/review#proposal-proposal-first',
    )

    await (wrapper.vm as unknown as {
      $router: { push: (path: string) => Promise<void> }
    }).$router.push('/workspace/review#proposal-proposal-target')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Target proposal')

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await flushPromises()

    expect(mocks.approveProposal).toHaveBeenCalledWith('proposal-target')
  })

  it('falls back to normal selection for malformed hash proposal ids', async () => {
    const wrapper = await mountView(
      [makeProposal({ id: 'proposal-first', summary: 'First proposal' })],
      '/workspace/review#proposal-%E0%A4%A',
    )

    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('First proposal')
  })

  it('shows recently applied proposals even when completed items are hidden from the queue', async () => {
    const olderAppliedAt = new Date(Date.now() - 90 * 60_000).toISOString()
    const newerAppliedAt = new Date(Date.now() - 30 * 60_000).toISOString()
    const wrapper = await mountView([
      makeProposal({ id: 'pending-001', summary: 'Pending work' }),
      makeProposal({
        id: 'applied-old',
        status: 'Applied',
        summary: 'Older applied work',
        appliedAt: olderAppliedAt,
      }),
      makeProposal({
        id: 'applied-new',
        status: 'Applied',
        summary: 'Newer applied work',
        appliedAt: newerAppliedAt,
      }),
    ])

    const railText = wrapper.find('[data-testid="paper-review-queue-rail"]').text()
    expect(railText).toContain('Pending work')
    expect(railText).toContain('Older applied work')
    expect(railText).toContain('Newer applied work')
    expect(railText.indexOf('Newer applied work')).toBeLessThan(railText.indexOf('Older applied work'))
  })

  it('replaces decision buttons with "File away" for an expired proposal', async () => {
    const wrapper = await mountView([
      makeProposal({
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        summary: 'Expired proposal',
      }),
    ])

    // A settled proposal can no longer be applied/rejected/edited/deferred, so
    // those buttons are gone entirely — the rail becomes a filing rail. #1161
    expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-reject"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-edit"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-defer"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-file-away"]').exists()).toBe(true)
  })

  it('files away an expired proposal when File away is clicked', async () => {
    mocks.dismissProposals.mockResolvedValueOnce({ dismissed: 1 })
    const wrapper = await mountView([
      makeProposal({
        id: 'expired-001',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        summary: 'Expired proposal',
      }),
    ])

    await wrapper.find('[data-testid="decision-file-away"]').trigger('click')
    await flushPromises()

    expect(mocks.dismissProposals).toHaveBeenCalledWith(['expired-001'])
    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.rejectProposal).not.toHaveBeenCalled()
  })

  it('files away a settled proposal with the ⌫ key', async () => {
    mocks.dismissProposals.mockResolvedValueOnce({ dismissed: 1 })
    const wrapper = await mountView([
      makeProposal({
        id: 'expired-002',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        summary: 'Expired proposal',
      }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Backspace', cancelable: true }))
    await flushPromises()

    expect(mocks.dismissProposals).toHaveBeenCalledWith(['expired-002'])
    expect(mocks.rejectProposal).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('shows a bulk "File away" action and clears every settled proposal on click', async () => {
    mocks.dismissProposals.mockResolvedValueOnce({ dismissed: 2 })
    const wrapper = await mountView([
      makeProposal({
        id: 'expired-a',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        summary: 'Expired A',
      }),
      makeProposal({
        id: 'expired-b',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 120_000).toISOString(),
        summary: 'Expired B',
      }),
    ])

    const bulk = wrapper.find('[data-testid="queue-file-away-all"]')
    expect(bulk.exists()).toBe(true)
    expect(bulk.text()).toContain('File away 2 settled')

    await bulk.trigger('click')
    await flushPromises()

    expect(mocks.dismissProposals).toHaveBeenCalledWith(['expired-a', 'expired-b'])
  })

  it('shows the bulk "File away" action for a single settled proposal so it is never unclearable', async () => {
    // A single hidden settled proposal (e.g. Applied) has no per-proposal rail
    // in Paper, so the bulk affordance must appear at count 1. #1161 (review)
    const wrapper = await mountView([
      makeProposal({
        id: 'expired-only',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        summary: 'Lone expired',
      }),
    ])

    const bulk = wrapper.find('[data-testid="queue-file-away-all"]')
    expect(bulk.exists()).toBe(true)
    expect(bulk.text()).toContain('File away 1 settled')
  })

  it('hides the bulk "File away" action when there are no settled proposals', async () => {
    const wrapper = await mountView([
      makeProposal({ id: 'pending-only', status: 'PendingReview', summary: 'Still pending' }),
    ])

    expect(wrapper.find('[data-testid="queue-file-away-all"]').exists()).toBe(false)
  })

  it('omits collaborator-owned settled proposals from the bulk file-away set (avoids a 403)', async () => {
    // The dismiss endpoint 403s the whole request if any id is not owned by the
    // caller, so a board-filtered queue with another user's settled proposal must
    // not include it in the bulk set. #1161 (review)
    const wrapper = await mountView([
      makeProposal({
        id: 'mine-expired',
        status: 'Expired',
        requestedByUserId: 'u-1',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        summary: 'My expired',
      }),
      makeProposal({
        id: 'theirs-expired',
        status: 'Expired',
        requestedByUserId: 'u-2',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        summary: 'Their expired',
      }),
    ])

    // Only the caller's own settled proposal counts → "1 settled", not 2.
    const bulk = wrapper.find('[data-testid="queue-file-away-all"]')
    expect(bulk.exists()).toBe(true)
    expect(bulk.text()).toContain('File away 1 settled')
  })

  it('treats an Approved-then-expired proposal as dismissable (File away)', async () => {
    const wrapper = await mountView([
      makeProposal({
        id: 'approved-expired',
        status: 'Approved',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        summary: 'Approved but expired before execution',
      }),
    ])

    expect(wrapper.find('[data-testid="decision-file-away"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(false)
  })

  it('rejects (not files away) with the ⌫ key when the proposal is still actionable', async () => {
    const promptSpy = vi.spyOn(window, 'prompt').mockReturnValue('')
    mocks.rejectProposal.mockResolvedValueOnce(makeProposal({ id: 'pending-xyz', status: 'Rejected' }))
    const wrapper = await mountView([
      makeProposal({ id: 'pending-xyz', status: 'PendingReview', summary: 'Still actionable' }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Backspace', cancelable: true }))
    await flushPromises()

    expect(mocks.rejectProposal).toHaveBeenCalled()
    expect(mocks.dismissProposals).not.toHaveBeenCalled()

    promptSpy.mockRestore()
    wrapper.unmount()
  })

  it('swaps the decision rail to "File away" when a focused proposal expires on the clock tick', async () => {
    vi.useFakeTimers()
    try {
      const base = new Date('2026-06-13T12:00:00.000Z').getTime()
      vi.setSystemTime(base)
      mocks.dismissProposals.mockResolvedValueOnce({ dismissed: 1 })

      const wrapper = await mountView([
        makeProposal({
          id: 'approved-soon',
          status: 'Approved',
          // Live now (base), but expires in 30s — before the first 60s tick.
          expiresAt: new Date(base + 30_000).toISOString(),
          summary: 'Approved, expiring shortly',
        }),
      ])

      // Still actionable at mount: decision buttons present, no File away.
      expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="decision-file-away"]').exists()).toBe(false)

      // One 60s clock tick advances nowMs past expiry; the rail must swap
      // reactively without a reload (#1161).
      await vi.advanceTimersByTimeAsync(60_000)
      await flushPromises()
      await wrapper.vm.$nextTick()

      expect(wrapper.find('[data-testid="decision-file-away"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(false)

      await wrapper.find('[data-testid="decision-file-away"]').trigger('click')
      await flushPromises()
      expect(mocks.dismissProposals).toHaveBeenCalledWith(['approved-soon'])

      wrapper.unmount()
    } finally {
      vi.useRealTimers()
    }
  })

  it('surfaces feedback when defer is invoked before backend support exists', async () => {
    const wrapper = await mountView([makeProposal()])

    await wrapper.find('[data-testid="decision-defer"]').trigger('click')

    expect(mocks.infoToast).toHaveBeenCalledWith(
      'Defer is not wired yet; the proposal is still in your queue.',
    )
  })

  it('does not send reject transitions for already approved proposals', async () => {
    const wrapper = await mountView([makeProposal({ status: 'Approved' })])

    await wrapper.find('[data-testid="decision-reject"]').trigger('click')
    await flushPromises()

    expect(mocks.rejectProposal).not.toHaveBeenCalled()
    expect(mocks.infoToast).toHaveBeenCalledWith(
      'This proposal can no longer be rejected. Refresh review to see current status.',
    )
  })

  it('opens the revision editor when request edit is clicked', async () => {
    const wrapper = await mountView([makeProposal()])

    expect(wrapper.find('[data-testid="revision-editor"]').exists()).toBe(false)

    await wrapper.find('[data-testid="decision-edit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="revision-editor"]').exists()).toBe(true)
  })

  it('opens revision editing for multi-operation proposals with all operations present', async () => {
    const wrapper = await mountView([
      makeProposal({
        operations: [
          {
            id: 'op-1',
            proposalId: 'proposal-001',
            sequence: 2,
            actionType: 'MoveCard',
            targetType: 'Card',
            targetId: 'card-1',
            parameters: '{"columnId":"done"}',
            idempotencyKey: 'k-2',
            expectedVersion: 7,
          },
          {
            id: 'op-2',
            proposalId: 'proposal-001',
            sequence: 1,
            actionType: 'CreateCard',
            targetType: 'Card',
            targetId: null,
            parameters: '{"title":"Draft"}',
            idempotencyKey: 'k-1',
            expectedVersion: null,
          },
        ],
      }),
    ])

    await wrapper.find('[data-testid="decision-edit"]').trigger('click')
    await flushPromises()

    const operationsField = wrapper.get('[data-testid="revision-field-operations"]')
    const value = JSON.parse((operationsField.element as HTMLTextAreaElement).value)

    expect(value).toHaveLength(2)
    expect(value.map((operation: { sequence: number }) => operation.sequence)).toEqual([1, 2])
  })

  it('disables apply and reject while a revision edit is open', async () => {
    const wrapper = await mountView([makeProposal()])

    await wrapper.find('[data-testid="decision-edit"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="decision-apply"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="decision-reject"]').attributes('disabled')).toBeDefined()

    await wrapper.get('[data-testid="decision-apply"]').trigger('click')
    await wrapper.get('[data-testid="decision-reject"]').trigger('click')
    await flushPromises()

    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.rejectProposal).not.toHaveBeenCalled()
  })

  it('surfaces feedback when provenance toggle is invoked before collapsible mode exists', async () => {
    const wrapper = await mountView([makeProposal()])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'p', cancelable: true }))
    await flushPromises()

    expect(mocks.infoToast).toHaveBeenCalledWith(
      'Provenance toggle is not wired yet; provenance is rendered inline below.',
    )

    wrapper.unmount()
  })

  it('surfaces feedback when the provenance report action is clicked', async () => {
    const wrapper = await mountView([makeProposal()])

    await wrapper.get('.paper-review-prov__more').trigger('click')
    await wrapper.vm.$nextTick()
    const reportButton = document.body.querySelector('.prov-drawer__action--report') as HTMLButtonElement
    await reportButton.click()

    expect(mocks.infoToast).toHaveBeenCalledWith('Report queued for this suggestion.')
  })

  it('loads and renders the proposal diff inline when preview diff is invoked', async () => {
    mocks.getProposalDiff.mockResolvedValueOnce('--- before\n+++ after\n+Add column "Done"')
    const wrapper = await mountView([makeProposal({ id: 'diff-001' })])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposalDiff).toHaveBeenCalledWith('diff-001')
    const diffPre = wrapper.find('[data-testid="paper-review-diff-pre"]')
    expect(diffPre.exists()).toBe(true)
    expect(diffPre.text()).toContain('Add column "Done"')
    // No stub toast — the diff is wired now.
    expect(mocks.infoToast).not.toHaveBeenCalledWith(
      'Preview diff is not wired yet; no diff was loaded.',
    )

    wrapper.unmount()
  })

  it('hides the inline diff when preview diff is invoked again for the same proposal', async () => {
    mocks.getProposalDiff.mockResolvedValueOnce('--- before\n+++ after\n+Add column "Done"')
    const wrapper = await mountView([makeProposal({ id: 'diff-002' })])

    // First press: load + render the diff.
    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()
    expect(wrapper.find('[data-testid="paper-review-diff-pre"]').exists()).toBe(true)
    expect(mocks.getProposalDiff).toHaveBeenCalledTimes(1)

    // Second press: toggle the diff off (no re-fetch).
    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()
    expect(wrapper.find('[data-testid="paper-review-diff"]').exists()).toBe(false)
    expect(mocks.getProposalDiff).toHaveBeenCalledTimes(1)

    wrapper.unmount()
  })

  it('renders an empty-diff state when the proposal has no changes to preview', async () => {
    mocks.getProposalDiff.mockResolvedValueOnce('')
    const wrapper = await mountView([makeProposal({ id: 'diff-empty' })])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposalDiff).toHaveBeenCalledWith('diff-empty')
    expect(wrapper.find('[data-testid="paper-review-diff-pre"]').exists()).toBe(false)
    const empty = wrapper.find('[data-testid="paper-review-diff-empty"]')
    expect(empty.exists()).toBe(true)
    expect(empty.text()).toContain('No changes to preview')

    wrapper.unmount()
  })

  it('surfaces a toast and does not crash when the diff request fails', async () => {
    mocks.getProposalDiff.mockRejectedValueOnce(new Error('boom'))
    const wrapper = await mountView([makeProposal({ id: 'diff-err' })])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposalDiff).toHaveBeenCalledWith('diff-err')
    expect(mocks.errorToast).toHaveBeenCalledWith('boom')
    // The inline diff surface is torn back down on error — no stale region.
    expect(wrapper.find('[data-testid="paper-review-diff"]').exists()).toBe(false)
    // The view is still mounted and interactive.
    expect(wrapper.find('[data-testid="paper-review-view"]').exists()).toBe(true)

    wrapper.unmount()
  })

  it('shows the empty-diff state without fetching for a no-operation proposal', async () => {
    const wrapper = await mountView([
      makeProposal({ id: 'diff-noop', diffPreview: null, operations: [] }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    // No diffPreview + no operations → the backend `/diff` would 404, so the view
    // shows the empty state directly without firing the request.
    expect(mocks.getProposalDiff).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="paper-review-diff-empty"]').exists()).toBe(true)

    wrapper.unmount()
  })

  it('shows a revision caveat in the diff preview when a saved revision exists', async () => {
    const now = new Date().toISOString()
    mocks.getRevisions.mockResolvedValue([
      {
        id: 'rev-1',
        proposalId: 'diff-rev',
        revisionNumber: 1,
        editorUserId: 'u-1',
        revisedPayload: '{"operations":[]}',
        revisedAt: now,
        reason: 'edit',
        createdAt: now,
      },
    ])
    mocks.getProposalDiff.mockResolvedValueOnce('--- before\n+++ after\n+x')
    const wrapper = await mountView([makeProposal({ id: 'diff-rev' })])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(wrapper.find('[data-testid="paper-review-diff-pre"]').exists()).toBe(true)
    // The preview reflects the ORIGINAL proposal, so a pending saved revision must
    // be flagged (the diff does not reflect the revision that Apply will run).
    const caveat = wrapper.find('[data-testid="paper-review-diff-revision-caveat"]')
    expect(caveat.exists()).toBe(true)
    expect(caveat.text()).toContain('original')

    wrapper.unmount()
  })
})
