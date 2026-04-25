import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
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
  getBoards: vi.fn(),
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
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.sessionState.userId = 'u-1'
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

  it('does not send apply or reject transitions for expired proposals', async () => {
    const wrapper = await mountView([
      makeProposal({
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        summary: 'Expired proposal',
      }),
    ])

    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await wrapper.find('[data-testid="decision-reject"]').trigger('click')
    await flushPromises()

    expect(mocks.approveProposal).not.toHaveBeenCalled()
    expect(mocks.executeProposal).not.toHaveBeenCalled()
    expect(mocks.rejectProposal).not.toHaveBeenCalled()
    expect(mocks.infoToast).toHaveBeenCalledWith(
      'This proposal is no longer actionable. Refresh review to see current status.',
    )
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

  it('surfaces feedback when request edit is invoked before backend support exists', async () => {
    const wrapper = await mountView([makeProposal()])

    await wrapper.find('[data-testid="decision-edit"]').trigger('click')
    await flushPromises()

    expect(mocks.getProposalDiff).not.toHaveBeenCalled()
    expect(mocks.infoToast).toHaveBeenCalledWith(
      'Request edit is not wired yet; no proposal changes were sent.',
    )
  })

  it('surfaces feedback when preview diff is invoked before visible diff UI exists', async () => {
    const wrapper = await mountView([makeProposal()])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
    await flushPromises()

    expect(mocks.getProposalDiff).not.toHaveBeenCalled()
    expect(mocks.infoToast).toHaveBeenCalledWith(
      'Preview diff is not wired yet; no diff was loaded.',
    )

    wrapper.unmount()
  })
})
