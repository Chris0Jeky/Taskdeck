import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Proposal } from '../../../../types/automation'
import ReviewView from '../../../../views/ReviewView.vue'

const mocks = vi.hoisted(() => ({
  getProposals: vi.fn(),
  getProposal: vi.fn(),
  approveProposal: vi.fn(),
  getBoards: vi.fn(),
  getColumns: vi.fn(),
  getProvenance: vi.fn(),
  getConfidence: vi.fn(),
  getSideEffects: vi.fn(),
  getConflicts: vi.fn(),
  getHistory: vi.fn(),
  getSimilarPast: vi.fn(),
  getProvenanceMetadata: vi.fn(),
  getCaptureItem: vi.fn(),
  getRevisions: vi.fn(),
  getLatestRevision: vi.fn(),
  getCollaboration: vi.fn(),
  refreshWorkloadCounts: vi.fn(),
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
    approveProposals: vi.fn(),
    rejectProposal: vi.fn(),
    deferProposal: vi.fn(),
    executeProposal: vi.fn(),
    executeProposals: vi.fn(),
    getProposalDiff: vi.fn(),
    dismissProposals: vi.fn(),
    reportBadSuggestion: vi.fn(),
  },
}))
vi.mock('../../../../api/boardsApi', () => ({ boardsApi: { getBoards: mocks.getBoards } }))
vi.mock('../../../../api/columnsApi', () => ({ columnsApi: { getColumns: mocks.getColumns } }))
vi.mock('../../../../api/workspaceApi', () => ({
  workspaceApi: { getCollaboration: mocks.getCollaboration },
}))
vi.mock('../../../../api/proposalDeepReviewApi', () => ({
  proposalDeepReviewApi: {
    getProvenance: mocks.getProvenance,
    getConfidence: mocks.getConfidence,
    getSideEffects: mocks.getSideEffects,
    getConflicts: mocks.getConflicts,
    getHistory: mocks.getHistory,
    getSimilarPast: mocks.getSimilarPast,
    getProvenanceMetadata: mocks.getProvenanceMetadata,
  },
}))
vi.mock('../../../../api/captureApi', () => ({
  captureApi: { getItem: mocks.getCaptureItem },
}))
vi.mock('../../../../api/proposalRevisionsApi', () => ({
  proposalRevisionsApi: {
    createRevision: vi.fn(),
    getRevisions: mocks.getRevisions,
    getLatestRevision: mocks.getLatestRevision,
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
vi.mock('../../../../store/workspaceStore', () => ({
  useWorkspaceStore: () => ({ refreshWorkloadCounts: mocks.refreshWorkloadCounts }),
}))

function proposal(overrides: Partial<Proposal> = {}): Proposal {
  const now = new Date().toISOString()
  return {
    id: 'proposal-active',
    sourceType: 'Chat',
    sourceReferenceId: null,
    boardId: 'board-1',
    requestedByUserId: 'u-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: 'Active proposal',
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
    operations: [{
      id: 'op-1',
      proposalId: 'proposal-active',
      sequence: 0,
      actionType: 'CreateCard',
      targetType: 'Card',
      targetId: null,
      parameters: '{}',
      idempotencyKey: 'key-1',
      expectedVersion: null,
    }],
    approvedRevisionId: null,
    latestRevisionId: null,
    ...overrides,
  }
}

async function mountView(initial: Proposal[], followUp: Proposal[]) {
  mocks.getProposals.mockResolvedValueOnce(initial)
  mocks.getProposals.mockResolvedValue(followUp)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/workspace/review', name: 'workspace-review', component: ReviewView }],
  })
  await router.push('/workspace/review')
  await router.isReady()
  const wrapper = mount(ReviewView, {
    attachTo: document.body,
    global: { plugins: [router] },
  })
  await flushPromises()
  await nextTick()
  return { wrapper, router }
}

describe('Paper Review unavailable-return root fallback (GH-2599)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    localStorage.setItem('td.paper.mode.v2', 'on')
    mocks.sessionState.userId = 'u-1'
    mocks.getBoards.mockResolvedValue([])
    mocks.getColumns.mockResolvedValue([])
    mocks.getCollaboration.mockResolvedValue({ memberCount: 2, hasCollaborators: true })
    mocks.getProposal.mockRejectedValue({ response: { status: 404 } })
    mocks.getProvenance.mockResolvedValue([])
    mocks.getConfidence.mockResolvedValue({
      overall: 0.84,
      components: [],
      note: null,
      threshold: null,
      source: 'model-reported',
      meetsThreshold: null,
    })
    mocks.getSideEffects.mockResolvedValue({
      rows: [],
      reversibility: {
        summary: 'Low risk',
        description: 'Confirm before applying.',
        windowMs: 60_000,
      },
    })
    mocks.getConflicts.mockResolvedValue([])
    mocks.getHistory.mockResolvedValue([])
    mocks.getSimilarPast.mockResolvedValue({ decisions: [], applyRate: 0 })
    mocks.getProvenanceMetadata.mockResolvedValue({
      provider: null,
      model: null,
      promptVersion: null,
    })
    mocks.getCaptureItem.mockRejectedValue(new Error('capture unavailable'))
    mocks.getRevisions.mockResolvedValue([])
    mocks.getLatestRevision.mockResolvedValue(null)
  })

  afterEach(() => {
    document.body.innerHTML = ''
    localStorage.clear()
  })

  it('focuses the stable review landmark when a filtered rail and decision receipt remove both ordinary targets', async () => {
    const pending = proposal()
    const approved = proposal({
      status: 'Approved',
      decidedAt: new Date().toISOString(),
      decidedByUserId: 'u-1',
    })
    mocks.approveProposal.mockResolvedValue(approved)

    const { wrapper, router } = await mountView([pending], [approved])
    try {
      await wrapper.get('[data-testid="decision-apply"]').trigger('click')
      await flushPromises()
      await nextTick()
      expect(mocks.approveProposal).toHaveBeenCalledWith('proposal-active')

      const staleFilter = wrapper
        .findAll('.paper-review-rail__pill')
        .find((button) => button.text().trim() === 'Stale')
      expect(staleFilter).toBeDefined()
      await staleFilter!.trigger('click')
      await nextTick()

      expect(wrapper.findAll('.paper-review-rail__queue-row').length).toBe(0)
      expect(wrapper.find('[data-testid="paper-review-main"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="paper-review-empty"]').exists()).toBe(false)

      await router.push('/workspace/review#proposal-proposal-missing')
      await flushPromises()
      await nextTick()
      const back = wrapper.get('[data-testid="paper-review-unavailable-return"]')
      ;(back.element as HTMLElement).focus()
      expect(document.activeElement).toBe(back.element)

      await back.trigger('click')
      await flushPromises()
      await nextTick()

      expect(router.currentRoute.value.hash).toBe('')
      expect(wrapper.findAll('.paper-review-rail__queue-row').length).toBe(0)
      expect(wrapper.find('[data-testid="paper-review-main"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="paper-review-empty"]').exists()).toBe(false)
      const landmark = wrapper.get('[data-testid="paper-review-view"]').element
      expect(landmark.getAttribute('tabindex')).toBe('-1')
      expect(document.activeElement).toBe(landmark)

      ;(landmark as HTMLElement).blur()
      await nextTick()
      expect(landmark.getAttribute('tabindex')).toBeNull()
    } finally {
      wrapper.unmount()
    }
  })
})
