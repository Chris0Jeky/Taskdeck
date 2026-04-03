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
    summary: 'Queue proposal',
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
      plainSummary: 'Queue proposal This would create card "Queue proposal".',
      impactSummary: '1 planned change touching 1 target surface.',
      riskCue: 'Low risk. Usually safe to review quickly.',
      sourceCue: 'Created from Inbox capture triage.',
      operationHeadlines: ['Create card "Queue proposal".'],
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
        ? {
            ...base.presentation!,
            ...overrides.presentation,
          }
        : overrides.presentation
      : base.presentation,
  }

  if (!hasPresentationOverride && overrides.summary && merged.presentation) {
    merged.presentation = {
      ...merged.presentation,
      plainSummary: `${overrides.summary} This would create card "${overrides.summary}".`,
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
    localStorage.clear()
    originalScrollIntoView = HTMLElement.prototype.scrollIntoView
    originalPrompt = window.prompt
    mocks.getProposals.mockResolvedValue([])
    mocks.getBoards.mockResolvedValue([
      { id: 'board-1', name: 'Engineering Sprint' },
      { id: 'board-7', name: 'Support Triage' },
      { id: 'board-12', name: 'Content Calendar' },
      { id: 'board-99', name: 'Archived Board' },
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

    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      writable: true,
      value: originalScrollIntoView,
    })

    window.prompt = originalPrompt
  })

  it('shows guided empty-state actions when there are no proposals', async () => {
    const { wrapper } = await mountAt('/workspace/review')

    expect(wrapper.text()).toContain('What is Review for?')
    expect(wrapper.text()).toContain('Nothing changes on a board until you approve it here.')
    expect(wrapper.text()).toContain('No proposals need review yet')
    expect(wrapper.text()).toContain('Go to Inbox')
    expect(wrapper.text()).toContain('Open Boards')
    expect(wrapper.text()).toContain('Back to Home')
  })

  it('uses trust-first review labels and board-apply action wording', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-review-needed',
        status: 'PendingReview',
      }),
      buildProposal({
        id: 'proposal-ready',
        status: 'Approved',
        sourceReferenceId: 'capture-2',
      }),
      buildProposal({
        id: 'proposal-applied',
        status: 'Applied',
        sourceReferenceId: 'capture-3',
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    // Actionable proposals visible by default
    expect(wrapper.text()).toContain('Review required')
    expect(wrapper.text()).toContain('Approved, ready to apply')
    expect(wrapper.text()).toContain('Approve for board')
    expect(wrapper.text()).toContain('Apply to board')
    // Two-step flow indicator replaces the old inline action cue
    expect(wrapper.text()).not.toContain('Changes stay in review until you approve them.')
    const flowSteps = wrapper.find('.td-review-card__flow-steps')
    expect(flowSteps.exists()).toBe(true)
    expect(flowSteps.text()).toContain('Approve')
    expect(flowSteps.text()).toContain('Apply to board')
    expect(flowSteps.attributes('role')).toBe('list')
    const activeStep = flowSteps.find('[aria-current="step"]')
    expect(activeStep.exists()).toBe(true)
    expect(activeStep.text()).toContain('Approve')

    // Applied proposals hidden by default (showCompleted is off)
    expect(wrapper.text()).not.toContain('Applied to board')

    // Toggle showCompleted on to reveal applied proposals
    const toggle = wrapper.find('.td-review__toggle-input')
    await toggle.setValue(true)
    expect(wrapper.text()).toContain('Applied to board')
  })

  it('renders capture provenance and canonical review links', async () => {
    const fullCorrelationId = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890'
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-99',
        sourceType: 'Queue',
        sourceReferenceId: 'capture-99',
        correlationId: fullCorrelationId,
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    expect(mocks.getProposals).toHaveBeenCalledWith({ limit: 200 })
    // "Capture-linked" chip is visible on the collapsed toggle
    expect(wrapper.text()).toContain('Capture-linked')

    // Expand the Technical details section to see provenance content
    const provenanceToggle = wrapper.findAll('.td-review-card__collapse-toggle').find((btn) => btn.text().includes('Technical details'))
    expect(provenanceToggle).toBeDefined()
    await provenanceToggle!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Triage run: a1b2c3d4...')
    expect(wrapper.text()).not.toContain(fullCorrelationId)
    const triageSpan = wrapper.find('.td-review-card__provenance-meta')
    expect(triageSpan.attributes('title')).toBe(fullCorrelationId)

    // Open the Links dropdown to find provenance links
    const linksBtn = wrapper.findAll('.td-btn--secondary').find((btn) => btn.text().includes('Links'))
    expect(linksBtn).toBeDefined()
    await linksBtn!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('a[href="/workspace/inbox?boardId=board-1#capture-capture-99"]').exists()).toBe(true)
    expect(wrapper.find('a[href="/workspace/review?boardId=board-1#proposal-proposal-99"]').exists()).toBe(true)
  })

  it('keeps board context on capture provenance links when review is board-scoped', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-99',
        boardId: 'board-7',
        sourceReferenceId: 'capture-99',
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review?boardId=board-7')

    // Expand the Technical details section
    const provenanceToggle = wrapper.findAll('.td-review-card__collapse-toggle').find((btn) => btn.text().includes('Technical details'))
    expect(provenanceToggle).toBeDefined()
    await provenanceToggle!.trigger('click')
    await wrapper.vm.$nextTick()

    // Open the Links dropdown
    const linksBtn = wrapper.findAll('.td-btn--secondary').find((btn) => btn.text().includes('Links'))
    expect(linksBtn).toBeDefined()
    await linksBtn!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('a[href="/workspace/inbox?boardId=board-7#capture-capture-99"]').exists()).toBe(true)
  })

  it('clears boardless proposal hashes on board-scoped review routes', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-1',
        boardId: 'board-7',
        summary: 'Scoped board proposal',
      }),
    ])
    mocks.getProposal.mockResolvedValue(
      buildProposal({
        id: 'proposal-boardless',
        boardId: null,
        summary: 'Boardless proposal',
      }),
    )

    const { wrapper, router } = await mountAt('/workspace/review?boardId=board-7#proposal-proposal-boardless')
    await new Promise((resolve) => setTimeout(resolve, 0))
    await wrapper.vm.$nextTick()

    expect(mocks.getProposal).toHaveBeenCalledWith('proposal-boardless')
    expect(router.currentRoute.value.fullPath).toBe('/workspace/review?boardId=board-7')
    expect(wrapper.find('#proposal-proposal-boardless').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Boardless proposal')
    expect(mocks.errorToast).not.toHaveBeenCalled()
  })

  it('requests board-scoped proposals when the review route carries a board query', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        boardId: 'board-7',
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review?boardId=board-7')

    expect(mocks.getProposals).toHaveBeenCalledWith({ limit: 200, boardId: 'board-7' })
    expect(wrapper.text()).toContain('Support Triage')
  })

  it('hydrates board-scoped proposal hashes that fall outside the first page', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-1',
        boardId: 'board-7',
        summary: 'Newest board proposal',
      }),
      buildProposal({
        id: 'proposal-2',
        boardId: 'board-7',
        summary: 'Second board proposal',
      }),
    ])
    mocks.getProposal.mockResolvedValue(
      buildProposal({
        id: 'proposal-older',
        boardId: 'board-7',
        summary: 'Older board proposal',
      }),
    )

    const { wrapper } = await mountAt('/workspace/review?boardId=board-7#proposal-proposal-older')
    await new Promise((resolve) => setTimeout(resolve, 0))
    await wrapper.vm.$nextTick()

    expect(mocks.getProposal).toHaveBeenCalledWith('proposal-older')
    expect(wrapper.text()).toContain('Older board proposal')
    expect(wrapper.find('#proposal-proposal-older').exists()).toBe(true)
    expect(wrapper.text()).toContain('Support Triage')
  })

  it('clears stale proposal hashes when the fetched proposal belongs to a different board', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-1',
        boardId: 'board-7',
        summary: 'Scoped board proposal',
      }),
    ])
    mocks.getProposal.mockResolvedValue(
      buildProposal({
        id: 'proposal-older',
        boardId: 'board-99',
        summary: 'Wrong board proposal',
      }),
    )

    const { wrapper, router } = await mountAt('/workspace/review?boardId=board-7#proposal-proposal-older')
    await new Promise((resolve) => setTimeout(resolve, 0))
    await wrapper.vm.$nextTick()

    expect(mocks.getProposal).toHaveBeenCalledWith('proposal-older')
    expect(router.currentRoute.value.fullPath).toBe('/workspace/review?boardId=board-7')
    expect(wrapper.find('#proposal-proposal-older').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Wrong board proposal')
    expect(mocks.errorToast).not.toHaveBeenCalled()
  })

  it('clears stale proposal hashes when the target proposal cannot be fetched', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-1',
        boardId: 'board-7',
        summary: 'Scoped board proposal',
      }),
    ])
    mocks.getProposal.mockRejectedValue({
      response: {
        status: 404,
      },
    })

    const { wrapper, router } = await mountAt('/workspace/review?boardId=board-7#proposal-proposal-older')
    await new Promise((resolve) => setTimeout(resolve, 0))
    await wrapper.vm.$nextTick()

    expect(mocks.getProposal).toHaveBeenCalledWith('proposal-older')
    expect(router.currentRoute.value.fullPath).toBe('/workspace/review?boardId=board-7')
    expect(mocks.errorToast).not.toHaveBeenCalled()
  })

  it('keeps the proposal hash and surfaces an error when proposal hydration fails unexpectedly', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-1',
        boardId: 'board-7',
        summary: 'Scoped board proposal',
      }),
    ])
    mocks.getProposal.mockRejectedValue({
      response: {
        status: 500,
      },
    })

    const { router } = await mountAt('/workspace/review?boardId=board-7#proposal-proposal-older')
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(mocks.getProposal).toHaveBeenCalledWith('proposal-older')
    expect(router.currentRoute.value.fullPath).toBe('/workspace/review?boardId=board-7#proposal-proposal-older')
    expect(mocks.errorToast).toHaveBeenCalledWith('Failed to load proposal')
  })

  it('clears cached proposal hashes that no longer match the active board filter', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-1',
        boardId: 'board-7',
        summary: 'Newest board proposal',
      }),
    ])
    mocks.getProposal.mockResolvedValue(
      buildProposal({
        id: 'proposal-older',
        boardId: 'board-7',
        summary: 'Older board proposal',
      }),
    )

    const { wrapper, router } = await mountAt('/workspace/review?boardId=board-7#proposal-proposal-older')
    await new Promise((resolve) => setTimeout(resolve, 0))
    await wrapper.vm.$nextTick()

    mocks.getProposals.mockRejectedValueOnce(new Error('board list failed'))
    await router.push('/workspace/review?boardId=board-9#proposal-proposal-older')
    await new Promise((resolve) => setTimeout(resolve, 0))
    await wrapper.vm.$nextTick()

    expect(router.currentRoute.value.fullPath).toBe('/workspace/review?boardId=board-9')
    expect(wrapper.find('#proposal-proposal-older').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Older board proposal')
  })

  it('preserves board context when opening inbox from a board-scoped review route', async () => {
    const { wrapper, router } = await mountAt('/workspace/review?boardId=board-7')
    const pushSpy = vi.spyOn(router, 'push')

    const openInboxButton = wrapper.find('.td-review__hero-actions').findAll('button').find((node) => node.text() === 'Open Inbox')
    await openInboxButton?.trigger('click')
    await Promise.resolve()

    expect(pushSpy).toHaveBeenCalledWith('/workspace/inbox?boardId=board-7')
  })

  it('keeps the newest proposal load when board-scoped requests resolve out of order', async () => {
    const initialLoad = createDeferred<Proposal[]>()
    const boardScopedLoad = createDeferred<Proposal[]>()

    mocks.getProposals.mockImplementation((query?: { boardId?: string }) => {
      if (query?.boardId === 'board-7') {
        return boardScopedLoad.promise
      }

      return initialLoad.promise
    })

    const { wrapper, router } = await mountAt('/workspace/review')
    expect(mocks.getProposals).toHaveBeenCalledWith({ limit: 200 })

    await router.push('/workspace/review?boardId=board-7')
    await router.isReady()

    expect(mocks.getProposals).toHaveBeenLastCalledWith({ limit: 200, boardId: 'board-7' })

    boardScopedLoad.resolve([
      buildProposal({
        id: 'proposal-board-7',
        boardId: 'board-7',
        summary: 'Board 7 proposal',
      }),
    ])
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Board 7 proposal')
    expect(wrapper.text()).toContain('Support Triage')

    initialLoad.resolve([
      buildProposal({
        id: 'proposal-stale',
        boardId: 'board-1',
        summary: 'Stale workspace proposal',
      }),
    ])
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Board 7 proposal')
    expect(wrapper.text()).not.toContain('Stale workspace proposal')
  })

  it('renders readable presentation cues and board follow-through actions', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        boardId: 'board-12',
        presentation: {
          plainSummary: 'Rename the support board and create a follow-up card.',
          impactSummary: '2 changes touching 2 target surfaces.',
          riskCue: 'Medium risk. Check the affected items before approving.',
          sourceCue: 'Created from an automation chat session.',
          operationHeadlines: [
            'Rename board "Support".',
            'Create card "Send update".',
          ],
          affectedEntities: [
            { entityType: 'Board', entityId: 'board-12', label: 'Board board-12', changeCount: 1 },
            { entityType: 'Card', entityId: 'card-12', label: 'Card card-12', changeCount: 1 },
          ],
        },
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    // Title and impact cue are always visible
    expect(wrapper.text()).toContain('Rename the support board and create a follow-up card.')
    expect(wrapper.text()).toContain('2 changes touching 2 target surfaces.')

    // Affected cards are collapsed by default -- expand to verify
    const entitiesToggle = wrapper.findAll('.td-review-card__collapse-toggle').find((btn) => btn.text().includes('Affected cards'))
    expect(entitiesToggle).toBeDefined()
    await entitiesToggle!.trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('Board board-12 · 1 change')

    // Planned changes are collapsed by default -- expand to verify
    const operationsToggle = wrapper.findAll('.td-review-card__collapse-toggle').find((btn) => btn.text().includes('Planned changes'))
    expect(operationsToggle).toBeDefined()
    await operationsToggle!.trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('Rename board "Support".')
  })

  it('redirects legacy proposal routes to the canonical review route', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-42',
      }),
    ])

    const { router } = await mountAt('/workspace/automations/proposals#proposal-proposal-42')

    expect(router.currentRoute.value.fullPath).toBe('/workspace/review#proposal-proposal-42')
  })

  it('preserves query and hash when the automations alias redirects to review', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-42',
        boardId: 'board-7',
      }),
    ])

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

    await wrapper.get('#proposal-proposal-a').findAll('button').find((node) => node.text() === 'View Diff')!.trigger('click')
    await wrapper.get('#proposal-proposal-b').findAll('button').find((node) => node.text() === 'View Diff')!.trigger('click')
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
    void diffA.promise.catch(() => {})

    const { wrapper } = await mountAt('/workspace/review')

    await wrapper.get('#proposal-proposal-a').findAll('button').find((node) => node.text() === 'View Diff')!.trigger('click')
    await wrapper.get('#proposal-proposal-b').findAll('button').find((node) => node.text() === 'View Diff')!.trigger('click')

    diffB.resolve('diff-b')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    diffA.reject(new Error('late failure'))
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('diff-b')
    expect(mocks.errorToast).not.toHaveBeenCalled()
  })

  it('shows board name instead of raw ID in the board filter label', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({ boardId: 'board-7' }),
    ])

    const { wrapper } = await mountAt('/workspace/review?boardId=board-7')

    expect(wrapper.text()).toContain('Support Triage')
    expect(wrapper.text()).not.toContain('board-7')
    expect(wrapper.text()).toContain('Show all boards')
  })

  it('falls back to raw board ID when the board is not in the loaded list', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({ boardId: 'board-unknown' }),
    ])

    const { wrapper } = await mountAt('/workspace/review?boardId=board-unknown')

    expect(wrapper.text()).toContain('board-unknown')
  })

  it('renders a board filter selector (InputAssistField)', async () => {
    const { wrapper } = await mountAt('/workspace/review')

    const selector = wrapper.find('.td-review__board-selector')
    expect(selector.exists()).toBe(true)
    expect(selector.find('input').exists()).toBe(true)
  })

  it('clears the board filter when "Show all boards" is clicked', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({ boardId: 'board-7' }),
    ])

    const { wrapper, router } = await mountAt('/workspace/review?boardId=board-7')
    const pushSpy = vi.spyOn(router, 'push')

    const clearButton = wrapper.findAll('button').find((node) => node.text() === 'Show all boards')
    await clearButton?.trigger('click')
    await Promise.resolve()

    expect(pushSpy).toHaveBeenCalledWith({ name: 'workspace-review' })
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
    const rejectButton = wrapper.get('#proposal-proposal-1').findAll('button').find((node) => node.text() === 'Reject')!

    await rejectButton.trigger('click')
    await Promise.resolve()

    expect(mocks.rejectProposal).toHaveBeenCalledWith('proposal-1', null)
  })

  it('shows Expired status badge for proposals with domain Expired status', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-expired',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    // Must show "Expired" status, not "Approved, ready to apply"
    expect(wrapper.text()).toContain('Expired')
    expect(wrapper.text()).not.toContain('Approved, ready to apply')
    // Must show expired notice
    expect(wrapper.text()).toContain('This proposal has expired and can no longer be applied')
    // Must have the expired CSS class
    expect(wrapper.find('.td-review-status--expired').exists()).toBe(true)
  })

  it('detects client-side expiry for Approved proposals past expiresAt', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-approved-expired',
        status: 'Approved',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    // Should show as Expired even though domain status is Approved
    expect(wrapper.find('.td-review-status--expired').exists()).toBe(true)
    expect(wrapper.text()).toContain('Expired')
    expect(wrapper.text()).not.toContain('Approved, ready to apply')
    // Apply to board button should NOT be available
    expect(wrapper.text()).toContain('This proposal has expired and can no longer be applied')

    // Expired action footer should be rendered for Approved+expired proposals
    const card = wrapper.get('#proposal-proposal-approved-expired')
    expect(card.text()).not.toContain('Apply to board')
    expect(card.text()).toContain('Dismiss')
  })

  it('detects client-side expiry for PendingReview proposals past expiresAt', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-pending-expired',
        status: 'PendingReview',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    expect(wrapper.find('.td-review-status--expired').exists()).toBe(true)
    expect(wrapper.text()).toContain('Expired')
    expect(wrapper.text()).not.toContain('Review required')
    // Approve/Reject buttons should not be rendered for expired proposals
    const card = wrapper.get('#proposal-proposal-pending-expired')
    expect(card.text()).not.toContain('Approve for board')
    expect(card.text()).not.toContain('Reject')
  })

  it('does not show Apply to board button for expired proposals', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-expired-no-apply',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')
    const card = wrapper.get('#proposal-proposal-expired-no-apply')

    // Should not have an Apply button
    const applyBtn = card.findAll('button').find((btn) => btn.text() === 'Apply to board')
    expect(applyBtn).toBeUndefined()
  })

  it('shows Dismiss button for expired proposals', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-expired-dismiss',
        status: 'Expired',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')
    const card = wrapper.get('#proposal-proposal-expired-dismiss')

    const dismissBtn = card.findAll('button').find((btn) => btn.text() === 'Dismiss')
    expect(dismissBtn).toBeDefined()
  })

  it('dismisses an expired proposal and removes it from the list', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-to-dismiss',
        status: 'Expired',
        summary: 'Expired proposal to dismiss',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
      }),
      buildProposal({
        id: 'proposal-still-active',
        status: 'PendingReview',
        summary: 'Active proposal',
        expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')
    expect(wrapper.text()).toContain('Expired proposal to dismiss')

    const card = wrapper.get('#proposal-proposal-to-dismiss')
    const dismissBtn = card.findAll('button').find((btn) => btn.text() === 'Dismiss')!
    await dismissBtn.trigger('click')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(mocks.dismissProposals).toHaveBeenCalledWith(['proposal-to-dismiss'])
    expect(wrapper.text()).not.toContain('Expired proposal to dismiss')
    expect(wrapper.text()).toContain('Active proposal')
    expect(mocks.successToast).toHaveBeenCalledWith('Proposal dismissed')
  })

  it('removes a client-side-expired proposal from view when backend returns dismissed 0', async () => {
    mocks.dismissProposals.mockResolvedValue({ dismissed: 0 })
    mocks.getProposals
      .mockResolvedValueOnce([
        buildProposal({
          id: 'proposal-stuck',
          status: 'Approved',
          summary: 'Stuck expired proposal',
          expiresAt: new Date(Date.now() - 60_000).toISOString(),
        }),
      ])
      .mockResolvedValueOnce([])

    const { wrapper } = await mountAt('/workspace/review')
    expect(wrapper.text()).toContain('Stuck expired proposal')

    const card = wrapper.get('#proposal-proposal-stuck')
    const dismissBtn = card.findAll('button').find((btn) => btn.text() === 'Dismiss')!
    await dismissBtn.trigger('click')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(mocks.dismissProposals).toHaveBeenCalledWith(['proposal-stuck'])
    // Should be removed from the local list even though backend returned 0
    expect(wrapper.text()).not.toContain('Stuck expired proposal')
    expect(mocks.infoToast).toHaveBeenCalledWith('Proposal removed from view. Refreshing...')
  })

  it('does not count expired proposals in pending or ready summary cards', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-active-pending',
        status: 'PendingReview',
        expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      }),
      buildProposal({
        id: 'proposal-expired-pending',
        status: 'PendingReview',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
      }),
      buildProposal({
        id: 'proposal-expired-approved',
        status: 'Approved',
        sourceReferenceId: 'capture-x',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    const summaryCards = wrapper.findAll('.td-review-summary-card')
    // "Pending review" card should only count 1 (the active one)
    const pendingCard = summaryCards.find((c) => c.text().includes('Pending review'))
    expect(pendingCard?.text()).toContain('1')
    // "Ready to execute" card should count 0 (the approved one is expired)
    const readyCard = summaryCards.find((c) => c.text().includes('Ready to execute'))
    expect(readyCard?.text()).toContain('0')
  })

  it('does not treat non-expired Approved proposals as expired', async () => {
    mocks.getProposals.mockResolvedValue([
      buildProposal({
        id: 'proposal-approved-active',
        status: 'Approved',
        sourceReferenceId: 'capture-2',
        expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      }),
    ])

    const { wrapper } = await mountAt('/workspace/review')

    expect(wrapper.find('.td-review-status--approved').exists()).toBe(true)
    expect(wrapper.text()).toContain('Approved, ready to apply')
    expect(wrapper.text()).not.toContain('This proposal has expired')
  it('renders human-readable diff content with operation details label', async () => {
    const readableDiff = '0. Create card "Fix login bug" in column "To Do"\n1. Move card "Setup CI" to column "In Progress"'
    mocks.getProposals.mockResolvedValue([buildProposal()])
    mocks.getProposalDiff.mockResolvedValue(readableDiff)

    const { wrapper } = await mountAt('/workspace/review')

    const viewDiffBtn = wrapper.get('#proposal-proposal-1').findAll('button').find((node) => node.text() === 'View Diff')!
    await viewDiffBtn.trigger('click')
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Fix login bug')
    expect(wrapper.text()).toContain('To Do')
    expect(wrapper.text()).toContain('Setup CI')
    expect(wrapper.text()).toContain('In Progress')
    expect(wrapper.text()).toContain('Operation details')
    expect(wrapper.find('.td-review-card__diff').exists()).toBe(true)
    expect(wrapper.find('.td-review-card__diff-label').exists()).toBe(true)
  })
})
