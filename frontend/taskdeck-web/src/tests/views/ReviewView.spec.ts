import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Proposal } from '../../types/automation'
import ReviewView from '../../views/ReviewView.vue'

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((innerResolve, innerReject) => {
    resolve = innerResolve
    reject = innerReject
  })

  return { promise, resolve, reject }
}

const mocks = vi.hoisted(() => ({
  getProposals: vi.fn(),
  approveProposal: vi.fn(),
  rejectProposal: vi.fn(),
  executeProposal: vi.fn(),
  getProposalDiff: vi.fn(),
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
      { path: '/workspace/review', name: 'workspace-review', component: ReviewView },
      { path: '/workspace/inbox', name: 'workspace-inbox', component: { template: '<div />' } },
      {
        path: '/workspace/automations',
        redirect: (to) => ({
          name: 'workspace-review',
          hash: to.hash,
          query: to.query,
        }),
      },
      {
        path: '/workspace/automations/proposals',
        redirect: (to) => ({
          name: 'workspace-review',
          hash: to.hash,
          query: to.query,
        }),
      },
    ],
  })

  await router.push(path)
  await router.isReady()

  const wrapper = mount(ReviewView, {
    attachTo: document.body,
    global: {
      plugins: [router],
    },
  })

  await Promise.resolve()
  await Promise.resolve()
  await wrapper.vm.$nextTick()

  mountedWrapper = wrapper
  return { wrapper, router }
}

let mountedWrapper: ReturnType<typeof mount> | null = null
let originalScrollIntoView: typeof HTMLElement.prototype.scrollIntoView
let originalPrompt: typeof window.prompt

describe('ReviewView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    originalScrollIntoView = HTMLElement.prototype.scrollIntoView
    originalPrompt = window.prompt
    mocks.getProposals.mockResolvedValue([])
    mocks.approveProposal.mockResolvedValue(buildProposal({ status: 'Approved' }))
    mocks.rejectProposal.mockResolvedValue(buildProposal({ status: 'Rejected' }))
    mocks.executeProposal.mockResolvedValue(buildProposal({ status: 'Applied' }))
    mocks.getProposalDiff.mockResolvedValue('diff')
    mocks.createRequestId.mockReturnValue('request-1')
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null

    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      writable: true,
      value: originalScrollIntoView,
    })

    window.prompt = originalPrompt
  })

  it('shows guided empty-state actions when there are no proposals', async () => {
    const { wrapper } = await mountAt('/workspace/review')

    expect(wrapper.text()).toContain('No proposals need review yet')
    expect(wrapper.text()).toContain('Go to Inbox')
    expect(wrapper.text()).toContain('Open Boards')
    expect(wrapper.text()).toContain('Back to Home')
  })

  it('renders capture provenance and canonical review links', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-99',
        sourceType: 'Queue',
        sourceReferenceId: 'capture-99',
        correlationId: 'triage-run-99',
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    expect(mocks.getProposals).toHaveBeenCalledWith({ limit: 200 })
    expect(wrapper.text()).toContain('Capture-linked')
    expect(wrapper.text()).toContain('Triage run: triage-run-99')
    expect(wrapper.find('a[href="/workspace/inbox#capture-capture-99"]').exists()).toBe(true)
    expect(wrapper.find('a[href="/workspace/review#proposal-proposal-99"]').exists()).toBe(true)
  })

  it('redirects legacy proposal routes to the canonical review route', async () => {
    const { router } = await mountAt('/workspace/automations/proposals#proposal-proposal-42')

    expect(router.currentRoute.value.fullPath).toBe('/workspace/review#proposal-proposal-42')
  })

  it('preserves query and hash when the automations alias redirects to review', async () => {
    const { router } = await mountAt('/workspace/automations?boardId=board-7#proposal-proposal-42')

    expect(router.currentRoute.value.fullPath).toBe('/workspace/review?boardId=board-7#proposal-proposal-42')
  })

  it('scrolls to the targeted proposal card when the hash matches a proposal id', async () => {
    mocks.getProposals.mockResolvedValue([buildProposal({ id: 'proposal-42' })])
    const scrollSpy = vi.fn()
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      writable: true,
      value: scrollSpy,
    })

    await mountAt('/workspace/review#proposal-proposal-42')

    expect(scrollSpy).toHaveBeenCalled()
  })

  it('keeps the newest diff response when requests resolve out of order', async () => {
    const proposalA = buildProposal({ id: 'proposal-a', summary: 'Proposal A' })
    const proposalB = buildProposal({ id: 'proposal-b', summary: 'Proposal B' })
    const diffA = createDeferred<string>()
    const diffB = createDeferred<string>()

    mocks.getProposals.mockResolvedValue([proposalA, proposalB])
    mocks.getProposalDiff.mockImplementation((proposalId: string) =>
      proposalId === 'proposal-a' ? diffA.promise : diffB.promise)

    const { wrapper } = await mountAt('/workspace/review')

    await wrapper.get('#proposal-proposal-a').findAll('button')[0]!.trigger('click')
    await wrapper.get('#proposal-proposal-b').findAll('button')[0]!.trigger('click')
    expect(wrapper.find('.td-review-card__diff').exists()).toBe(false)

    diffB.resolve('diff-b')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(wrapper.get('#proposal-proposal-b').text()).toContain('diff-b')

    diffA.resolve('diff-a')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('diff-b')
    expect(wrapper.text()).not.toContain('diff-a')
  })

  it('ignores stale diff errors after a newer proposal is selected', async () => {
    const proposalA = buildProposal({ id: 'proposal-a', summary: 'Proposal A' })
    const proposalB = buildProposal({ id: 'proposal-b', summary: 'Proposal B' })
    const diffA = createDeferred<string>()
    const diffB = createDeferred<string>()

    mocks.getProposals.mockResolvedValue([proposalA, proposalB])
    mocks.getProposalDiff.mockImplementation((proposalId: string) =>
      proposalId === 'proposal-a' ? diffA.promise : diffB.promise)

    const { wrapper } = await mountAt('/workspace/review')

    await wrapper.get('#proposal-proposal-a').findAll('button')[0]!.trigger('click')
    await wrapper.get('#proposal-proposal-b').findAll('button')[0]!.trigger('click')

    diffB.resolve('diff-b')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    diffA.reject(new Error('late failure'))
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('diff-b')
    expect(mocks.errorToast).not.toHaveBeenCalled()
  })

  it('does not reject a proposal when the rejection prompt is cancelled', async () => {
    mocks.getProposals.mockResolvedValue([buildProposal()])
    window.prompt = vi.fn(() => null)

    const { wrapper } = await mountAt('/workspace/review')
    const rejectButton = wrapper.get('#proposal-proposal-1').findAll('button')[2]!

    await rejectButton.trigger('click')
    await Promise.resolve()

    expect(mocks.rejectProposal).not.toHaveBeenCalled()
    expect(mocks.errorToast).not.toHaveBeenCalled()
  })

  it('sends null when an optional rejection reason is left blank', async () => {
    mocks.getProposals.mockResolvedValue([buildProposal()])
    window.prompt = vi.fn(() => '   ')

    const { wrapper } = await mountAt('/workspace/review')
    const rejectButton = wrapper.get('#proposal-proposal-1').findAll('button')[2]!

    await rejectButton.trigger('click')
    await Promise.resolve()

    expect(mocks.rejectProposal).toHaveBeenCalledWith('proposal-1', null)
  })
})
