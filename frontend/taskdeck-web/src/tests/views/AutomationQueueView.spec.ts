import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Proposal } from '../../types/automation'
import AutomationQueueView from '../../views/AutomationQueueView.vue'

const mocks = vi.hoisted(() => ({
  getProposals: vi.fn(),
  approveProposal: vi.fn(),
  rejectProposal: vi.fn(),
  executeProposal: vi.fn(),
  getProposalDiff: vi.fn(),
  fetchByStatus: vi.fn(),
  fetchStats: vi.fn(),
  submitRequest: vi.fn(),
  cancelRequest: vi.fn(),
  processNext: vi.fn(),
  successToast: vi.fn(),
  errorToast: vi.fn(),
  createRequestId: vi.fn(),
}))

vi.mock('../../api/automationApi', () => ({
  automationApi: {
    getProposals: mocks.getProposals,
    approveProposal: mocks.approveProposal,
    rejectProposal: mocks.rejectProposal,
    executeProposal: mocks.executeProposal,
    getProposalDiff: mocks.getProposalDiff,
  },
}))

vi.mock('../../store/queueStore', () => ({
  useQueueStore: () => ({
    stats: {
      pendingCount: 1,
      processingCount: 0,
      completedCount: 0,
      failedCount: 0,
      cancelledCount: 0,
    },
    loading: false,
    requests: [],
    fetchByStatus: mocks.fetchByStatus,
    fetchStats: mocks.fetchStats,
    submitRequest: mocks.submitRequest,
    cancelRequest: mocks.cancelRequest,
    processNext: mocks.processNext,
  }),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.successToast,
    error: mocks.errorToast,
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
  return {
    id: 'proposal-1',
    sourceType: 'Queue',
    sourceReferenceId: 'capture-1',
    boardId: 'board-1',
    requestedByUserId: 'user-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: 'Queue proposal',
    diffPreview: null,
    validationIssues: null,
    createdAt: now,
    updatedAt: now,
    expiresAt: now,
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'triage-run-1',
    operations: [],
    ...overrides,
  }
}

async function mountAt(path: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/workspace/automations/queue', name: 'workspace-automations-queue', component: AutomationQueueView },
      { path: '/workspace/automations/proposals', name: 'workspace-automations-proposals', component: AutomationQueueView },
    ],
  })

  await router.push(path)
  await router.isReady()

  const wrapper = mount(AutomationQueueView, {
    attachTo: document.body,
    global: {
      plugins: [router],
    },
  })

  await Promise.resolve()
  await Promise.resolve()
  await wrapper.vm.$nextTick()
  return wrapper
}

describe('AutomationQueueView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.fetchByStatus.mockResolvedValue(undefined)
    mocks.fetchStats.mockResolvedValue(undefined)
    mocks.submitRequest.mockResolvedValue(undefined)
    mocks.cancelRequest.mockResolvedValue(undefined)
    mocks.processNext.mockResolvedValue(undefined)
    mocks.approveProposal.mockResolvedValue(buildProposal({ status: 'Approved' }))
    mocks.rejectProposal.mockResolvedValue(buildProposal({ status: 'Rejected' }))
    mocks.executeProposal.mockResolvedValue(buildProposal({ status: 'Applied' }))
    mocks.getProposalDiff.mockResolvedValue('diff')
    mocks.createRequestId.mockReturnValue('request-1')
  })

  it('shows capture and triage provenance context for queue proposals', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-99',
        sourceType: 'Queue',
        sourceReferenceId: 'capture-99',
        correlationId: 'triage-run-99',
      }),
    ])

    const wrapper = await mountAt('/workspace/automations/proposals')

    expect(mocks.getProposals).toHaveBeenCalledWith({ limit: 200 })
    expect(wrapper.text()).toContain('Capture-linked')
    expect(wrapper.text()).toContain('Triage run: triage-run-99')
    expect(wrapper.find('a[href="/workspace/inbox#capture-capture-99"]').exists()).toBe(true)
    expect(wrapper.find('a[href="/workspace/automations/proposals#proposal-proposal-99"]').exists()).toBe(true)
  })

  it('does not render capture link for non-queue proposals', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-manual',
        sourceType: 'Manual',
        sourceReferenceId: null,
        correlationId: 'manual-correlation',
      }),
    ])

    const wrapper = await mountAt('/workspace/automations/proposals')

    expect(wrapper.text()).not.toContain('Capture-linked')
    expect(wrapper.text()).not.toContain('Open Capture')
    expect(wrapper.text()).not.toContain('Triage run:')
  })

  it('scrolls to proposal card when route hash targets a proposal id', async () => {
    mocks.getProposals.mockResolvedValue([buildProposal({ id: 'proposal-42' })])
    const scrollSpy = vi.fn()
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: scrollSpy,
    })

    await mountAt('/workspace/automations/proposals#proposal-proposal-42')

    expect(mocks.getProposals).toHaveBeenCalled()
    expect(scrollSpy).toHaveBeenCalled()
  })

  it('does not attempt proposal scrolling when no proposal hash is present', async () => {
    mocks.getProposals.mockResolvedValue([buildProposal({ id: 'proposal-42' })])
    const scrollSpy = vi.fn()
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: scrollSpy,
    })

    await mountAt('/workspace/automations/proposals')

    expect(scrollSpy).not.toHaveBeenCalled()
  })
})
