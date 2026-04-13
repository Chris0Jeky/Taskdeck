import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Proposal } from '../../types/automation'
import ReviewView from '../../views/ReviewView.vue'

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
  createRequestId: vi.fn(),
}))

vi.mock('../../api/automationApi', () => ({
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

vi.mock('../../api/boardsApi', () => ({
  boardsApi: {
    getBoards: mocks.getBoards,
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.successToast,
    error: mocks.errorToast,
    info: mocks.infoToast,
  }),
}))

vi.mock('../../utils/requestId', () => ({
  createRequestId: mocks.createRequestId,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

function buildProposal(overrides: Partial<Proposal> = {}): Proposal {
  const now = new Date().toISOString()
  const futureExpiry = new Date(Date.now() + 24 * 60 * 60_000).toISOString()
  const base: Proposal = {
    id: 'proposal-1',
    sourceType: 'Queue',
    sourceReferenceId: 'capture-1',
    boardId: 'board-1',
    requestedByUserId: 'user-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: 'Test proposal',
    diffPreview: null,
    validationIssues: null,
    createdAt: now,
    updatedAt: now,
    expiresAt: futureExpiry,
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'triage-run-1',
    operations: [],
    presentation: {
      plainSummary: 'Test proposal summary.',
      impactSummary: '1 planned change.',
      riskCue: 'Low risk.',
      sourceCue: 'Created from Inbox.',
      operationHeadlines: ['Create card "Test".'],
      affectedEntities: [
        {
          entityType: 'Card',
          entityId: 'card-1',
          label: 'Card card-1',
          changeCount: 1,
        },
      ],
    },
  }

  const hasPresentationOverride = 'presentation' in overrides
  const merged: Proposal = {
    ...base,
    ...overrides,
    presentation: hasPresentationOverride
      ? overrides.presentation
        ? { ...base.presentation!, ...overrides.presentation }
        : overrides.presentation
      : base.presentation,
  }

  if (!hasPresentationOverride && overrides.summary && merged.presentation) {
    merged.presentation = {
      ...merged.presentation,
      plainSummary: `${overrides.summary} summary.`,
      operationHeadlines: [`Create card "${overrides.summary}".`],
    }
  }

  return merged
}

async function mountAt(path: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/workspace/review', name: 'workspace-review', component: ReviewView },
      { path: '/workspace/inbox', name: 'workspace-inbox', component: { template: '<div />' } },
      { path: '/workspace/boards/:id', name: 'workspace-board', component: { template: '<div />' } },
    ],
  })

  await router.push(path)
  await router.isReady()

  const wrapper = mount(ReviewView, {
    attachTo: document.body,
    global: { plugins: [router] },
  })

  await Promise.resolve()
  await Promise.resolve()
  await wrapper.vm.$nextTick()

  mountedWrapper = wrapper
  return { wrapper, router }
}

let mountedWrapper: ReturnType<typeof mount> | null = null
let originalPrompt: typeof window.prompt

describe('ReviewView — approve and apply actions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    originalPrompt = window.prompt
    mocks.getProposals.mockResolvedValue([])
    mocks.getBoards.mockResolvedValue([
      { id: 'board-1', name: 'Engineering Sprint' },
    ])
    mocks.approveProposal.mockResolvedValue(buildProposal({ status: 'Approved' }))
    mocks.rejectProposal.mockResolvedValue(buildProposal({ status: 'Rejected' }))
    mocks.executeProposal.mockResolvedValue(buildProposal({ status: 'Applied' }))
    mocks.getProposalDiff.mockResolvedValue('diff')
    mocks.dismissProposals.mockResolvedValue({ dismissed: 1 })
    mocks.createRequestId.mockReturnValue('request-1')
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
    window.prompt = originalPrompt
  })

  it('approves a PendingReview proposal and transitions to Approved status', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-to-approve',
        status: 'PendingReview',
        summary: 'Approve me',
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    expect(wrapper.text()).toContain('Review required')
    expect(wrapper.text()).toContain('Approve me')

    const card = wrapper.get('#proposal-proposal-to-approve')
    const approveBtn = card.findAll('button').find((b) => b.text() === 'Approve for board')
    expect(approveBtn).toBeDefined()
    await approveBtn!.trigger('click')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(mocks.approveProposal).toHaveBeenCalledWith('proposal-to-approve')
    expect(mocks.successToast).toHaveBeenCalled()
  })

  it('applies an Approved proposal to the board after confirmation', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)

    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-to-apply',
        status: 'Approved',
        summary: 'Apply me',
        sourceReferenceId: 'capture-2',
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    expect(wrapper.text()).toContain('Approved, ready to apply')

    const card = wrapper.get('#proposal-proposal-to-apply')
    const applyBtn = card.findAll('button').find((b) => b.text() === 'Apply to board')
    expect(applyBtn).toBeDefined()
    await applyBtn!.trigger('click')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(confirmSpy).toHaveBeenCalled()
    expect(mocks.executeProposal).toHaveBeenCalledWith('proposal-to-apply', 'request-1')
    expect(mocks.successToast).toHaveBeenCalled()

    confirmSpy.mockRestore()
  })

  it('shows error toast when approve fails', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-fail-approve',
        status: 'PendingReview',
        summary: 'Fail approve',
      }),
    ])
    mocks.approveProposal.mockRejectedValue(new Error('Approve failed'))

    const { wrapper } = await mountAt('/workspace/review')

    const card = wrapper.get('#proposal-proposal-fail-approve')
    const approveBtn = card.findAll('button').find((b) => b.text() === 'Approve for board')
    await approveBtn!.trigger('click')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(mocks.errorToast).toHaveBeenCalled()
  })

  it('shows error toast when apply fails', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)

    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-fail-apply',
        status: 'Approved',
        summary: 'Fail apply',
        sourceReferenceId: 'capture-3',
      }),
    ])
    mocks.executeProposal.mockRejectedValue(new Error('Apply failed'))

    const { wrapper } = await mountAt('/workspace/review')

    const card = wrapper.get('#proposal-proposal-fail-apply')
    const applyBtn = card.findAll('button').find((b) => b.text() === 'Apply to board')
    await applyBtn!.trigger('click')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(mocks.errorToast).toHaveBeenCalled()

    confirmSpy.mockRestore()
  })

  it('rejects a proposal with a reason and updates the card status', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-to-reject',
        status: 'PendingReview',
        summary: 'Reject me',
      }),
    ])
    window.prompt = vi.fn(() => 'Too risky')

    const { wrapper } = await mountAt('/workspace/review')

    const card = wrapper.get('#proposal-proposal-to-reject')
    const rejectBtn = card.findAll('button').find((b) => b.text() === 'Reject')
    await rejectBtn!.trigger('click')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(mocks.rejectProposal).toHaveBeenCalledWith('proposal-to-reject', 'Too risky')
    expect(mocks.successToast).toHaveBeenCalled()
  })
})

describe('ReviewView — summary cards', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    originalPrompt = window.prompt
    mocks.getBoards.mockResolvedValue([
      { id: 'board-1', name: 'Engineering Sprint' },
    ])
    mocks.approveProposal.mockResolvedValue(buildProposal({ status: 'Approved' }))
    mocks.executeProposal.mockResolvedValue(buildProposal({ status: 'Applied' }))
    mocks.getProposalDiff.mockResolvedValue('diff')
    mocks.dismissProposals.mockResolvedValue({ dismissed: 1 })
    mocks.createRequestId.mockReturnValue('request-1')
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
    window.prompt = originalPrompt
  })

  it('renders summary cards with pending and ready counts', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-pending-1',
        status: 'PendingReview',
        summary: 'Pending 1',
      }),
      buildProposal({
        id: 'proposal-pending-2',
        status: 'PendingReview',
        summary: 'Pending 2',
        sourceReferenceId: 'capture-2',
      }),
      buildProposal({
        id: 'proposal-approved-1',
        status: 'Approved',
        summary: 'Approved 1',
        sourceReferenceId: 'capture-3',
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    const summaryCards = wrapper.findAll('.td-review-summary-card')
    const pendingCard = summaryCards.find((c) => c.text().includes('Pending review'))
    expect(pendingCard?.text()).toContain('2')

    const readyCard = summaryCards.find((c) => c.text().includes('Ready to execute'))
    expect(readyCard?.text()).toContain('1')
  })

  it('shows two-step flow indicator on pending proposals', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-flow',
        status: 'PendingReview',
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    const flowSteps = wrapper.find('.td-review-card__flow-steps')
    expect(flowSteps.exists()).toBe(true)
    expect(flowSteps.attributes('role')).toBe('list')
    expect(flowSteps.text()).toContain('Approve')
    expect(flowSteps.text()).toContain('Apply to board')
  })
})

describe('ReviewView — risk level indicators', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    originalPrompt = window.prompt
    mocks.getBoards.mockResolvedValue([{ id: 'board-1', name: 'Sprint' }])
    mocks.approveProposal.mockResolvedValue(buildProposal({ status: 'Approved' }))
    mocks.createRequestId.mockReturnValue('request-1')
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
    window.prompt = originalPrompt
  })

  it('renders Low risk badge from proposal riskLevel', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        riskLevel: 'Low',
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    const riskBadge = wrapper.find('.td-risk-badge')
    expect(riskBadge.exists()).toBe(true)
    expect(riskBadge.text()).toContain('Low risk')
    expect(riskBadge.classes()).toContain('td-risk--low')
  })

  it('renders Medium risk badge from proposal riskLevel', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        riskLevel: 'Medium',
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    const riskBadge = wrapper.find('.td-risk-badge')
    expect(riskBadge.exists()).toBe(true)
    expect(riskBadge.text()).toContain('Medium risk')
    expect(riskBadge.classes()).toContain('td-risk--medium')
  })
})

describe('ReviewView — loading and error on proposals fetch', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    originalPrompt = window.prompt
    mocks.getBoards.mockResolvedValue([])
    mocks.createRequestId.mockReturnValue('request-1')
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
    window.prompt = originalPrompt
  })

  it('shows error toast when proposals loading fails', async () => {
    mocks.getProposals.mockRejectedValue(new Error('Network error'))

    await mountAt('/workspace/review')

    expect(mocks.errorToast).toHaveBeenCalled()
  })
})
