import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { mount, flushPromises, enableAutoUnmount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Proposal } from '../../../../types/automation'
import PaperReviewView from '../../../../views/paper/PaperReviewView.vue'
import ReviewRevisionEditor from '../../../../views/paper/review/ReviewRevisionEditor.vue'
import { resetProposalDisplayNamesForTests } from '../../../../composables/useProposalDisplayNames'

const mocks = vi.hoisted(() => ({
  getProposals: vi.fn(),
  getProposal: vi.fn(),
  approveProposal: vi.fn(),
  rejectProposal: vi.fn(),
  deferProposal: vi.fn(),
  executeProposal: vi.fn(),
  getProposalDiff: vi.fn(),
  dismissProposals: vi.fn(),
  reportBadSuggestion: vi.fn(),
  getProvenance: vi.fn(),
  getConfidence: vi.fn(),
  getSideEffects: vi.fn(),
  getConflicts: vi.fn(),
  getHistory: vi.fn(),
  getSimilarPast: vi.fn(),
  getBoards: vi.fn(),
  getColumns: vi.fn(),
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
    deferProposal: mocks.deferProposal,
    executeProposal: mocks.executeProposal,
    getProposalDiff: mocks.getProposalDiff,
    dismissProposals: mocks.dismissProposals,
    reportBadSuggestion: mocks.reportBadSuggestion,
  },
}))

vi.mock('../../../../api/boardsApi', () => ({
  boardsApi: { getBoards: mocks.getBoards },
}))

vi.mock('../../../../api/columnsApi', () => ({
  columnsApi: { getColumns: mocks.getColumns },
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
    getSideEffects: vi.fn().mockResolvedValue({
      rows: [],
      reversibility: {
        summary: 'Low risk · confirm before apply',
        description: 'Confirm affected items.',
        windowMs: 6 * 60 * 60 * 1000,
      },
    }),
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
    approvedRevisionId: null,
    ...overrides,
  }
}

async function mountView(
  proposals: Proposal[],
  path = '/workspace/review',
  boards: unknown[] = [],
  columns: unknown[] = [],
  // `document.activeElement` only tracks elements that are in the document, so
  // the focus specs need a real attachment; everything else mounts detached.
  options: { attachTo?: boolean } = {},
) {
  mocks.getProposals.mockResolvedValueOnce(proposals)
  mocks.getBoards.mockResolvedValueOnce(boards)
  mocks.getColumns.mockResolvedValue(columns)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/workspace/review', name: 'workspace-review', component: PaperReviewView }],
  })
  router.push(path)
  await router.isReady()

  const wrapper = mount(PaperReviewView, {
    global: {
      plugins: [router],
    },
    ...(options.attachTo ? { attachTo: document.body } : {}),
  })
  await flushPromises()
  return wrapper
}

/**
 * Type an optional reason into the GH-1969 reject dialog and accept it. Also
 * teleported to <body>, and hard-asserting for the same reason as the apply
 * gate below: a reject that skipped its collector must fail here.
 */
async function acceptRejectDialog(reason?: string) {
  if (reason !== undefined) {
    const field = document.body.querySelector(
      '[data-testid="reject-dialog-reason"]',
    ) as HTMLTextAreaElement | null
    expect(field, 'expected the reject reason dialog to be open (GH-1969)').not.toBeNull()
    field!.value = reason
    field!.dispatchEvent(new Event('input'))
    await flushPromises()
  }
  const accept = document.body.querySelector(
    '[data-testid="reject-dialog-accept"]',
  ) as HTMLButtonElement | null
  expect(accept, 'expected the reject reason dialog to be open (GH-1969)').not.toBeNull()
  accept!.click()
  await flushPromises()
}

/**
 * Accept the #1818 phase-2 confirmation dialog. It is a TdDialog teleported to
 * <body>, so it is not inside the wrapper's own tree. Hard-asserts the dialog is
 * present: if the confirmation gate were ever removed, executeProposal would
 * still be called and a fire-and-forget helper would keep the test green.
 */
async function confirmApplyDialog() {
  const accept = document.body.querySelector(
    '[data-testid="apply-confirm-accept"]',
  ) as HTMLButtonElement | null
  expect(
    accept,
    'expected the apply-to-board confirmation dialog to be open (#1818 phase-2 gate)',
  ).not.toBeNull()
  accept!.click()
  await flushPromises()
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
    resetProposalDisplayNamesForTests()
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
        summary: 'Low risk · confirm before apply',
        description: 'Confirm affected items before applying.',
        windowMs: 6 * 60 * 60 * 1000,
      },
    })
    mocks.getConflicts.mockResolvedValue([])
    mocks.getHistory.mockResolvedValue([])
    mocks.getSimilarPast.mockResolvedValue({ decisions: [], applyRate: 0 })
    mocks.getColumns.mockResolvedValue([])
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

  it('discloses a board-scoped empty queue and clears it back to the loaded proposal', async () => {
    const otherBoardProposal = makeProposal({ id: 'other-board', boardId: 'board-2' })
    mocks.getProposals.mockResolvedValue([otherBoardProposal])
    const wrapper = await mountView(
      [otherBoardProposal],
      '/workspace/review?boardId=board-1',
      [{ id: 'board-1', name: 'Payments API Migration' }],
    )

    const scope = wrapper.find('[data-testid="paper-scope-disclosure"]')
    expect(scope.text()).toContain('Board: Payments API Migration')
    expect(wrapper.find('[data-testid="paper-review-queue-rail"]').text()).toContain('0 awaiting in this board')
    expect(wrapper.find('[data-testid="paper-review-empty"]').text()).toContain('No proposals in Board: Payments API Migration')

    await wrapper.find('[data-testid="paper-review-clear-scope"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="paper-review-empty"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="paper-scope-disclosure"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Split "dark mode" into 3 cards')
  })

  it('orders Paper queue rows by risk while preserving hash selection and manual actions', async () => {
    const proposals = [
      makeProposal({ id: 'critical', riskLevel: 'Critical', summary: 'Critical proposal' }),
      makeProposal({ id: 'low', riskLevel: 'Low', summary: 'Low proposal' }),
      makeProposal({ id: 'high', riskLevel: 'High', summary: 'High proposal' }),
      makeProposal({ id: 'medium', riskLevel: 'Medium', summary: 'Medium proposal' }),
    ]
    const originalOrder = proposals.map((proposal) => proposal.id)
    const wrapper = await mountView(proposals, '/workspace/review#proposal-critical')

    expect(
      wrapper.findAll('.paper-review-q').map((row) => row.find('.paper-review-q__title').text()),
    ).toEqual(['Low proposal', 'Medium proposal', 'High proposal', 'Critical proposal'])
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Critical proposal')
    expect(wrapper.find('[data-testid="paper-review-risk-order-note"]').text()).toContain(
      'Risk order: Low, Medium, High, Critical',
    )
    expect(proposals.map((proposal) => proposal.id)).toEqual(originalOrder)
    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.executeProposal).not.toHaveBeenCalled()
  })

  it('renders a malformed-risk proposal and keeps it last in the Paper queue', async () => {
    const wrapper = await mountView(
      [
        makeProposal({ id: 'malformed', riskLevel: null as any, summary: 'Malformed proposal' }),
        makeProposal({ id: 'low', riskLevel: 'Low', summary: 'Low proposal' }),
      ],
      '/workspace/review#proposal-malformed',
    )

    expect(
      wrapper.findAll('.paper-review-q').map((row) => row.find('.paper-review-q__title').text()),
    ).toEqual(['Low proposal', 'Malformed proposal'])
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Malformed proposal')
    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.executeProposal).not.toHaveBeenCalled()
  })

  it('applies risk ordering after Mine and Stale filters', async () => {
    const staleAt = new Date(Date.now() - 25 * 60 * 60_000).toISOString()
    const wrapper = await mountView([
      makeProposal({
        id: 'mine-high',
        requestedByUserId: 'u-1',
        riskLevel: 'High',
        summary: 'Mine high',
        createdAt: staleAt,
      }),
      makeProposal({
        id: 'mine-low',
        requestedByUserId: 'u-1',
        riskLevel: 'Low',
        summary: 'Mine low',
      }),
      makeProposal({
        id: 'theirs-medium',
        requestedByUserId: 'u-2',
        riskLevel: 'Medium',
        summary: 'Theirs medium',
        createdAt: staleAt,
      }),
    ])

    const mineButton = wrapper.findAll('button').find((button) => button.text() === 'Mine')
    await mineButton?.trigger('click')
    await flushPromises()
    expect(wrapper.findAll('.paper-review-q').map((row) => row.find('.paper-review-q__title').text())).toEqual([
      'Mine low',
      'Mine high',
    ])

    const staleButton = wrapper.findAll('button').find((button) => button.text() === 'Stale')
    await staleButton?.trigger('click')
    expect(wrapper.findAll('.paper-review-q').map((row) => row.find('.paper-review-q__title').text())).toEqual([
      'Theirs medium',
      'Mine high',
    ])
  })

  it('selects the first actionable proposal after a filter removes the current selection', async () => {
    const wrapper = await mountView(
      [
        makeProposal({
          id: 'expired-mine',
          requestedByUserId: 'u-1',
          status: 'Expired',
          riskLevel: 'Low',
          summary: 'Expired mine',
          expiresAt: new Date(Date.now() - 60_000).toISOString(),
        }),
        makeProposal({
          id: 'pending-mine',
          requestedByUserId: 'u-1',
          riskLevel: 'High',
          summary: 'Pending mine',
        }),
        makeProposal({
          id: 'pending-theirs',
          requestedByUserId: 'u-2',
          riskLevel: 'Low',
          summary: 'Pending theirs',
        }),
      ],
      '/workspace/review#proposal-pending-theirs',
    )

    const mineButton = wrapper.findAll('button').find((button) => button.text() === 'Mine')
    await mineButton?.trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Pending mine')
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).not.toContain('Expired mine')
  })

  it('preserves the displayed fallback after removal when a later filter still contains it', async () => {
    mocks.dismissProposals.mockResolvedValueOnce({ dismissed: 1 })
    const wrapper = await mountView(
      [
        makeProposal({
          id: 'removed-expired',
          requestedByUserId: 'u-1',
          status: 'Expired',
          riskLevel: 'Medium',
          summary: 'Removed expired',
          expiresAt: new Date(Date.now() - 60_000).toISOString(),
        }),
        makeProposal({
          id: 'displayed-fallback',
          requestedByUserId: 'u-1',
          riskLevel: 'High',
          summary: 'Displayed fallback',
          createdAt: new Date(Date.now() - 25 * 60 * 60_000).toISOString(),
        }),
        makeProposal({
          id: 'stale-preferred',
          requestedByUserId: 'u-2',
          riskLevel: 'Low',
          summary: 'Stale preferred',
          createdAt: new Date(Date.now() - 25 * 60 * 60_000).toISOString(),
        }),
      ],
      '/workspace/review#proposal-removed-expired',
    )

    await wrapper.findAll('button').find((button) => button.text() === 'Mine')?.trigger('click')
    await wrapper.find('[data-testid="decision-file-away"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Displayed fallback')

    await wrapper.findAll('button').find((button) => button.text() === 'Stale')?.trigger('click')

    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Displayed fallback')
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).not.toContain('Stale preferred')
  })

  it('preserves a manually displayed proposal when the next filter still contains it', async () => {
    const wrapper = await mountView([
      makeProposal({
        id: 'low-proposal',
        requestedByUserId: 'u-2',
        riskLevel: 'Low',
        summary: 'Low proposal',
        createdAt: new Date(Date.now() - 25 * 60 * 60_000).toISOString(),
      }),
      makeProposal({
        id: 'selected-proposal',
        requestedByUserId: 'u-1',
        riskLevel: 'High',
        summary: 'Selected proposal',
        createdAt: new Date(Date.now() - 25 * 60 * 60_000).toISOString(),
      }),
    ])

    await wrapper.findAll('.paper-review-q')[1].trigger('click')
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Selected proposal')

    await wrapper.findAll('button').find((button) => button.text() === 'Stale')?.trigger('click')

    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Selected proposal')
  })

  it('falls back to the first read-only proposal when a filter has no actionable items', async () => {
    const wrapper = await mountView(
      [
        makeProposal({
          id: 'expired-mine',
          requestedByUserId: 'u-1',
          status: 'Expired',
          summary: 'Expired mine',
          expiresAt: new Date(Date.now() - 60_000).toISOString(),
          diffPreview: 'Stored expired preview',
        }),
        makeProposal({
          id: 'pending-theirs',
          requestedByUserId: 'u-2',
          summary: 'Pending theirs',
        }),
      ],
      '/workspace/review#proposal-pending-theirs',
    )

    const mineButton = wrapper.findAll('button').find((button) => button.text() === 'Mine')
    await mineButton?.trigger('click')
    await flushPromises()

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    const main = wrapper.find('[data-testid="paper-review-main"]')
    expect(main.text()).toContain('Expired mine')
    expect(wrapper.find('[data-testid="paper-review-diff-banner"]').text()).toContain('read-only')
  })

  it('renders the empty state when the queue is empty', async () => {
    const wrapper = await mountView([])
    expect(wrapper.find('[data-testid="paper-review-empty"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-review-main"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="paper-review-right-rail"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Nothing waiting')
    // The empty state describes the actor model-neutrally, never a persona (#1767).
    expect(wrapper.text()).toContain('When the assistant has something to propose')
    expect(wrapper.text().toLowerCase()).not.toContain('haiku')
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
    expect(mainText).toContain('columnId: Unavailable column')
    expect(mainText).not.toContain('Implement dark mode')
    expect(mainText).not.toContain('No data left this device')

    const viewText = wrapper.text()
    // No user-facing surface may name a specific LLM model or persona (#1767).
    expect(viewText.toLowerCase()).not.toContain('haiku')
    expect(viewText).not.toContain('crossed your "split this" threshold')
  })

  it('uses accessible board and column names while keeping IDs in technical details', async () => {
    const proposal = makeProposal({
      operations: [
        {
          id: 'op-move',
          proposalId: 'proposal-001',
          sequence: 0,
          actionType: 'MoveCard',
          targetType: 'Card',
          targetId: 'card-99',
          parameters: JSON.stringify({ boardId: 'board-1', columnId: 'column-1', position: 2 }),
          idempotencyKey: 'move-1',
          expectedVersion: null,
        },
      ],
    })
    const originalOperations = JSON.parse(JSON.stringify(proposal.operations))
    const wrapper = await mountView(
      [proposal],
      '/workspace/review',
      [{ id: 'board-1', name: 'Support Triage' }],
      [{ id: 'column-1', boardId: 'board-1', name: 'Done' }],
    )

    const mainText = wrapper.find('[data-testid="paper-review-main"]').text()
    expect(mainText).toContain('Support Triage')
    expect(mainText).toContain('Done')
    expect(mainText).not.toContain('board-1')
    expect(mainText).not.toContain('column-1')
    expect(mocks.getBoards).toHaveBeenCalledTimes(1)
    expect(mocks.getColumns).toHaveBeenCalledTimes(1)
    expect(proposal.operations).toEqual(originalOperations)

    const details = wrapper.find('[data-testid="paper-review-technical-details"]')
    expect(details.attributes('open')).toBeUndefined()
    await details.find('summary').trigger('click')
    expect(details.text()).toContain('board-1')
    expect(details.text()).toContain('column-1')
  })

  it('uses a neutral fallback for an inaccessible board or column', async () => {
    const wrapper = await mountView([
      makeProposal({
        operations: [{
          id: 'op-column',
          proposalId: 'proposal-001',
          sequence: 0,
          actionType: 'MoveCard',
          targetType: 'Column',
          targetId: 'column-missing',
          parameters: '{}',
          idempotencyKey: 'move-1',
          expectedVersion: null,
        }],
      }),
    ])

    const mainText = wrapper.find('[data-testid="paper-review-main"]').text()
    expect(mainText).toContain('Unavailable column')
    expect(mainText).not.toContain('column-missing')
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
    expect(railText).toContain('assistant')
    expect(railText.toLowerCase()).not.toContain('haiku')
    expect(railText).not.toContain('capture')
    // The author card must make the same actor split as the rail (#1767 review).
    expect(wrapper.text()).toContain('Assistant · chat proposal')
  })

  it('attributes non-chat proposals to Capture, not the assistant (#1767)', async () => {
    const wrapper = await mountView([
      makeProposal({
        sourceType: 'Queue',
        summary: 'Queue-sourced proposal',
      }),
    ])

    const viewText = wrapper.text()
    expect(viewText).toContain('Capture · queue proposal')
    expect(viewText).not.toContain('Assistant · queue proposal')
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

  it('clears a manual hash selection when a filter has no matching proposals', async () => {
    const wrapper = await mountView([
      makeProposal({
        id: 'theirs-001',
        requestedByUserId: 'u-2',
        summary: 'Theirs proposal',
      }),
    ])

    await wrapper.find('.paper-review-q').trigger('click')
    await flushPromises()
    expect((wrapper.vm as unknown as { $route: { hash: string } }).$route.hash).toBe(
      '#proposal-theirs-001',
    )

    await wrapper.findAll('button').find((button) => button.text() === 'Mine')?.trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="paper-review-empty"]').text()).toContain('No matches in Mine.')
    expect(wrapper.find('[data-testid="paper-review-main"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(false)
    expect((wrapper.vm as unknown as { $route: { hash: string } }).$route.hash).toBe('')
  })

  it('shows apply-risk guidance without promising an undo action', async () => {
    const wrapper = await mountView([makeProposal()])

    const posture = wrapper.get('[data-testid="apply-risk-posture"]')
    expect(posture.text()).toContain('Apply considerations')
    expect(posture.text()).toContain('Low risk · confirm before apply')
    expect(posture.text().toLowerCase()).not.toContain('undo')
    expect(posture.text().toLowerCase()).not.toContain('reversib')
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

  it('hydrates a mixed-case hash and actions the canonical Paper proposal id', async () => {
    const canonicalTarget = makeProposal({
      id: 'proposal-target',
      summary: 'Target proposal',
    })
    mocks.getProposal.mockResolvedValueOnce(canonicalTarget)
    mocks.approveProposal.mockResolvedValueOnce(
      makeProposal({ id: 'proposal-target', status: 'Approved' }),
    )
    const wrapper = await mountView(
      [makeProposal({ id: 'proposal-first', summary: 'First proposal' })],
      '/workspace/review#proposal-PROPOSAL-TARGET',
    )

    expect(mocks.getProposal).toHaveBeenCalledWith('PROPOSAL-TARGET')
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Target proposal')
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).not.toContain('First proposal')

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await flushPromises()

    expect(mocks.approveProposal).toHaveBeenCalledWith('proposal-target')
  })

  it('renders a requested Paper proposal 404 without decision controls or fallback selection', async () => {
    mocks.getProposal.mockRejectedValueOnce({ response: { status: 404 } })
    const wrapper = await mountView(
      [makeProposal({ id: 'proposal-first', summary: 'First proposal' })],
      '/workspace/review#proposal-PROPOSAL-MISSING',
    )

    expect(mocks.getProposal).toHaveBeenCalledWith('PROPOSAL-MISSING')
    expect(wrapper.find('[data-testid="paper-review-main"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="paper-review-empty"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-review-empty"]').text()).toContain('This proposal is unavailable.')
    expect(wrapper.find('[data-testid="paper-review-empty"]').text()).toContain('PROPOSAL-MISSING')
    expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-reject"]').exists()).toBe(false)
    expect(
      (wrapper.vm as unknown as { $route: { fullPath: string } }).$route.fullPath,
    ).toBe('/workspace/review#proposal-PROPOSAL-MISSING')

    await wrapper.find('[data-testid="paper-review-unavailable-return"]').trigger('click')
    await flushPromises()
    expect((wrapper.vm as unknown as { $route: { hash: string } }).$route.hash).toBe('')
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('First proposal')
  })

  it('updates the hash when manual queue selection replaces a deep-link target', async () => {
    mocks.approveProposal.mockResolvedValueOnce(makeProposal({ id: 'proposal-first' }))
    const wrapper = await mountView(
      [
        makeProposal({ id: 'proposal-first', summary: 'First proposal' }),
        makeProposal({ id: 'proposal-target', summary: 'Target proposal' }),
      ],
      '/workspace/review#proposal-proposal-target',
    )

    await wrapper.findAll('.paper-review-q')[0].trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('First proposal')
    expect(
      (wrapper.vm as unknown as { $route: { fullPath: string } }).$route.fullPath,
    ).toBe('/workspace/review#proposal-proposal-first')

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
    const promptSpy = vi.spyOn(window, 'prompt')
    mocks.rejectProposal.mockResolvedValueOnce(makeProposal({ id: 'pending-xyz', status: 'Rejected' }))
    const wrapper = await mountView([
      makeProposal({ id: 'pending-xyz', status: 'PendingReview', summary: 'Still actionable' }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Backspace', cancelable: true }))
    await flushPromises()

    // GH-1969: ⌫ opens the in-app reason dialog; only its accept rejects.
    await acceptRejectDialog()

    expect(mocks.rejectProposal).toHaveBeenCalled()
    expect(mocks.dismissProposals).not.toHaveBeenCalled()
    expect(promptSpy).not.toHaveBeenCalled()

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

  it('snoozes the active proposal when defer is clicked and shows a success toast', async () => {
    const deferred = makeProposal({
      id: 'proposal-001',
      deferredUntil: new Date(Date.now() + 60 * 60_000).toISOString(),
    })
    mocks.deferProposal.mockResolvedValueOnce(deferred)
    const wrapper = await mountView([makeProposal({ id: 'proposal-001' })])

    await wrapper.find('[data-testid="decision-defer"]').trigger('click')
    await flushPromises()

    expect(mocks.deferProposal).toHaveBeenCalledWith('proposal-001')
    expect(mocks.successToast).toHaveBeenCalledWith(
      'Snoozed for 1 hour — it will return to your queue.',
    )
  })

  it('removes a snoozed proposal from the visible queue after defer resolves', async () => {
    const deferred = makeProposal({
      id: 'snooze-me',
      deferredUntil: new Date(Date.now() + 60 * 60_000).toISOString(),
    })
    mocks.deferProposal.mockResolvedValueOnce(deferred)
    const wrapper = await mountView([
      makeProposal({ id: 'snooze-me', summary: 'Snooze candidate' }),
      makeProposal({ id: 'keep-me', summary: 'Stays visible' }),
    ])

    expect(wrapper.find('[data-testid="paper-review-queue-rail"]').text()).toContain(
      'Snooze candidate',
    )

    await wrapper.find('[data-testid="decision-defer"]').trigger('click')
    await flushPromises()

    const railText = wrapper.find('[data-testid="paper-review-queue-rail"]').text()
    expect(railText).not.toContain('Snooze candidate')
    expect(railText).toContain('Stays visible')
  })

  it('surfaces a toast and keeps the proposal when defer fails', async () => {
    mocks.deferProposal.mockRejectedValueOnce(new Error('snooze boom'))
    const wrapper = await mountView([makeProposal({ id: 'defer-err', summary: 'Defer error' })])

    await wrapper.find('[data-testid="decision-defer"]').trigger('click')
    await flushPromises()

    expect(mocks.errorToast).toHaveBeenCalledWith('snooze boom')
    // The proposal stays in the queue (no optimistic removal on failure).
    expect(wrapper.find('[data-testid="paper-review-queue-rail"]').text()).toContain('Defer error')
  })

  it('keeps an already-snoozed deep-linked proposal visible when extending its snooze fails', async () => {
    // #1245 adversarial sweep: onDefer must clear the deep-link hash ONLY on success. An
    // already-snoozed proposal reached via #proposal-<id> is kept visible by the carve-out; if the
    // user re-defers it and the request FAILS, clearing the hash would hide it (its prior snooze
    // still stands) with no retry path. The hash — and the proposal — must survive the failure.
    mocks.deferProposal.mockRejectedValueOnce(new Error('snooze boom'))
    const wrapper = await mountView(
      [makeProposal({
        id: 'already-snoozed',
        summary: 'Already snoozed',
        deferredUntil: new Date(Date.now() + 60 * 60_000).toISOString(),
        expiresAt: new Date(Date.now() + 25 * 60 * 60_000).toISOString(),
      })],
      '/workspace/review#proposal-already-snoozed',
    )

    // The deep link keeps the snoozed proposal visible via the carve-out.
    expect(wrapper.find('[data-testid="paper-review-queue-rail"]').text()).toContain('Already snoozed')

    await wrapper.find('[data-testid="decision-defer"]').trigger('click')
    await flushPromises()

    expect(mocks.errorToast).toHaveBeenCalledWith('snooze boom')
    // Failure must NOT clear the deep link → the proposal stays visible and retryable.
    expect(wrapper.find('[data-testid="paper-review-queue-rail"]').text()).toContain('Already snoozed')
  })

  it('clears the deep link and hides a deep-linked proposal once its snooze succeeds', async () => {
    // The success counterpart: snoozing a deep-linked proposal drops the hash so the deferred
    // filter hides it (the carve-out no longer applies).
    const deferred = makeProposal({
      id: 'deep-snooze',
      summary: 'Deep snooze',
      deferredUntil: new Date(Date.now() + 60 * 60_000).toISOString(),
    })
    mocks.deferProposal.mockResolvedValueOnce(deferred)
    const wrapper = await mountView(
      [
        makeProposal({ id: 'deep-snooze', summary: 'Deep snooze' }),
        makeProposal({ id: 'keep-me', summary: 'Stays visible' }),
      ],
      '/workspace/review#proposal-deep-snooze',
    )

    expect(wrapper.find('[data-testid="paper-review-queue-rail"]').text()).toContain('Deep snooze')

    await wrapper.find('[data-testid="decision-defer"]').trigger('click')
    await flushPromises()

    const railText = wrapper.find('[data-testid="paper-review-queue-rail"]').text()
    expect(railText).not.toContain('Deep snooze')
    expect(railText).toContain('Stays visible')
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
            expectedVersion: '7',
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

  it('records feedback and keeps the proposal in the queue when report is clicked', async () => {
    mocks.reportBadSuggestion.mockResolvedValueOnce(undefined)
    const wrapper = await mountView([makeProposal({ id: 'proposal-001', summary: 'Report me' })])

    await wrapper.get('.paper-review-prov__more').trigger('click')
    await wrapper.vm.$nextTick()
    const reportButton = document.body.querySelector('.prov-drawer__action--report') as HTMLButtonElement
    reportButton.click()
    await flushPromises()

    expect(mocks.reportBadSuggestion).toHaveBeenCalledWith('proposal-001')
    expect(mocks.successToast).toHaveBeenCalledWith('Feedback recorded for this suggestion.')
    // Pure feedback: the proposal is not removed from the queue.
    expect(wrapper.find('[data-testid="paper-review-queue-rail"]').text()).toContain('Report me')
  })

  it('surfaces an error toast and keeps the proposal when report fails', async () => {
    mocks.reportBadSuggestion.mockRejectedValueOnce(new Error('feedback boom'))
    const wrapper = await mountView([makeProposal({ id: 'report-err', summary: 'Report error' })])

    await wrapper.get('.paper-review-prov__more').trigger('click')
    await wrapper.vm.$nextTick()
    const reportButton = document.body.querySelector('.prov-drawer__action--report') as HTMLButtonElement
    reportButton.click()
    await flushPromises()

    expect(mocks.errorToast).toHaveBeenCalledWith('feedback boom')
    expect(wrapper.find('[data-testid="paper-review-queue-rail"]').text()).toContain('Report error')
  })

  it('does not double-submit feedback when report is clicked twice in quick succession', async () => {
    let resolveReport: (() => void) | undefined
    mocks.reportBadSuggestion.mockImplementationOnce(
      () => new Promise<void>((resolve) => { resolveReport = resolve }),
    )
    const wrapper = await mountView([makeProposal({ id: 'proposal-001' })])

    await wrapper.get('.paper-review-prov__more').trigger('click')
    await wrapper.vm.$nextTick()
    const reportButton = document.body.querySelector('.prov-drawer__action--report') as HTMLButtonElement
    reportButton.click()
    reportButton.click()

    expect(mocks.reportBadSuggestion).toHaveBeenCalledTimes(1)

    resolveReport?.()
    await flushPromises()
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

  it('shows the invalid state without fetching for a no-operation proposal (#1397)', async () => {
    const wrapper = await mountView([
      makeProposal({ id: 'diff-noop', diffPreview: null, operations: [] }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    // No operations → the backend `/diff` would 400 ("must contain at least one
    // operation"), the same verdict Apply gives. The reviewer must see that
    // rejection BEFORE approving, so the view shows the explicit invalid state
    // without firing the request — never a "No changes" surface it could approve.
    expect(mocks.getProposalDiff).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="paper-review-diff-empty"]').exists()).toBe(false)
    const invalid = wrapper.find('[data-testid="paper-review-diff-invalid"]')
    expect(invalid.exists()).toBe(true)
    expect(invalid.text()).toContain('no operations')
    expect(invalid.text()).toContain('reject')

    wrapper.unmount()
  })

  it('presents the stored preview under a read-only banner for an expired proposal without firing /diff (#1397)', async () => {
    const wrapper = await mountView([
      makeProposal({
        id: 'diff-expired',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        diffPreview: '0. Create card "Archived plan"',
      }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    // Expired proposals stay inspectable via their stored preview, but the live
    // `/diff` (which now 400s for them) is never fired.
    expect(mocks.getProposalDiff).not.toHaveBeenCalled()
    const banner = wrapper.find('[data-testid="paper-review-diff-banner"]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('Expired')
    expect(banner.text()).toContain('read-only')
    expect(banner.text()).toContain('stored preview')
    const pre = wrapper.find('[data-testid="paper-review-diff-pre"]')
    expect(pre.exists()).toBe(true)
    expect(pre.text()).toContain('Archived plan')
    expect(mocks.errorToast).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('diverts to the stored preview when a proposal expires during the revision-load await, never firing a live /diff (#1414 final round)', async () => {
    // onPreviewDiff checks read-only BEFORE its revision-load await. If the
    // proposal expires on the 60s clock DURING that await, the post-await
    // re-check must re-classify it as read-only and present the stored preview —
    // NOT fall through to a live /diff that #1395 400s (which would defeat
    // #1397's stored-preview guarantee). Identity alone doesn't catch this: the
    // id is unchanged, only the (clock-derived) expiry state flipped.
    vi.useFakeTimers()
    try {
      const base = new Date('2026-06-13T12:00:00.000Z').getTime()
      vi.setSystemTime(base)
      let resolveRevisions: ((value: unknown[]) => void) | undefined
      mocks.getRevisions.mockImplementation(
        () => new Promise((resolve) => { resolveRevisions = resolve as (value: unknown[]) => void }),
      )
      const wrapper = await mountView([
        makeProposal({
          id: 'preview-expire',
          // Live at entry, expires before the first 60s clock tick.
          expiresAt: new Date(base + 30_000).toISOString(),
          diffPreview: '0. Create card "Racing preview"',
        }),
      ])

      // Space opens preview; not read-only at entry, so onPreviewDiff parks on
      // its revision-load await (revisionsLoaded still false).
      window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
      await Promise.resolve()
      expect(mocks.getProposalDiff).not.toHaveBeenCalled()

      // The clock ticks past expiry while the revision load is still pending.
      await vi.advanceTimersByTimeAsync(60_000)

      // Settle the revision load → the post-await re-check sees the now-expired
      // proposal and diverts to the stored preview.
      resolveRevisions?.([])
      await flushPromises()
      await wrapper.vm.$nextTick()

      // The forbidden live /diff was never fired; the stored preview is shown.
      expect(mocks.getProposalDiff).not.toHaveBeenCalled()
      const banner = wrapper.find('[data-testid="paper-review-diff-banner"]')
      expect(banner.exists()).toBe(true)
      expect(banner.text()).toContain('read-only')
      const pre = wrapper.find('[data-testid="paper-review-diff-pre"]')
      expect(pre.exists()).toBe(true)
      expect(pre.text()).toContain('Racing preview')
      expect(mocks.errorToast).not.toHaveBeenCalled()

      wrapper.unmount()
    } finally {
      vi.useRealTimers()
    }
  })

  it('presents the stored preview for a server-expired PendingReview proposal whose client clock still reads live, never firing /diff (#1414 P2 #1)', async () => {
    // Server says isExpired:true (clock lag/skew) but the client 60s clock has
    // not yet passed expiresAt. Honoring the server flag classifies the proposal
    // read-only, so the stored preview is shown instead of a live /diff that 400s.
    const wrapper = await mountView([
      makeProposal({
        id: 'server-expired',
        status: 'PendingReview',
        expiresAt: new Date(Date.now() + 60 * 60_000).toISOString(), // client clock: still live
        isExpired: true, // server: already expired
        diffPreview: '0. Create card "Server expired"',
      }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposalDiff).not.toHaveBeenCalled()
    const banner = wrapper.find('[data-testid="paper-review-diff-banner"]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('read-only')
    const pre = wrapper.find('[data-testid="paper-review-diff-pre"]')
    expect(pre.exists()).toBe(true)
    expect(pre.text()).toContain('Server expired')

    wrapper.unmount()
  })

  it('retracts the stored preview and warns when access is revoked (getProposal 403) after reveal (#1414 P2 #2)', async () => {
    // Revealing the stored preview re-authorizes via getProposal; a 403/404 means
    // board access was revoked mid-session, so the locally-cached preview must be
    // torn down rather than left on screen.
    mocks.getProposal.mockRejectedValueOnce({ response: { status: 403 } })
    const wrapper = await mountView([
      makeProposal({
        id: 'revoked',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        diffPreview: '0. Create card "Secret"',
      }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposal).toHaveBeenCalledWith('revoked')
    // Pane torn down; no stored preview left on screen.
    expect(wrapper.find('[data-testid="paper-review-diff-banner"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="paper-review-diff-pre"]').exists()).toBe(false)
    expect(mocks.errorToast).toHaveBeenCalledWith(
      expect.stringContaining('no longer available'),
    )

    wrapper.unmount()
  })

  it('keeps the stored preview on a transient (500) error from the access re-check (#1414 P2 #2)', async () => {
    // Only a genuine 403/404 retracts the preview — a transient error must not
    // tear down an otherwise-inspectable local preview.
    mocks.getProposal.mockRejectedValueOnce({ response: { status: 500 } })
    const wrapper = await mountView([
      makeProposal({
        id: 'transient',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        diffPreview: '0. Create card "Still here"',
      }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(wrapper.find('[data-testid="paper-review-diff-banner"]').exists()).toBe(true)
    const pre = wrapper.find('[data-testid="paper-review-diff-pre"]')
    expect(pre.exists()).toBe(true)
    expect(pre.text()).toContain('Still here')
    expect(mocks.errorToast).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('falls back to the recorded operations for an expired proposal with no stored preview (#1397 / Codex)', async () => {
    // Normal creation flows never populate diffPreview, so an expired proposal
    // with operations must still be inspectable via a locally rendered
    // operation listing — no /diff call, no dead "no stored preview" end.
    const wrapper = await mountView([
      makeProposal({
        id: 'diff-expired-ops',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        diffPreview: null,
      }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposalDiff).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="paper-review-diff-banner"]').exists()).toBe(true)
    const opsFallback = wrapper.find('[data-testid="paper-review-diff-stored-operations"]')
    expect(opsFallback.exists()).toBe(true)
    expect(opsFallback.text()).toContain('Create Card')
    expect(wrapper.find('[data-testid="paper-review-diff-stored-empty"]').exists()).toBe(false)
    expect(mocks.errorToast).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('shows the banner and a no-stored-preview note for an expired zero-op proposal with no stored content (#1397)', async () => {
    const wrapper = await mountView([
      makeProposal({
        id: 'diff-expired-empty',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        diffPreview: null,
        operations: [],
      }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposalDiff).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="paper-review-diff-banner"]').exists()).toBe(true)
    const storedEmpty = wrapper.find('[data-testid="paper-review-diff-stored-empty"]')
    expect(storedEmpty.exists()).toBe(true)
    expect(storedEmpty.text()).toContain('No stored preview')
    // Never an error toast + cleared pane for a settled proposal.
    expect(mocks.errorToast).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('renders the invalid verdict with the backend reason (not a toast) when /diff 400s for a pending proposal (#1397)', async () => {
    mocks.getProposalDiff.mockRejectedValueOnce({
      response: {
        status: 400,
        data: { errorCode: 'ValidationError', message: 'Proposal must contain at least one operation' },
      },
    })
    const wrapper = await mountView([makeProposal({ id: 'diff-400' })])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposalDiff).toHaveBeenCalledWith('diff-400')
    // The 400 is presented inline with the backend's actual message, not
    // toasted, and the pane is not torn down.
    const invalid = wrapper.find('[data-testid="paper-review-diff-invalid"]')
    expect(invalid.exists()).toBe(true)
    expect(invalid.text()).toContain('must contain at least one operation')
    expect(mocks.errorToast).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="paper-review-diff"]').exists()).toBe(true)

    wrapper.unmount()
  })

  it('renders the backend expiry reason for a 400 on the expiry race, not the zero-op copy (#1397 MEDIUM-1)', async () => {
    // The 60s review clock can lag a server-side expiry: the proposal still
    // classifies live client-side → the live diff fires → the backend answers
    // 400 "Proposal has expired". The pane must present THAT reason; telling
    // the reviewer the proposal "contains no operations" would be false.
    mocks.getProposalDiff.mockRejectedValueOnce({
      response: {
        status: 400,
        data: { errorCode: 'ValidationError', message: 'Proposal has expired' },
      },
    })
    const wrapper = await mountView([makeProposal({ id: 'diff-race' })])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    const invalid = wrapper.find('[data-testid="paper-review-diff-invalid"]')
    expect(invalid.exists()).toBe(true)
    expect(invalid.text()).toContain('Proposal has expired')
    expect(invalid.text()).not.toContain('no operations')
    expect(mocks.errorToast).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('discloses a revision on the stored preview of a terminal proposal (#1397 MEDIUM-2)', async () => {
    // diffPreview is creation-time content revisions never update: a
    // revised-then-settled proposal's stored preview shows the ORIGINAL
    // submission, so the banner must say the proposal was revised.
    const now = new Date().toISOString()
    mocks.getRevisions.mockResolvedValue([
      {
        id: 'rev-1',
        proposalId: 'diff-revised-expired',
        revisionNumber: 1,
        editorUserId: 'u-1',
        revisedPayload: '{"operations":[]}',
        revisedAt: now,
        reason: 'edit',
        createdAt: now,
      },
    ])
    const wrapper = await mountView([
      makeProposal({
        id: 'diff-revised-expired',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        diffPreview: '0. Create card "Original ops"',
      }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposalDiff).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="paper-review-diff-banner"]').exists()).toBe(true)
    const revisedNote = wrapper.find('[data-testid="paper-review-diff-revised-note"]')
    expect(revisedNote.exists()).toBe(true)
    expect(revisedNote.text()).toContain('revised')
    expect(revisedNote.text()).toContain('original')
    // The live-mode "reflects your saved edit" caveat must NOT certify the
    // stored (pre-revision) content.
    expect(wrapper.find('[data-testid="paper-review-diff-revision-caveat"]').exists()).toBe(false)

    wrapper.unmount()
  })

  it('blocks Approve for a zero-operation pending proposal with an explicit verdict (#1397 LOW-3)', async () => {
    const wrapper = await mountView([
      makeProposal({ id: 'noop-approve', diffPreview: null, operations: [] }),
    ])

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await flushPromises()

    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.infoToast).toHaveBeenCalledWith(
      expect.stringContaining('no operations'),
    )
    // Pin the PENDING-distinctive copy (not the Approved variant): an
    // always-true `approvedZeroOp` bug would swap this for the Approved wording
    // and drop the "Reject or file it away" guidance — assert it explicitly.
    expect(mocks.infoToast).toHaveBeenCalledWith(
      expect.stringContaining('Reject or file it away instead'),
    )

    wrapper.unmount()
  })

  it('parks re-entrant Apply clicks while the revision load is in flight, then blocks a zero-op proposal (#1397 round 3)', async () => {
    // SEAM INVARIANT: a zero-op proposal may only be approved when revision
    // state is KNOWN. While the guard's revision load is awaited, further Apply
    // clicks are IGNORED — a re-entrant loadRevisionState would cancel the
    // earlier load via its generation counter and resume the first click with
    // revisionsLoaded still false (the round-3 Codex race).
    let resolveRevisions: ((value: unknown[]) => void) | undefined
    mocks.getRevisions.mockImplementation(
      () => new Promise((resolve) => { resolveRevisions = resolve as (value: unknown[]) => void }),
    )
    const wrapper = await mountView([
      makeProposal({ id: 'noop-inflight', diffPreview: null, operations: [] }),
    ])
    // Mount fires the watcher's background load (call 1); the Apply click's own
    // load is call 2.
    expect(mocks.getRevisions).toHaveBeenCalledTimes(1)

    // First click parks on the revision load — approve has NOT fired.
    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await Promise.resolve()
    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.getRevisions).toHaveBeenCalledTimes(2)

    // Rapid second click: guard busy — ignored entirely (no third load that
    // would cancel the first, and no approve).
    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await Promise.resolve()
    expect(mocks.getRevisions).toHaveBeenCalledTimes(2)
    expect(mocks.approveProposal).not.toHaveBeenCalled()

    // Settle the guard's own GET with an empty list → truly zero-op → blocked.
    resolveRevisions?.([])
    await flushPromises()

    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.infoToast).toHaveBeenCalledWith(expect.stringContaining('no operations'))

    wrapper.unmount()
  })

  it('holds the shared busy lock during the zero-op guard load so Defer cannot snooze the proposal mid-flight (#1414 round 4 P2-A)', async () => {
    // P2-A: while the zero-op guard awaits its revision load, applyGuardBusy now
    // joins the shared busy lock, so Defer/Reject are inert. Without it a Defer
    // landing during the await — with the #proposal- hash carve-out keeping the
    // same proposal selected — could snooze a proposal the resumed Apply then
    // approves.
    const now = new Date().toISOString()
    let resolveRevisions: ((value: unknown[]) => void) | undefined
    mocks.getRevisions.mockImplementation(
      () => new Promise((resolve) => { resolveRevisions = resolve as (value: unknown[]) => void }),
    )
    mocks.approveProposal.mockResolvedValueOnce(
      makeProposal({ id: 'noop-busy', status: 'Approved' }),
    )
    const wrapper = await mountView(
      [makeProposal({ id: 'noop-busy', diffPreview: null, operations: [] })],
      '/workspace/review#proposal-noop-busy',
    )

    // Apply parks on the revision load; the guard now holds the shared busy lock.
    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await Promise.resolve()
    expect(mocks.approveProposal).not.toHaveBeenCalled()

    // Defer mid-await is swallowed by the shared busy lock — no snooze request.
    await wrapper.find('[data-testid="decision-defer"]').trigger('click')
    await Promise.resolve()
    expect(mocks.deferProposal).not.toHaveBeenCalled()

    // Settle with a saved revision → known, non-zero state → Apply proceeds and
    // the never-snoozed proposal is approved.
    resolveRevisions?.([
      {
        id: 'rev-busy',
        proposalId: 'noop-busy',
        revisionNumber: 1,
        editorUserId: 'u-1',
        revisedPayload: '{"operations":[]}',
        revisedAt: now,
        reason: 'edit',
        createdAt: now,
      },
    ])
    await flushPromises()
    expect(mocks.approveProposal).toHaveBeenCalledWith('noop-busy')
    expect(mocks.deferProposal).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('holds the shared busy lock so Reject cannot settle the proposal during the zero-op guard load (#1414 round 4 P2-A)', async () => {
    // The P2-A invariant names Defer AND Reject. onReject now carries the same
    // internal busy guard as onDefer; assert a Reject landing mid-await is inert
    // (no reject request), so the resumed Apply cannot approve a just-rejected
    // proposal.
    const now = new Date().toISOString()
    let resolveRevisions: ((value: unknown[]) => void) | undefined
    mocks.getRevisions.mockImplementation(
      () => new Promise((resolve) => { resolveRevisions = resolve as (value: unknown[]) => void }),
    )
    mocks.approveProposal.mockResolvedValueOnce(
      makeProposal({ id: 'noop-reject-busy', status: 'Approved' }),
    )
    const wrapper = await mountView(
      [makeProposal({ id: 'noop-reject-busy', diffPreview: null, operations: [] })],
      '/workspace/review#proposal-noop-reject-busy',
    )

    // Apply parks on the revision load; the guard holds the shared busy lock.
    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await Promise.resolve()
    expect(mocks.approveProposal).not.toHaveBeenCalled()

    // Reject mid-await is swallowed by the shared busy lock — no reject request.
    await wrapper.find('[data-testid="decision-reject"]').trigger('click')
    await Promise.resolve()
    expect(mocks.rejectProposal).not.toHaveBeenCalled()

    // Settle with a saved revision → known, non-zero → Apply proceeds; the
    // never-rejected proposal is approved.
    resolveRevisions?.([
      {
        id: 'rev-reject-busy',
        proposalId: 'noop-reject-busy',
        revisionNumber: 1,
        editorUserId: 'u-1',
        revisedPayload: '{"operations":[]}',
        revisedAt: now,
        reason: 'edit',
        createdAt: now,
      },
    ])
    await flushPromises()
    expect(mocks.approveProposal).toHaveBeenCalledWith('noop-reject-busy')
    expect(mocks.rejectProposal).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('does not approve a zero-op proposal that is deferred when the guard load resolves — the post-await isProposalDeferred arm blocks it (#1414 round 4 P2-A)', async () => {
    // Exercises the `isProposalDeferred(current)` arm of the post-await re-check
    // specifically: a snoozed proposal stays PendingReview + non-expired, so
    // `isApplyActionable` alone stays TRUE — only the deferred arm blocks it.
    // The proposal is deep-linked (hash carve-out keeps a snoozed item visible)
    // and its revision load is still pending at click time, forcing the async
    // guard path where the re-check runs.
    const now = new Date().toISOString()
    let resolveRevisions: ((value: unknown[]) => void) | undefined
    mocks.getRevisions.mockImplementation(
      () => new Promise((resolve) => { resolveRevisions = resolve as (value: unknown[]) => void }),
    )
    const wrapper = await mountView(
      [
        makeProposal({
          id: 'noop-deferred',
          diffPreview: null,
          operations: [],
          // Snoozed into the future but still PendingReview (isApplyActionable stays true).
          deferredUntil: new Date(Date.now() + 60 * 60_000).toISOString(),
        }),
      ],
      '/workspace/review#proposal-noop-deferred',
    )

    // Apply parks on the revision load (revisionsLoaded still false).
    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await Promise.resolve()
    expect(mocks.approveProposal).not.toHaveBeenCalled()

    // Settle with a saved revision → known, non-zero: absent the deferred re-check
    // this would approve a snoozed proposal. The isProposalDeferred arm blocks it.
    resolveRevisions?.([
      {
        id: 'rev-deferred',
        proposalId: 'noop-deferred',
        revisionNumber: 1,
        editorUserId: 'u-1',
        revisedPayload: '{"operations":[]}',
        revisedAt: now,
        reason: 'edit',
        createdAt: now,
      },
    ])
    await flushPromises()

    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.executeProposal).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('executes a plain Approved proposal with operations without entering the zero-op guard (#1414 round 4 P2-B regression)', async () => {
    // The common Approved-execute path must be untouched by the P2-B reorder: a
    // non-empty-operations Approved proposal skips the zero-op guard entirely and
    // dispatches execute directly (dialog-gated since #1818).
    const confirmSpy = vi.spyOn(window, 'confirm')
    mocks.executeProposal.mockResolvedValueOnce(
      makeProposal({ id: 'approved-ops', status: 'Applied' }),
    )
    const wrapper = await mountView([
      makeProposal({ id: 'approved-ops', status: 'Approved' }),
    ])

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await flushPromises()

    // #1818: the first click opens the app dialog and executes NOTHING.
    expect(mocks.executeProposal).not.toHaveBeenCalled()
    expect(confirmSpy).not.toHaveBeenCalled()
    await confirmApplyDialog()

    expect(mocks.executeProposal).toHaveBeenCalledWith('approved-ops', expect.anything())
    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.infoToast).not.toHaveBeenCalledWith(
      expect.stringContaining('no operations'),
    )

    confirmSpy.mockRestore()
    wrapper.unmount()
  })

  it('does not approve a zero-op proposal that leaves the actionable state under the guard load — the post-await re-check blocks it (#1414 round 4 P2-A)', async () => {
    // P2-A defense-in-depth: identity is not enough. A non-local change (here the
    // 60s expiry clock; equally a realtime status/defer update from another
    // surface) can settle the proposal out of an actionable state WHILE the guard
    // load is in flight. The post-await re-check re-evaluates actionability on the
    // CURRENT object and no-ops when it is no longer applyable.
    vi.useFakeTimers()
    try {
      const base = new Date('2026-06-13T12:00:00.000Z').getTime()
      vi.setSystemTime(base)
      const nowIso = new Date(base).toISOString()
      let resolveRevisions: ((value: unknown[]) => void) | undefined
      mocks.getRevisions.mockImplementation(
        () => new Promise((resolve) => { resolveRevisions = resolve as (value: unknown[]) => void }),
      )
      const wrapper = await mountView([
        makeProposal({
          id: 'noop-expire',
          diffPreview: null,
          operations: [],
          // Live at mount, expires before the first 60s clock tick.
          expiresAt: new Date(base + 30_000).toISOString(),
        }),
      ])

      // Apply parks on the revision load (revisionsLoaded still false).
      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()
      expect(mocks.approveProposal).not.toHaveBeenCalled()

      // The clock ticks past expiry while the load is still pending.
      await vi.advanceTimersByTimeAsync(60_000)

      // Settle with a saved revision → known, non-zero: absent the re-check this
      // would approve a now-expired proposal. The re-check blocks it.
      resolveRevisions?.([
        {
          id: 'rev-expire',
          proposalId: 'noop-expire',
          revisionNumber: 1,
          editorUserId: 'u-1',
          revisedPayload: '{"operations":[]}',
          revisedAt: nowIso,
          reason: 'edit',
          createdAt: nowIso,
        },
      ])
      await flushPromises()
      await wrapper.vm.$nextTick()

      expect(mocks.approveProposal).not.toHaveBeenCalled()
      expect(mocks.executeProposal).not.toHaveBeenCalled()

      wrapper.unmount()
    } finally {
      vi.useRealTimers()
    }
  })

  it('routes an Approved zero-op proposal through the guard instead of executing a doomed apply (#1414 round 4 P2-B)', async () => {
    // P2-B: an already-Approved zero-op proposal (pre-#1423 data, or another
    // client that approved via the still-permissive approve endpoint) must hit
    // the same zero-op verdict, not a guaranteed Apply-time 400 round-trip. The
    // Approved-execute dispatch now sits AFTER the guard.
    const wrapper = await mountView([
      makeProposal({ id: 'approved-noop', status: 'Approved', diffPreview: null, operations: [] }),
    ])

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await flushPromises()

    expect(mocks.executeProposal).not.toHaveBeenCalled()
    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.infoToast).toHaveBeenCalledWith(
      expect.stringContaining('applying it to the board will be rejected'),
    )

    wrapper.unmount()
  })

  it('still executes an Approved proposal that carries a saved revision once the guard clears it (#1414 round 4 P2-B)', async () => {
    // The guard must not break the normal Approved-execute path: an Approved
    // proposal whose original operations are empty but which carries a saved
    // revision (#1235) is applied revision-aware, so it still executes.
    const now = new Date().toISOString()
    const confirmSpy = vi.spyOn(window, 'confirm')
    mocks.getRevisions.mockResolvedValue([
      {
        id: 'rev-approved',
        proposalId: 'approved-revised',
        revisionNumber: 1,
        editorUserId: 'u-1',
        revisedPayload: '{"operations":[{"actionType":"CreateCard"}]}',
        revisedAt: now,
        reason: 'edit',
        createdAt: now,
      },
    ])
    mocks.executeProposal.mockResolvedValueOnce(
      makeProposal({ id: 'approved-revised', status: 'Applied' }),
    )
    const wrapper = await mountView([
      makeProposal({ id: 'approved-revised', status: 'Approved', diffPreview: null, operations: [] }),
    ])

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await flushPromises()

    // #1830 round 2: the dialog that gates this path must not report the zero
    // ORIGINAL operations as what is about to be applied.
    expect(document.body.querySelector('[data-testid="apply-confirm-operations"]')).toBeNull()
    expect(
      document.body.querySelector('[data-testid="apply-confirm-revision"]')?.textContent,
    ).toContain('latest saved revision')

    await confirmApplyDialog()

    expect(mocks.executeProposal).toHaveBeenCalledWith('approved-revised', expect.anything())
    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(confirmSpy).not.toHaveBeenCalled()

    confirmSpy.mockRestore()
    wrapper.unmount()
  })

  it('blocks Apply when the revision load fails for a zero-op proposal — unknown state never approves (#1397 round 3)', async () => {
    // REVERSES the round-2 fall-through semantic: a failed load leaves revision
    // state UNKNOWN, and unknown now blocks approve instead of deferring to the
    // backend (whose approve path would accept, then guarantee the Apply-time
    // 400). #1416 remains the backend root-cause fix.
    mocks.getRevisions.mockRejectedValue(new Error('revisions boom'))
    const wrapper = await mountView([
      makeProposal({ id: 'noop-failload', diffPreview: null, operations: [] }),
    ])

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await flushPromises()

    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.infoToast).toHaveBeenCalledWith(
      expect.stringContaining('Revision history is unavailable'),
    )

    wrapper.unmount()
  })

  it('discloses a revision on the recorded-operations fallback when no stored preview was captured (#1397 MEDIUM-2 / #1414)', async () => {
    // A revised-then-settled proposal that never captured a diffPreview renders
    // the recorded-operations fallback, not a stored preview — so the disclosure
    // copy must say the RECORDED OPERATIONS show the original submission, never
    // "the stored preview" (which does not exist here).
    const now = new Date().toISOString()
    mocks.getRevisions.mockResolvedValue([
      {
        id: 'rev-1',
        proposalId: 'diff-revised-ops',
        revisionNumber: 1,
        editorUserId: 'u-1',
        revisedPayload: '{"operations":[]}',
        revisedAt: now,
        reason: 'edit',
        createdAt: now,
      },
    ])
    const wrapper = await mountView([
      makeProposal({
        id: 'diff-revised-ops',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        diffPreview: null,
      }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposalDiff).not.toHaveBeenCalled()
    // The ops fallback is what's on screen (no captured stored preview).
    expect(wrapper.find('[data-testid="paper-review-diff-stored-operations"]').exists()).toBe(true)
    const revisedNote = wrapper.find('[data-testid="paper-review-diff-revised-note"]')
    expect(revisedNote.exists()).toBe(true)
    expect(revisedNote.text()).toContain('revised')
    expect(revisedNote.text()).toContain('recorded operations')
    expect(revisedNote.text()).not.toContain('stored preview')

    wrapper.unmount()
  })

  it('renders the stored preview synchronously while the revision GET is pending; the caveat augments after it resolves (#1397 round 3)', async () => {
    // SEAM INVARIANT: the stored preview is local content and must render the
    // moment Space is pressed — the revision metadata GET only gates the
    // revised-note caveat and runs asynchronously afterwards. A slow GET must
    // never make the toggle look dead.
    const now = new Date().toISOString()
    let resolveRevisions: ((value: unknown[]) => void) | undefined
    mocks.getRevisions.mockImplementation(
      () => new Promise((resolve) => { resolveRevisions = resolve as (value: unknown[]) => void }),
    )
    const wrapper = await mountView([
      makeProposal({
        id: 'sync-stored',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        diffPreview: '0. Create card "Stored now"',
      }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await wrapper.vm.$nextTick()

    // GET still pending — the preview is ALREADY up.
    expect(wrapper.find('[data-testid="paper-review-diff-banner"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-review-diff-pre"]').text()).toContain('Stored now')
    expect(wrapper.find('[data-testid="paper-review-diff-loading"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="paper-review-diff-revised-note"]').exists()).toBe(false)

    // The metadata resolves with a revision → the caveat augments in place.
    resolveRevisions?.([
      {
        id: 'rev-1',
        proposalId: 'sync-stored',
        revisionNumber: 1,
        editorUserId: 'u-1',
        revisedPayload: '{"operations":[]}',
        revisedAt: now,
        reason: 'edit',
        createdAt: now,
      },
    ])
    await flushPromises()

    expect(wrapper.find('[data-testid="paper-review-diff-revised-note"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-review-diff-pre"]').text()).toContain('Stored now')
    expect(mocks.getProposalDiff).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('keeps the stored preview up with no error toast when the revision GET fails (#1397 round 3)', async () => {
    // The augment-only revision load is silent: a failed metadata GET must not
    // error-toast over a locally presentable preview, and the preview stays up.
    mocks.getRevisions.mockRejectedValue(new Error('revisions boom'))
    const wrapper = await mountView([
      makeProposal({
        id: 'silent-stored',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        diffPreview: '0. Create card "Still here"',
      }),
    ])
    // The mount-time background load (non-silent) already failed during
    // mountView; clear its toast so the assertion isolates the preview path.
    mocks.errorToast.mockClear()

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(wrapper.find('[data-testid="paper-review-diff-banner"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-review-diff-pre"]').text()).toContain('Still here')
    expect(mocks.errorToast).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="paper-review-diff-revised-note"]').exists()).toBe(false)

    wrapper.unmount()
  })

  it('re-derives an open live pane to the stored presentation when the expiry clock passes (#1397 LOW-5)', async () => {
    // Open the live diff on a pending proposal that expires in 30s, then tick
    // the 60s review clock past its expiresAt: the pane must flip to the stored
    // read-only presentation instead of keeping the live-looking diff on a
    // proposal that is no longer actionable. Only setInterval and Date are
    // faked so flushPromises (setTimeout-based) keeps working.
    vi.useFakeTimers({ toFake: ['setInterval', 'Date'] })
    try {
      mocks.getProposalDiff.mockResolvedValueOnce('0. Create card "Live"')
      const wrapper = await mountView([
        makeProposal({
          id: 'flip-exp',
          status: 'PendingReview',
          expiresAt: new Date(Date.now() + 30_000).toISOString(),
          diffPreview: '0. Create card "Stored"',
        }),
      ])

      window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
      await flushPromises()
      expect(wrapper.find('[data-testid="paper-review-diff-pre"]').text()).toContain('Live')
      expect(wrapper.find('[data-testid="paper-review-diff-banner"]').exists()).toBe(false)

      // One 60s clock tick → nowMs passes expiresAt → the proposal is expired.
      vi.advanceTimersByTime(60_000)
      await flushPromises()
      await wrapper.vm.$nextTick()

      const banner = wrapper.find('[data-testid="paper-review-diff-banner"]')
      expect(banner.exists()).toBe(true)
      expect(banner.text()).toContain('read-only')
      expect(wrapper.find('[data-testid="paper-review-diff-pre"]').text()).toContain('Stored')

      wrapper.unmount()
    } finally {
      vi.useRealTimers()
    }
  })

  it('fetches the revision-aware diff for a 0-operation proposal that has a saved revision', async () => {
    // Regression for the #1235 review: the no-op short-circuit must not fire when a
    // saved revision exists. The backend renders a revision-aware diff, so Apply
    // would execute the revised operations even when the ORIGINAL ops are empty —
    // the view must fetch, not silently show the empty surface (preview == apply).
    const now = new Date().toISOString()
    mocks.getRevisions.mockResolvedValue([
      {
        id: 'rev-1',
        proposalId: 'diff-noop-rev',
        revisionNumber: 1,
        editorUserId: 'u-1',
        revisedPayload:
          '{"operations":[{"sequence":0,"actionType":"create","targetType":"card","parameters":"{}","idempotencyKey":"k"}]}',
        revisedAt: now,
        reason: 'edit',
        createdAt: now,
      },
    ])
    mocks.getProposalDiff.mockResolvedValueOnce('0. Create card')
    const wrapper = await mountView([
      makeProposal({ id: 'diff-noop-rev', diffPreview: null, operations: [] }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposalDiff).toHaveBeenCalledWith('diff-noop-rev')
    expect(wrapper.find('[data-testid="paper-review-diff-empty"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="paper-review-diff-pre"]').exists()).toBe(true)

    wrapper.unmount()
  })

  it('notes that the diff reflects the saved revision when one exists', async () => {
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
    // The backend now returns the revision-aware diff (#1235), so the note confirms
    // the preview reflects the saved edit and equals what Apply will execute.
    const caveat = wrapper.find('[data-testid="paper-review-diff-revision-caveat"]')
    expect(caveat.exists()).toBe(true)
    expect(caveat.text()).toContain('saved edit')
    expect(caveat.text()).toContain('not the original')

    wrapper.unmount()
  })

  it('clears an open diff after a revision is saved so the note cannot certify stale content', async () => {
    // #1235 review (Codex P2): if the diff is open and the reviewer then saves an
    // edit, the visible previewDiff is pre-revision content; the "reflects your
    // saved edit" note must never certify it. Saving clears the diff so re-opening
    // fetches the fresh revision-aware one.
    const now = new Date().toISOString()
    mocks.getProposalDiff.mockResolvedValueOnce('0. Create card "Original"')
    mocks.createRevision.mockResolvedValue({
      id: 'rev-1',
      proposalId: 'proposal-001',
      revisionNumber: 1,
      editorUserId: 'u-1',
      revisedPayload:
        '{"operations":[{"sequence":0,"actionType":"create","targetType":"card","parameters":"{}","idempotencyKey":"k"}]}',
      revisedAt: now,
      reason: 'edit',
      createdAt: now,
    })
    const wrapper = await mountView([makeProposal({ id: 'proposal-001' })])

    // Open the diff.
    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()
    expect(wrapper.find('[data-testid="paper-review-diff"]').exists()).toBe(true)

    // Enter edit mode and save a revision.
    await wrapper.find('[data-testid="decision-edit"]').trigger('click')
    await flushPromises()
    wrapper.findComponent(ReviewRevisionEditor).vm.$emit('save', {
      revisedPayload:
        '{"operations":[{"sequence":0,"actionType":"create","targetType":"card","parameters":"{}","idempotencyKey":"k"}]}',
      reason: 'edit',
    })
    await flushPromises()

    // The stale diff is gone; re-opening would fetch the revision-aware one.
    expect(mocks.createRevision).toHaveBeenCalled()
    expect(wrapper.find('[data-testid="paper-review-diff"]').exists()).toBe(false)

    wrapper.unmount()
  })

  it('surfaces an error (not an empty diff) when a 404 occurs for an operations-bearing proposal', async () => {
    // A proposal with operations bypasses the no-op guard and fetches; a 404 here
    // means the proposal was deleted/dismissed elsewhere, so it must error rather
    // than silently render an empty diff.
    mocks.getProposalDiff.mockRejectedValueOnce({
      response: { data: { errorCode: 'NotFound', message: 'Proposal not found' } },
    })
    const wrapper = await mountView([makeProposal({ id: 'diff-gone' })])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposalDiff).toHaveBeenCalledWith('diff-gone')
    expect(mocks.errorToast).toHaveBeenCalledWith('Proposal not found')
    expect(wrapper.find('[data-testid="paper-review-diff"]').exists()).toBe(false)

    wrapper.unmount()
  })

  describe('mini-cadence wiring (#1802)', () => {
    function decidedDaysAgo(daysAgo: number, overrides: Partial<Proposal> = {}): Proposal {
      const d = new Date()
      d.setHours(12, 0, 0, 0)
      d.setDate(d.getDate() - daysAgo)
      const iso = d.toISOString()
      return makeProposal({
        id: `decided-${daysAgo}`,
        status: 'Applied',
        decidedAt: iso,
        decidedByUserId: 'u-1',
        appliedAt: iso,
        ...overrides,
      })
    }

    it("renders real cadence bars from the current user's decided proposals", async () => {
      const wrapper = await mountView([
        makeProposal({ id: 'pending' }),
        decidedDaysAgo(0, { id: 'a' }),
        decidedDaysAgo(0, { id: 'b' }),
        decidedDaysAgo(3, { id: 'c' }),
      ])

      const cadence = wrapper.find('[data-testid="paper-review-mini-cadence"]')
      expect(cadence.exists()).toBe(true)
      expect(cadence.attributes('aria-label')).toBe('Activity for the last 7 days')

      const bars = wrapper.findAll('.paper-review-cadence__bar')
      // Two decisions today (tallest, and the only "today" bar), one three days
      // ago at half height, the remaining days measured zero — not invented.
      expect(bars.map((bar) => (bar.element as HTMLElement).style.height)).toEqual([
        '0%',
        '0%',
        '0%',
        '50%',
        '0%',
        '0%',
        '100%',
      ])
      expect(bars[6].classes()).toContain('paper-review-cadence__bar--today')
      expect(bars[3].classes()).not.toContain('paper-review-cadence__bar--today')
    })

    it('hides the cadence when only other users have decided proposals', async () => {
      const wrapper = await mountView([
        makeProposal({ id: 'pending' }),
        decidedDaysAgo(1, { id: 'theirs', decidedByUserId: 'u-2' }),
      ])

      expect(wrapper.find('[data-testid="paper-review-mini-cadence"]').exists()).toBe(false)
      expect(wrapper.findAll('.paper-review-cadence__bar')).toHaveLength(0)
    })

    it('hides the cadence when nothing has been decided at all', async () => {
      const wrapper = await mountView([makeProposal({ id: 'pending' })])

      expect(wrapper.find('[data-testid="paper-review-mini-cadence"]').exists()).toBe(false)
      // The rail still shows the honest apply-rate empty state beneath the heading.
      expect(wrapper.find('[data-testid="paper-review-apply-rate-empty"]').text()).toBe(
        'No decisions yet',
      )
    })

    it('scopes the cadence to the active board filter', async () => {
      const wrapper = await mountView(
        [
          makeProposal({ id: 'pending', boardId: 'board-1' }),
          decidedDaysAgo(1, { id: 'other-board', boardId: 'board-2' }),
        ],
        '/workspace/review?boardId=board-1',
      )

      expect(wrapper.find('[data-testid="paper-review-mini-cadence"]').exists()).toBe(false)

      wrapper.unmount()
    })
  })

  // --- #1818: the two-phase Apply must be legible on the Paper surface --------

  describe('two-phase apply legibility (#1818)', () => {
    function railPhase(wrapper: ReturnType<typeof mount>): string | undefined {
      return wrapper.find('[data-testid="paper-review-decision-rail"]').attributes('data-apply-phase')
    }

    it('a pending proposal offers Approve, no approved banner, and an approve key hint', async () => {
      const wrapper = await mountView([makeProposal({ id: 'pending-1' })])

      expect(wrapper.find('[data-testid="paper-review-approved-banner"]').exists()).toBe(false)
      // The VISIBLE face of the primary button; the other phase's label is also
      // in the DOM as the width reservation (GH-1942).
      expect(wrapper.get('[data-testid="decision-apply-label"]').text()).toBe('Approve')
      expect(wrapper.get('[data-testid="decision-apply-reserve"]').text()).toBe('Apply to board')
      expect(railPhase(wrapper)).toBe('approve')
      expect(wrapper.get('[data-testid="paper-review-key-hint"]').text()).toBe(
        'PRESS ⏎ TO APPROVE · ⌫ TO REJECT',
      )
      // The right-rail key legend must agree with the rail.
      expect(wrapper.get('[data-testid="paper-review-right-rail"]').text()).toContain(
        'Approve proposal · step 1 of 2',
      )

      wrapper.unmount()
    })

    it('after the first Apply the surface visibly becomes "approved — not yet applied"', async () => {
      // THE defect from the live walkthrough: the first click approved the
      // proposal and nothing on screen said the board was untouched.
      mocks.approveProposal.mockResolvedValueOnce(
        makeProposal({ id: 'pending-2', status: 'Approved' }),
      )
      const wrapper = await mountView([makeProposal({ id: 'pending-2' })])

      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()

      expect(mocks.approveProposal).toHaveBeenCalledWith('pending-2')
      // Phase 1 must NOT touch the board.
      expect(mocks.executeProposal).not.toHaveBeenCalled()

      const banner = wrapper.get('[data-testid="paper-review-decision-receipt"]')
      expect(banner.text()).toContain('Approved — not yet applied to the board.')
      expect(banner.text()).toContain('Apply to board')
      expect(wrapper.get('[data-testid="decision-apply-label"]').text()).toBe('Apply to board')
      expect(railPhase(wrapper)).toBe('execute')
      expect(wrapper.get('[data-testid="paper-review-key-hint"]').text()).toBe(
        'PRESS ⏎ TO APPLY TO BOARD',
      )
      expect(wrapper.get('[data-testid="paper-review-right-rail"]').text()).toContain(
        'Apply to board · step 2 of 2',
      )

      wrapper.unmount()
    })

    it('the second Apply opens the app dialog with the proposal summary, not a native confirm', async () => {
      const confirmSpy = vi.spyOn(window, 'confirm')
      mocks.executeProposal.mockResolvedValueOnce(
        makeProposal({ id: 'approved-2', status: 'Applied' }),
      )
      const wrapper = await mountView([
        makeProposal({ id: 'approved-2', status: 'Approved', summary: 'Split "dark mode" into 3 cards' }),
      ])

      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()

      expect(confirmSpy).not.toHaveBeenCalled()
      expect(mocks.executeProposal).not.toHaveBeenCalled()
      const summary = document.body.querySelector('[data-testid="apply-confirm-summary"]')
      expect(summary?.textContent).toContain('Split "dark mode" into 3 cards')

      await confirmApplyDialog()
      expect(mocks.executeProposal).toHaveBeenCalledWith('approved-2', expect.anything())

      confirmSpy.mockRestore()
      wrapper.unmount()
    })

    it('cancelling the dialog leaves the proposal approved and the board untouched', async () => {
      const wrapper = await mountView([makeProposal({ id: 'approved-3', status: 'Approved' })])

      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()

      const cancel = document.body.querySelector(
        '[data-testid="apply-confirm-cancel"]',
      ) as HTMLButtonElement
      cancel.click()
      await flushPromises()

      expect(mocks.executeProposal).not.toHaveBeenCalled()
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()
      // Still approved-but-not-applied, and still says so.
      expect(wrapper.find('[data-testid="paper-review-approved-banner"]').exists()).toBe(true)
      expect(railPhase(wrapper)).toBe('execute')

      wrapper.unmount()
    })

    it('a settled proposal is distinct from both pending and approved-not-yet-applied', async () => {
      // Approved-then-expired: the state most easily confused with "approved,
      // waiting for me to confirm". It must NOT carry the approved banner, and
      // the rail must be the filing rail, not an apply phase.
      const wrapper = await mountView([
        makeProposal({
          id: 'approved-expired-1818',
          status: 'Approved',
          expiresAt: new Date(Date.now() - 60_000).toISOString(),
        }),
      ])

      expect(wrapper.find('[data-testid="paper-review-approved-banner"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="decision-file-away"]').exists()).toBe(true)
      expect(railPhase(wrapper)).toBe('settled')
      expect(wrapper.get('[data-testid="paper-review-key-hint"]').text()).toBe(
        'PRESS ⌫ TO FILE AWAY',
      )

      wrapper.unmount()
    })

    it('the review keymap is inert while the confirmation dialog is open', async () => {
      // #1830 round 2: asserting only "executeProposal was not called" for ⏎ is
      // NOT discriminating — with the `executeConfirmProposal === null` clause
      // deleted from the keymap's enabled gate, ⏎ reaches onApply, which merely
      // re-requests the same confirmation and calls no API. Two assertions that
      // do discriminate:
      //   1. the keymap only calls preventDefault() when it actually dispatches
      //      a handler, so an un-prevented event proves the gate held;
      //   2. ⌫ behind the dialog would reach onReject, and an Approved proposal
      //      is not reject-actionable, so it emits its refusal toast.
      const wrapper = await mountView([makeProposal({ id: 'approved-4', status: 'Approved' })])

      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).not.toBeNull()
      mocks.infoToast.mockClear()

      const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true })
      window.dispatchEvent(enter)
      await flushPromises()
      expect(enter.defaultPrevented).toBe(false)

      const backspace = new KeyboardEvent('keydown', { key: 'Backspace', cancelable: true })
      window.dispatchEvent(backspace)
      await flushPromises()
      expect(backspace.defaultPrevented).toBe(false)
      expect(mocks.infoToast).not.toHaveBeenCalled()
      expect(mocks.rejectProposal).not.toHaveBeenCalled()

      // The dialog is still the only path to the board.
      expect(mocks.executeProposal).not.toHaveBeenCalled()
      expect(mocks.approveProposal).not.toHaveBeenCalled()
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).not.toBeNull()

      wrapper.unmount()
    })
  })

  it('keeps the exact approved proposal visible through a stale filter without auto-applying', async () => {
    mocks.approveProposal.mockResolvedValueOnce(
      makeProposal({ id: 'receipt-approved', status: 'Approved', summary: 'Receipt target' }),
    )
    const staleAt = new Date(Date.now() - 25 * 60 * 60_000).toISOString()
    const wrapper = await mountView(
      [
        makeProposal({ id: 'receipt-approved', summary: 'Receipt target', createdAt: staleAt }),
        makeProposal({ id: 'other-stale', summary: 'Other stale proposal', createdAt: staleAt }),
      ],
      '/workspace/review#proposal-receipt-approved',
    )

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await flushPromises()

    const receipt = wrapper.get('[data-testid="paper-review-decision-receipt"]')
    expect(receipt.attributes('role')).toBe('status')
    expect(receipt.attributes('data-decision')).toBe('approved')
    expect(receipt.text()).toContain('not yet applied')
    expect(mocks.executeProposal).not.toHaveBeenCalled()
    expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()
    expect(wrapper.find('[data-testid="decision-reject"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-edit"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-defer"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="paper-review-right-rail"]').text()).not.toContain('Reject')
    expect(wrapper.findAll('.paper-review-keys__row')).toHaveLength(1)

    await wrapper.findAll('button').find((button) => button.text() === 'Stale')?.trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Receipt target')
  })

  it('retains apply, reject, and defer outcomes as nonactionable receipts', async () => {
    mocks.executeProposal.mockResolvedValueOnce(
      makeProposal({ id: 'receipt-applied', status: 'Applied', summary: 'Applied receipt', appliedAt: new Date().toISOString() }),
    )
    const applied = await mountView([makeProposal({ id: 'receipt-applied', status: 'Approved', summary: 'Applied receipt' })])
    await applied.find('[data-testid="decision-apply"]').trigger('click')
    await confirmApplyDialog()
    expect(applied.get('[data-testid="paper-review-decision-receipt"]').attributes('data-decision')).toBe('applied')
    expect(applied.find('[data-testid="paper-review-decision-rail"]').exists()).toBe(false)
    expect(applied.find('[data-testid="paper-review-queue-rail"]').text()).toContain('Applied receipt')
    applied.unmount()

    mocks.rejectProposal.mockResolvedValueOnce(
      makeProposal({ id: 'receipt-rejected', status: 'Rejected', summary: 'Rejected receipt' }),
    )
    const rejected = await mountView([makeProposal({ id: 'receipt-rejected', summary: 'Rejected receipt' })])
    await rejected.find('[data-testid="decision-reject"]').trigger('click')
    await flushPromises()
    await acceptRejectDialog('not needed')
    expect(rejected.get('[data-testid="paper-review-decision-receipt"]').attributes('data-decision')).toBe('rejected')
    expect(rejected.find('[data-testid="paper-review-decision-rail"]').exists()).toBe(false)
    rejected.unmount()

    const deferredUntil = new Date(Date.now() + 60 * 60_000).toISOString()
    mocks.deferProposal.mockResolvedValueOnce(
      makeProposal({ id: 'receipt-deferred', summary: 'Deferred receipt', deferredUntil }),
    )
    const deferred = await mountView([makeProposal({ id: 'receipt-deferred', summary: 'Deferred receipt' })])
    await deferred.find('[data-testid="decision-defer"]').trigger('click')
    await flushPromises()
    expect(deferred.get('[data-testid="paper-review-decision-receipt"]').attributes('data-decision')).toBe('deferred')
    expect(deferred.find('[data-testid="paper-review-decision-rail"]').exists()).toBe(false)
  })

  it('does not dispatch the global review keymap after a deferred receipt', async () => {
    const deferredUntil = new Date(Date.now() + 60 * 60_000).toISOString()
    mocks.deferProposal.mockResolvedValueOnce(
      makeProposal({ id: 'keymap-deferred', deferredUntil }),
    )
    const wrapper = await mountView([makeProposal({ id: 'keymap-deferred' })])

    await wrapper.find('[data-testid="decision-defer"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="paper-review-decision-receipt"]').attributes('data-decision')).toBe('deferred')
    expect(wrapper.find('[data-testid="paper-review-key-hint"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="paper-review-right-rail"]').text()).not.toContain('Apply to board')

    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true })
    window.dispatchEvent(enter)
    await flushPromises()

    expect(enter.defaultPrevented).toBe(false)
    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.executeProposal).not.toHaveBeenCalled()
  })

  it('does not render or execute a board-A receipt during a board-B reload', async () => {
    const boardA = makeProposal({ id: 'board-a-receipt', boardId: 'board-a', summary: 'Board A receipt' })
    const boardB = makeProposal({ id: 'board-b-pending', boardId: 'board-b', summary: 'Board B pending' })
    mocks.approveProposal.mockResolvedValueOnce(
      makeProposal({ ...boardA, status: 'Approved' }),
    )
    const wrapper = await mountView(
      [boardA],
      '/workspace/review?boardId=board-a',
      [
        { id: 'board-a', name: 'Board A' },
        { id: 'board-b', name: 'Board B' },
      ],
    )

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="paper-review-decision-receipt"]').attributes('data-decision')).toBe('approved')
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Board A receipt')

    let resolveBoardB!: (value: Proposal[]) => void
    mocks.getProposals.mockImplementationOnce(
      () => new Promise<Proposal[]>((resolve) => { resolveBoardB = resolve }),
    )
    mocks.approveProposal.mockClear()
    const router = wrapper.vm.$router
    await router.push({ name: 'workspace-review', query: { boardId: 'board-b' } })
    await nextTick()

    expect(wrapper.find('[data-testid="paper-review-decision-receipt"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(false)
    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true })
    window.dispatchEvent(enter)
    await flushPromises()
    expect(enter.defaultPrevented).toBe(false)
    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.executeProposal).not.toHaveBeenCalled()

    resolveBoardB([boardB])
    await flushPromises()
    expect(wrapper.find('[data-testid="paper-review-decision-receipt"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain('Board B pending')
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).not.toContain('Board A receipt')
  })

  // --- #1940 receipt flow: explicit Approve then Apply -----------------------
  //
  // #1818 made the two phases visible; what was left was a third click that did
  // nothing but open the dialog. The receipt now makes approval legible in
  // place; Apply deliberately opens the confirmation only when chosen.
  describe('apply-flow receipt and confirmation (GH-1942)', () => {
    function railPhase(wrapper: ReturnType<typeof mount>): string | undefined {
      return wrapper.find('[data-testid="paper-review-decision-rail"]').attributes('data-apply-phase')
    }

    it('takes three explicit user actions from pending to applied, and the board is untouched until confirmation', async () => {
      mocks.approveProposal.mockResolvedValueOnce(
        makeProposal({ id: 'flow-1', status: 'Approved' }),
      )
      mocks.executeProposal.mockResolvedValueOnce(
        makeProposal({ id: 'flow-1', status: 'Applied' }),
      )
      const wrapper = await mountView([makeProposal({ id: 'flow-1' })])

      let userActions = 0

      // ── Action 1: Approve on the decision rail.
      expect(wrapper.get('[data-testid="decision-apply-label"]').text()).toBe('Approve')
      userActions += 1
      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()

      // Phase 1 recorded the approval and NOTHING else. The one remaining step
      // is already on screen — there is no intermediate button to press.
      expect(mocks.approveProposal).toHaveBeenCalledTimes(1)
      expect(mocks.executeProposal).not.toHaveBeenCalled()
      expect(railPhase(wrapper)).toBe('execute')
      expect(wrapper.get('[data-testid="decision-apply-label"]').text()).toBe('Apply to board')
      expect(wrapper.get('[data-testid="paper-review-decision-receipt"]').text()).toContain('not yet applied')
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()

      // ── Action 2: accept that dialog. `confirmApplyDialog` hard-asserts the
      // accept button is present, so re-adding a middle step fails here rather
      // than silently passing.
      userActions += 1
      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).not.toBeNull()

      userActions += 1
      await confirmApplyDialog()

      expect(userActions).toBe(3)
      expect(mocks.executeProposal).toHaveBeenCalledTimes(1)
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()

      wrapper.unmount()
    })

    it('keeps approve and execute as two separate explicit backend calls in order (ADR-0003)', async () => {
      // The collapse is presentational ONLY. Auto-applying — approve's response
      // triggering execute without a second human action — is the thing
      // ADR-0003 forbids, so pin the call sequence and its gating.
      const calls: string[] = []
      mocks.approveProposal.mockImplementationOnce(async (id: string) => {
        // Execute must not have run before, or as part of, the approve call.
        expect(mocks.executeProposal).not.toHaveBeenCalled()
        calls.push(`approve:${id}`)
        return makeProposal({ id, status: 'Approved' })
      })
      mocks.executeProposal.mockImplementationOnce(async (id: string) => {
        calls.push(`execute:${id}`)
        return makeProposal({ id, status: 'Applied' })
      })
      const wrapper = await mountView([makeProposal({ id: 'adr-3' })])

      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()

      // Approve has returned; the board is still untouched and stays that way
      // for as long as the human does not accept.
      expect(calls).toEqual(['approve:adr-3'])
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()

      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()
      await confirmApplyDialog()

      expect(calls).toEqual(['approve:adr-3', 'execute:adr-3'])
      expect(mocks.approveProposal).toHaveBeenCalledTimes(1)
      expect(mocks.approveProposal).toHaveBeenCalledWith('adr-3')
      // Execute is the mutating half and still carries its Idempotency-Key;
      // approve takes no such key. Two different calls, not one repeated.
      expect(mocks.executeProposal).toHaveBeenCalledTimes(1)
      expect(mocks.executeProposal).toHaveBeenCalledWith('adr-3', expect.any(String))

      wrapper.unmount()
    })

    it('does not offer the apply confirmation when approve fails', async () => {
      mocks.approveProposal.mockRejectedValueOnce(new Error('approve exploded'))
      const wrapper = await mountView([makeProposal({ id: 'approve-fail' })])

      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()

      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()
      expect(mocks.executeProposal).not.toHaveBeenCalled()
      expect(railPhase(wrapper)).toBe('approve')
      expect(wrapper.find('[data-testid="paper-review-approved-banner"]').exists()).toBe(false)
      expect(mocks.errorToast).toHaveBeenCalled()

      wrapper.unmount()
    })

    it('backing out of the explicit Apply dialog leaves its approved receipt in place', async () => {
      mocks.approveProposal.mockResolvedValueOnce(
        makeProposal({ id: 'back-out', status: 'Approved' }),
      )
      const wrapper = await mountView([makeProposal({ id: 'back-out' })])

      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()

      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()
      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()

      const cancel = document.body.querySelector(
        '[data-testid="apply-confirm-cancel"]',
      ) as HTMLButtonElement
      // Dismissing does not undo the approval, and the copy says so.
      expect(cancel.textContent).toContain('Not yet')
      cancel.click()
      await flushPromises()

      expect(mocks.executeProposal).not.toHaveBeenCalled()
      expect(railPhase(wrapper)).toBe('execute')
      expect(wrapper.find('[data-testid="paper-review-decision-receipt"]').exists()).toBe(true)

      // The rail's primary button is the way back to the same single step —
      // and it must not re-approve.
      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()
      expect(mocks.approveProposal).toHaveBeenCalledTimes(1)
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).not.toBeNull()

      wrapper.unmount()
    })

    // The collapse introduced an await between the click and the dialog. Two
    // things that used to be impossible now are: focus is elsewhere when the
    // dialog mounts, and the reviewer can move the queue underneath it.

    it('returns focus to the rail when the explicitly requested Apply dialog is dismissed', async () => {
      // NOTE ON FIDELITY. jsdom does NOT implement the HTML rule that a focused
      // element losing focusability (here: `disabled` flipping on while the
      // approve call is in flight) moves focus to the body — it leaves
      // activeElement pointing at the disabled button. Left implicit, this spec
      // would pass with the restore deleted, because TdDialog's own capture
      // would still be holding the trigger. So the blur is performed
      // explicitly: it is the browser behaviour under test, not a convenience.
      let resolveApprove!: (proposal: Proposal) => void
      mocks.approveProposal.mockImplementationOnce(
        () =>
          new Promise<Proposal>((resolve) => {
            resolveApprove = resolve
          }),
      )
      const wrapper = await mountView(
        [makeProposal({ id: 'focus-1' })],
        '/workspace/review',
        [],
        [],
        { attachTo: true },
      )

      const trigger = wrapper.get('[data-testid="decision-apply"]').element as HTMLButtonElement
      trigger.focus()
      expect(document.activeElement).toBe(trigger)

      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()

      // The shared busy lock disables the trigger for the whole round trip…
      expect(trigger.disabled).toBe(true)
      // …which is what strands focus on <body> in a real browser. (`blur()` is
      // not the way to model it here: jsdom ignores blur() on an element that
      // is no longer a focusable area, which a disabled button is not.)
      document.body.focus()
      expect(document.activeElement).toBe(document.body)

      // Only NOW does the dialog open, so the only return target TdDialog can
      // capture for itself is <body> — a restore it makes into a no-op.
      resolveApprove(makeProposal({ id: 'focus-1', status: 'Approved' }))
      await flushPromises()
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()
      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).not.toBeNull()

      const cancel = document.body.querySelector(
        '[data-testid="apply-confirm-cancel"]',
      ) as HTMLButtonElement
      cancel.click()
      await flushPromises()
      await nextTick()

      // Not <body>: a keyboard user picks up on the control they left, rather
      // than restarting at the top of the document.
      expect(document.activeElement).not.toBe(document.body)
      expect(document.activeElement).toBe(wrapper.get('[data-testid="decision-apply"]').element)

      wrapper.unmount()
    })

    it('returns focus to the rail after a failed apply, once the busy lock clears', async () => {
      // The accepted-apply exit cannot restore at dialog-close time: the rail
      // is disabled for the whole execute round trip and focus() on a disabled
      // control is a no-op. The restore must defer until the busy lock clears.
      // The failed-execute path is the one where a target still exists — the
      // rail re-enables for the retry. (A successful apply unmounts the rail
      // with the proposal; that gap is GH-1940's decision-feedback work.)
      let rejectExecute!: (reason: Error) => void
      mocks.executeProposal.mockImplementationOnce(
        () =>
          new Promise<Proposal>((_resolve, reject) => {
            rejectExecute = reject
          }),
      )
      // Approved-direct path: the click opens the dialog without an approve
      // round trip, so the completed-apply exit is isolated from the approve
      // choreography the dismissal spec above already covers.
      const wrapper = await mountView(
        [makeProposal({ id: 'focus-2', status: 'Approved' })],
        '/workspace/review',
        [],
        [],
        { attachTo: true },
      )

      const trigger = wrapper.get('[data-testid="decision-apply"]').element as HTMLButtonElement
      trigger.focus()
      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()

      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).not.toBeNull()
      const accept = document.body.querySelector(
        '[data-testid="apply-confirm-accept"]',
      ) as HTMLButtonElement
      accept.click()
      await flushPromises()
      await nextTick()

      // Dialog closed, execute still in flight: the rail is disabled (TdDialog's
      // own restore no-ops against the disabled trigger), so an eager view-side
      // restore would no-op too and leave focus on <body>.
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()
      expect(trigger.disabled).toBe(true)
      document.body.focus()
      expect(document.activeElement).toBe(document.body)

      rejectExecute(new Error('apply failed'))
      await flushPromises()
      await nextTick()
      await nextTick()

      // The rail survived the failure and re-enabled — focus lands back on the
      // control the user needs for the retry, not on <body>.
      const active = document.activeElement as HTMLElement | null
      expect(active).not.toBe(document.body)
      expect(['decision-apply', 'decision-file-away']).toContain(active?.dataset?.testid)

      wrapper.unmount()
    })

    it('does not open the confirmation against a proposal switched during the approve round trip', async () => {
      let resolveApprove!: (proposal: Proposal) => void
      mocks.approveProposal.mockImplementationOnce(
        () =>
          new Promise<Proposal>((resolve) => {
            resolveApprove = resolve
          }),
      )
      const wrapper = await mountView([
        makeProposal({ id: 'aaa-1', summary: 'First proposal' }),
        makeProposal({ id: 'bbb-1', summary: 'Second proposal' }),
      ])

      await wrapper.find('[data-serial="#AAA-"]').trigger('click')
      await flushPromises()
      await wrapper.find('[data-testid="decision-apply"]').trigger('click')

      // The busy lock covers the decision buttons and the keymap, NOT the queue:
      // switching proposals mid-flight is a real thing a reviewer can do.
      await wrapper.find('[data-serial="#BBB-"]').trigger('click')
      await flushPromises()

      resolveApprove(makeProposal({ id: 'aaa-1', status: 'Approved' }))
      await flushPromises()

      // The approval stands, and nothing was written to the board — but the
      // reviewer is not asked to confirm an apply for a proposal they left.
      expect(mocks.approveProposal).toHaveBeenCalledWith('aaa-1')
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()
      expect(mocks.executeProposal).not.toHaveBeenCalled()
      expect(wrapper.find('[data-testid="paper-review-decision-receipt"]').exists()).toBe(false)
      expect(wrapper.get('[data-testid="paper-review-main"]').text()).toContain('Second proposal')
      expect(wrapper.get('[data-testid="paper-review-main"]').text()).not.toContain('First proposal')

      wrapper.unmount()
    })
  })

  /**
   * GH-1983 — the keyboard path through the two-phase apply.
   *
   * ⏎ approves; a second deliberate ⏎ opens the explicit Apply confirmation.
   * The dialog still owns the final confirmation and preserves the two calls.
   */
  describe('keyboard path through the apply confirmation (GH-1983)', () => {
    function pressEnterInDialog(options: KeyboardEventInit = {}) {
      const dialog = document.body.querySelector('.td-dialog') as HTMLElement | null
      expect(dialog, 'expected the apply confirmation to be open').not.toBeNull()
      dialog!.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true, ...options }),
      )
    }

    it('approves on ⏎, opens Apply on a second deliberate ⏎, and confirms in the dialog', async () => {
      mocks.approveProposal.mockResolvedValueOnce(
        makeProposal({ id: 'proposal-001', status: 'Approved' }),
      )
      mocks.executeProposal.mockResolvedValueOnce(
        makeProposal({ id: 'proposal-001', status: 'Applied' }),
      )
      const wrapper = await mountView([makeProposal()])

      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }))
      await flushPromises()

      expect(mocks.approveProposal).toHaveBeenCalledWith('proposal-001')
      expect(mocks.executeProposal).not.toHaveBeenCalled()
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()

      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }))
      await flushPromises()
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).not.toBeNull()

      pressEnterInDialog()
      await flushPromises()

      expect(mocks.executeProposal).toHaveBeenCalledTimes(1)
      expect(mocks.executeProposal.mock.calls[0][0]).toBe('proposal-001')
      // Exactly one approve, exactly one execute — the second ⏎ did not also
      // re-dispatch the surface's own Enter handler behind the closing dialog.
      expect(mocks.approveProposal).toHaveBeenCalledTimes(1)

      wrapper.unmount()
    })

    it('a HELD ⏎ carried over from the approve does not apply', async () => {
      // The keyboard half of the hazard GH-1942 closed for the pointer when it
      // turned backdrop dismissal off: the dialog appears under an interaction
      // that is still in progress. Auto-repeat is how a held key presents.
      mocks.approveProposal.mockResolvedValueOnce(
        makeProposal({ id: 'proposal-001', status: 'Approved' }),
      )
      const wrapper = await mountView([makeProposal()])

      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }))
      await flushPromises()

      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', repeat: true, cancelable: true }))
      await flushPromises()
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).not.toBeNull()

      pressEnterInDialog({ repeat: true })
      pressEnterInDialog({ repeat: true })
      await flushPromises()

      expect(mocks.executeProposal).not.toHaveBeenCalled()
      // The proposal is left approved-but-not-applied, with the dialog still
      // offering the step — nothing was decided and nothing was lost.
      expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).not.toBeNull()

      wrapper.unmount()
    })
  })

  /**
   * GH-1969 — the rejection reason is collected in-app, not by `window.prompt`.
   *
   * Asserted end to end through the rendered DOM against the reject request
   * payload, because the reason IS the artifact: it is what a later reader sees
   * explaining why a proposal did not ship.
   */
  describe('reject reason dialog (GH-1969)', () => {
    it('opens a dialog instead of a native prompt, and decides nothing until accepted', async () => {
      const promptSpy = vi.spyOn(window, 'prompt')
      const wrapper = await mountView([makeProposal()])

      await wrapper.find('[data-testid="decision-reject"]').trigger('click')
      await flushPromises()

      expect(promptSpy).not.toHaveBeenCalled()
      expect(document.body.querySelector('[data-testid="reject-dialog"]')).not.toBeNull()
      expect(mocks.rejectProposal).not.toHaveBeenCalled()

      promptSpy.mockRestore()
      wrapper.unmount()
    })

    it('persists the typed reason on the reject request', async () => {
      mocks.rejectProposal.mockResolvedValueOnce(
        makeProposal({ id: 'proposal-001', status: 'Rejected' }),
      )
      const wrapper = await mountView([makeProposal()])

      await wrapper.find('[data-testid="decision-reject"]').trigger('click')
      await flushPromises()
      await acceptRejectDialog('already tracked on the On-Call rota')

      expect(mocks.rejectProposal).toHaveBeenCalledWith(
        'proposal-001',
        'already tracked on the On-Call rota',
      )
      expect(document.body.querySelector('[data-testid="reject-dialog"]')).toBeNull()

      wrapper.unmount()
    })

    it('cancelling the dialog leaves the proposal alone', async () => {
      const wrapper = await mountView([makeProposal()])

      await wrapper.find('[data-testid="decision-reject"]').trigger('click')
      await flushPromises()
      ;(
        document.body.querySelector('[data-testid="reject-dialog-cancel"]') as HTMLButtonElement
      ).click()
      await flushPromises()

      expect(mocks.rejectProposal).not.toHaveBeenCalled()
      expect(document.body.querySelector('[data-testid="reject-dialog"]')).toBeNull()
      // The rail is live again — a cancelled reject is not a decision.
      expect(wrapper.get('[data-testid="decision-apply"]').attributes('disabled')).toBeUndefined()

      wrapper.unmount()
    })

    it('demands a reason for a high-risk proposal before offering the accept', async () => {
      mocks.rejectProposal.mockResolvedValueOnce(
        makeProposal({ id: 'proposal-001', riskLevel: 'High', status: 'Rejected' }),
      )
      const wrapper = await mountView([makeProposal({ riskLevel: 'High' })])

      await wrapper.find('[data-testid="decision-reject"]').trigger('click')
      await flushPromises()

      const accept = document.body.querySelector(
        '[data-testid="reject-dialog-accept"]',
      ) as HTMLButtonElement
      expect(accept.disabled).toBe(true)

      await acceptRejectDialog('Duplicates the incident ticket')
      expect(mocks.rejectProposal).toHaveBeenCalledWith(
        'proposal-001',
        'Duplicates the incident ticket',
      )

      wrapper.unmount()
    })

    it('silences the review keymap while the reason dialog is open', async () => {
      const wrapper = await mountView([makeProposal()])

      await wrapper.find('[data-testid="decision-reject"]').trigger('click')
      await flushPromises()

      // ⏎ behind the dialog must not approve, and ⌫ must not re-open the gate.
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }))
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Backspace', cancelable: true }))
      await flushPromises()

      expect(mocks.approveProposal).not.toHaveBeenCalled()
      expect(mocks.rejectProposal).not.toHaveBeenCalled()

      wrapper.unmount()
    })
  })

  /**
   * GH-1964 — "Request edit" bricked the surface.
   *
   * Every assertion here is against the RENDERED DOM on purpose. The bug lived
   * for as long as it did because `revisionEditing === true` was the whole
   * observable contract: the composer really did exist, just below the fold with
   * no scroll and no focus, while the shared lock silenced four working buttons
   * and the entire keymap. A spec asserting only the flag would have passed
   * throughout, so these assert what the reviewer can actually see and reach.
   */
  describe('revision composer entry and lock legibility (GH-1964)', () => {
    it('scrolls the composer into view and moves focus into it on entry', async () => {
      const scrollIntoView = vi.fn()
      const original = Element.prototype.scrollIntoView
      // happy-dom does not implement scrollIntoView, so install it rather than spy.
      Element.prototype.scrollIntoView = scrollIntoView
      try {
        const wrapper = await mountView([makeProposal()], '/workspace/review', [], [], {
          attachTo: true,
        })

        await wrapper.find('[data-testid="decision-edit"]').trigger('click')
        await flushPromises()

        const composer = wrapper.get('[data-testid="revision-editor"]')
        expect(scrollIntoView).toHaveBeenCalled()
        // The composer element itself was the scroll target, not some other node
        // that happened to scroll during the same tick.
        expect(scrollIntoView.mock.instances).toContain(composer.element)

        // Focus is INSIDE the composer, on something the reviewer can type into.
        const active = document.activeElement as HTMLElement | null
        expect(active).not.toBeNull()
        expect(composer.element.contains(active)).toBe(true)
        expect(active?.tagName).toBe('TEXTAREA')

        wrapper.unmount()
      } finally {
        Element.prototype.scrollIntoView = original
      }
    })

    it('states on the rail why the decisions are disabled, and offers the exit there', async () => {
      const wrapper = await mountView([makeProposal()])

      expect(wrapper.find('[data-testid="decision-lock-note"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="decision-cancel-edit"]').exists()).toBe(false)

      await wrapper.find('[data-testid="decision-edit"]').trigger('click')
      await flushPromises()

      const note = wrapper.get('[data-testid="decision-lock-note"]')
      expect(note.text()).toContain('save or cancel the edit')
      // The four disabled buttons point AT that explanation.
      const noteId = note.attributes('id')
      expect(noteId).toBeTruthy()
      for (const testid of ['decision-reject', 'decision-edit', 'decision-defer', 'decision-apply']) {
        const button = wrapper.get(`[data-testid="${testid}"]`)
        expect(button.attributes('disabled')).toBeDefined()
        expect(button.attributes('aria-describedby')).toBe(noteId)
      }

      // The exit is ON the rail and is NOT itself disabled by the lock it cancels.
      const cancel = wrapper.get('[data-testid="decision-cancel-edit"]')
      expect(cancel.attributes('disabled')).toBeUndefined()
    })

    it('restores every decision button AND the keymap when the rail cancel is used', async () => {
      mocks.approveProposal.mockResolvedValueOnce(
        makeProposal({ id: 'proposal-001', status: 'Approved' }),
      )
      const wrapper = await mountView([makeProposal()])

      await wrapper.find('[data-testid="decision-edit"]').trigger('click')
      await flushPromises()

      // The keymap is silent while the lock is held.
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }))
      await flushPromises()
      expect(mocks.approveProposal).not.toHaveBeenCalled()

      await wrapper.get('[data-testid="decision-cancel-edit"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="revision-editor"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="decision-lock-note"]').exists()).toBe(false)
      for (const testid of ['decision-reject', 'decision-edit', 'decision-defer', 'decision-apply']) {
        expect(wrapper.get(`[data-testid="${testid}"]`).attributes('disabled')).toBeUndefined()
      }

      // …and the keymap answers again, with no reload.
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }))
      await flushPromises()
      expect(mocks.approveProposal).toHaveBeenCalledWith('proposal-001')

      wrapper.unmount()
    })

    it('clears the lock when the reviewer switches to another proposal', async () => {
      // The persistence half of GH-1964: the proposal-switch watcher in
      // useProposalRevisions must actually drop `editing`, so re-entering the
      // surface never finds a rail locked by a composer that is not there.
      const wrapper = await mountView([
        makeProposal({ id: 'aaa-1', summary: 'First proposal' }),
        makeProposal({ id: 'bbb-1', summary: 'Second proposal' }),
      ])

      await wrapper.find('[data-serial="#AAA-"]').trigger('click')
      await flushPromises()
      await wrapper.find('[data-testid="decision-edit"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="revision-editor"]').exists()).toBe(true)

      await wrapper.find('[data-serial="#BBB-"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="revision-editor"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="decision-lock-note"]').exists()).toBe(false)
      expect(wrapper.get('[data-testid="decision-apply"]').attributes('disabled')).toBeUndefined()
    })
  })
})
