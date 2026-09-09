import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Proposal } from '../../../../types/automation'
import PaperReviewView from '../../../../views/paper/PaperReviewView.vue'
import { REVIEW_QUEUE_REFRESH_MS } from '../../../../composables/useReviewProposals'

const mocks = vi.hoisted(() => ({
  getProposals: vi.fn(),
  getProposal: vi.fn(),
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
    approveProposal: vi.fn(),
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

vi.mock('../../../../api/boardsApi', () => ({
  boardsApi: { getBoards: mocks.getBoards },
}))

vi.mock('../../../../api/columnsApi', () => ({
  columnsApi: { getColumns: mocks.getColumns },
}))

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

function makeProposal(overrides: Partial<Proposal> = {}): Proposal {
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
    operations: [
      {
        id: 'op-1',
        proposalId: 'proposal-active',
        sequence: 0,
        actionType: 'CreateCard',
        targetType: 'Card',
        targetId: null,
        parameters: '{}',
        idempotencyKey: 'key-1',
        expectedVersion: null,
      },
    ],
    approvedRevisionId: null,
    latestRevisionId: null,
    ...overrides,
  }
}

async function mountView(
  initialProposals: Proposal[],
  followUpProposals: Proposal[],
  path: string,
) {
  mocks.getProposals.mockResolvedValueOnce(initialProposals)
  mocks.getProposals.mockResolvedValue(followUpProposals)
  mocks.getBoards.mockResolvedValue([])
  mocks.getColumns.mockResolvedValue([])

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/workspace/review', name: 'workspace-review', component: PaperReviewView }],
  })
  await router.push(path)
  await router.isReady()
  const wrapper = mount(PaperReviewView, {
    attachTo: document.body,
    global: { plugins: [router] },
  })
  await flushPromises()
  return wrapper
}

function routerOf(wrapper: { vm: unknown }) {
  return (wrapper.vm as {
    $router: {
      push: (path: string) => Promise<void>
    }
  }).$router
}

async function settleActiveProposal(wrapper: {
  find: (selector: string) => { exists: () => boolean }
}) {
  vi.advanceTimersByTime(REVIEW_QUEUE_REFRESH_MS)
  await flushPromises()
  await nextTick()
  expect(wrapper.find('[data-testid="paper-review-settled-elsewhere"]').exists()).toBe(true)
}

describe('PaperReviewView unavailable deep-link return focus', () => {
  enableAutoUnmount(afterEach)

  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    mocks.sessionState.userId = 'u-1'
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
      reversibility: { summary: 'Low risk', description: 'Confirm before applying.', windowMs: 60_000 },
    })
    mocks.getConflicts.mockResolvedValue([])
    mocks.getHistory.mockResolvedValue([])
    mocks.getSimilarPast.mockResolvedValue({ decisions: [], applyRate: 0 })
    mocks.getProvenanceMetadata.mockResolvedValue({ provider: null, model: null, promptVersion: null })
    mocks.getCaptureItem.mockRejectedValue(new Error('capture unavailable'))
    mocks.getRevisions.mockResolvedValue([])
    mocks.getLatestRevision.mockResolvedValue(null)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it.each([
    { label: 'missing', status: 404, id: 'proposal-missing' },
    { label: 'malformed', status: 400, id: 'proposal-not-a-guid' },
  ])('replaces a settled notice with a $label panel and returns in one click', async ({ status, id }) => {
    const active = makeProposal()
    const replacement = makeProposal({ id: 'proposal-replacement', summary: 'Replacement proposal' })
    mocks.getProposal.mockRejectedValue({ response: { status } })
    const wrapper = await mountView([active], [replacement], '/workspace/review')
    try {
      await settleActiveProposal(wrapper)
      await routerOf(wrapper).push(`/workspace/review#proposal-${id}`)
      await flushPromises()
      await nextTick()

      const unavailableReturn = wrapper.get('[data-testid="paper-review-unavailable-return"]')
      expect(wrapper.find('[data-testid="paper-review-settled-elsewhere"]').exists()).toBe(false)
      expect(document.activeElement).toBe(unavailableReturn.element)
      expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(false)

      await unavailableReturn.trigger('click')
      await flushPromises()
      await nextTick()

      expect((wrapper.vm as { $route: { hash: string } }).$route.hash).toBe('')
      expect(wrapper.find('[data-testid="paper-review-settled-elsewhere"]').exists()).toBe(false)
      expect(wrapper.get('[data-testid="paper-review-main"]').text()).toContain('Replacement proposal')
      expect(document.activeElement).toBe(
        wrapper.get('.paper-review-rail__queue-row button').element,
      )
    } finally {
      wrapper.unmount()
    }
  })

  it('returns focus to the empty state after a malformed link leaves no queue', async () => {
    mocks.getProposal.mockRejectedValue({ response: { status: 400 } })
    const wrapper = await mountView(
      [makeProposal()],
      [],
      '/workspace/review',
    )
    try {
      await settleActiveProposal(wrapper)
      await routerOf(wrapper).push('/workspace/review#proposal-not-a-guid')
      await flushPromises()
      await nextTick()

      const unavailableReturn = wrapper.get('[data-testid="paper-review-unavailable-return"]')
      expect(document.activeElement).toBe(unavailableReturn.element)

      await unavailableReturn.trigger('click')
      await flushPromises()
      await nextTick()

      const empty = wrapper.get('[data-testid="paper-review-empty"]').element
      expect(wrapper.find('[data-testid="paper-review-settled-elsewhere"]').exists()).toBe(false)
      expect(wrapper.findAll('[data-testid="paper-review-main"]').length).toBe(0)
      expect(empty.getAttribute('tabindex')).toBe('-1')
      expect(document.activeElement).toBe(empty)
    } finally {
      wrapper.unmount()
    }
  })

  it('keeps a successful deep-link lookup actionable after a settled notice', async () => {
    const active = makeProposal()
    const target = makeProposal({ id: 'proposal-target', summary: 'Target proposal' })
    mocks.getProposal.mockResolvedValueOnce(target)
    const wrapper = await mountView([active], [], '/workspace/review')
    try {
      await settleActiveProposal(wrapper)
      await routerOf(wrapper).push('/workspace/review#proposal-proposal-target')
      await flushPromises()
      await nextTick()

      expect(wrapper.find('[data-testid="paper-review-settled-elsewhere"]').exists()).toBe(false)
      expect(wrapper.get('[data-testid="paper-review-main"]').text()).toContain('Target proposal')
      expect(wrapper.findAll('[data-testid="decision-apply"]').length).toBe(1)
    } finally {
      wrapper.unmount()
    }
  })
})
