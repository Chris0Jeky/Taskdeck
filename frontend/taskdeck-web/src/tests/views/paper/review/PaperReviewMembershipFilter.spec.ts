import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises, enableAutoUnmount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Proposal } from '../../../../types/automation'
import PaperReviewView from '../../../../views/paper/PaperReviewView.vue'
import { COLLABORATION_REFRESH_THROTTLE_MS } from '../../../../composables/useWorkspaceCollaboration'
import { resetProposalDisplayNamesForTests } from '../../../../composables/useProposalDisplayNames'

/**
 * #1940 — Paper Review must decide whether to offer the All/Mine author
 * partition from the server-computed collaboration-membership contract alone,
 * with explicit loading / unknown / failure semantics that always fail open.
 *
 * Lives beside the main Paper Review suite rather than inside it so the
 * membership contract has one legible home.
 */

const mocks = vi.hoisted(() => ({
  getProposals: vi.fn(),
  getProposal: vi.fn(),
  getBoards: vi.fn(),
  getColumns: vi.fn(),
  getCollaboration: vi.fn(),
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
    rejectProposal: vi.fn(),
    deferProposal: vi.fn(),
    executeProposal: vi.fn(),
    getProposalDiff: vi.fn().mockResolvedValue(null),
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

vi.mock('../../../../api/proposalRevisionsApi', () => ({
  proposalRevisionsApi: {
    createRevision: vi.fn(),
    getRevisions: vi.fn().mockResolvedValue([]),
    getLatestRevision: vi.fn().mockResolvedValue(null),
  },
}))

vi.mock('../../../../api/proposalDeepReviewApi', () => ({
  proposalDeepReviewApi: {
    getProvenance: vi.fn().mockResolvedValue([]),
    getConfidence: vi.fn().mockResolvedValue({
      overall: 0.8,
      components: [],
      note: null,
      threshold: 0.5,
      meetsThreshold: true,
    }),
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

enableAutoUnmount(afterEach)

const STALE_AGE_MS = 3 * 24 * 60 * 60 * 1000

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

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  // Attach a no-op catch so an unresolved rejection cannot surface as an
  // unhandled rejection before the view's own handler runs.
  promise.catch(() => {})
  return { promise, resolve, reject }
}

async function mountView(proposals: Proposal[], path = '/workspace/review') {
  mocks.getProposals.mockResolvedValueOnce(proposals)
  mocks.getBoards.mockResolvedValueOnce([])
  mocks.getColumns.mockResolvedValue([])
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/workspace/review', name: 'workspace-review', component: PaperReviewView }],
  })
  router.push(path)
  await router.isReady()

  const wrapper = mount(PaperReviewView, { global: { plugins: [router] } })
  await flushPromises()
  return wrapper
}

function pillLabels(wrapper: { findAll: (selector: string) => { text: () => string }[] }): string[] {
  return wrapper.findAll('.paper-review-rail__pill').map((pill) => pill.text())
}

describe('PaperReviewView — All/Mine membership contract', () => {
  beforeEach(() => {
    resetProposalDisplayNamesForTests()
    mocks.getProposals.mockReset()
    mocks.getBoards.mockReset()
    mocks.getColumns.mockReset()
    mocks.getCollaboration.mockReset()
    mocks.getProposal.mockReset()
    mocks.sessionState.userId = 'u-1'
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads the collaboration contract once on mount', async () => {
    mocks.getCollaboration.mockResolvedValue({ memberCount: 1, hasCollaborators: false })

    await mountView([makeProposal()])

    expect(mocks.getCollaboration).toHaveBeenCalledTimes(1)
  })

  it('keeps All and Mine while the membership answer is still loading', async () => {
    const pending = deferred<{ memberCount: number; hasCollaborators: boolean }>()
    mocks.getCollaboration.mockReturnValue(pending.promise)

    const wrapper = await mountView([makeProposal()])

    expect(pillLabels(wrapper)).toEqual(['All', 'Mine', 'Stale'])

    pending.resolve({ memberCount: 1, hasCollaborators: false })
    await flushPromises()

    expect(pillLabels(wrapper)).toEqual(['Stale'])
  })

  it('keeps All and Mine when the membership lookup fails', async () => {
    mocks.getCollaboration.mockRejectedValue(new Error('offline'))

    const wrapper = await mountView([makeProposal()])

    expect(pillLabels(wrapper)).toEqual(['All', 'Mine', 'Stale'])
  })

  it('keeps All and Mine when the membership payload cannot be trusted', async () => {
    mocks.getCollaboration.mockResolvedValue({ memberCount: null, hasCollaborators: null })

    const wrapper = await mountView([makeProposal()])

    expect(pillLabels(wrapper)).toEqual(['All', 'Mine', 'Stale'])
  })

  it('hides All and Mine, and keeps Stale, on a proven single-member workspace', async () => {
    mocks.getCollaboration.mockResolvedValue({ memberCount: 1, hasCollaborators: false })

    const wrapper = await mountView([makeProposal()])

    expect(pillLabels(wrapper)).toEqual(['Stale'])
  })

  it('hides All and Mine even when the queue is empty', async () => {
    // Cardinality of the queue is not membership: an empty queue must not be
    // read as "no collaborators", nor prevent a solo answer from applying.
    mocks.getCollaboration.mockResolvedValue({ memberCount: 1, hasCollaborators: false })

    const wrapper = await mountView([])

    expect(pillLabels(wrapper)).toEqual(['Stale'])
  })

  it('keeps All and Mine when the workspace has collaborators', async () => {
    mocks.getCollaboration.mockResolvedValue({ memberCount: 2, hasCollaborators: true })

    const wrapper = await mountView([makeProposal()])

    expect(pillLabels(wrapper)).toEqual(['All', 'Mine', 'Stale'])
  })

  it('restores All and Mine when a solo workspace gains a collaborator', async () => {
    // Only `Date` is faked: the view holds a real 60s interval clock that
    // whole-timer faking would have to be driven around.
    vi.useFakeTimers({ toFake: ['Date'] })
    mocks.getCollaboration.mockResolvedValue({ memberCount: 1, hasCollaborators: false })

    const wrapper = await mountView([makeProposal()])
    expect(pillLabels(wrapper)).toEqual(['Stale'])

    mocks.getCollaboration.mockResolvedValue({ memberCount: 2, hasCollaborators: true })
    vi.setSystemTime(Date.now() + COLLABORATION_REFRESH_THROTTLE_MS + 1)
    document.dispatchEvent(new Event('visibilitychange'))
    await flushPromises()

    expect(mocks.getCollaboration).toHaveBeenCalledTimes(2)
    expect(pillLabels(wrapper)).toEqual(['All', 'Mine', 'Stale'])
  })

  it('falls back from Mine to the whole queue when a late solo answer hides the partition', async () => {
    const pending = deferred<{ memberCount: number; hasCollaborators: boolean }>()
    mocks.getCollaboration.mockReturnValue(pending.promise)

    const mine = makeProposal({ id: 'proposal-mine', requestedByUserId: 'u-1' })
    const theirs = makeProposal({ id: 'proposal-theirs', requestedByUserId: 'u-2' })
    const wrapper = await mountView([mine, theirs])

    await wrapper.findAll('.paper-review-rail__pill')[1].trigger('click')
    await flushPromises()
    expect(wrapper.findAll('.paper-review-q').length).toBe(1)

    pending.resolve({ memberCount: 1, hasCollaborators: false })
    await flushPromises()

    expect(pillLabels(wrapper)).toEqual(['Stale'])
    // The fallback reached the view, not just the rail: both rows are back.
    expect(wrapper.findAll('.paper-review-q').length).toBe(2)
    expect(wrapper.find('[data-testid="paper-review-view"]').exists()).toBe(true)
  })

  it('keeps the deep-linked proposal selected across the fallback', async () => {
    const pending = deferred<{ memberCount: number; hasCollaborators: boolean }>()
    mocks.getCollaboration.mockReturnValue(pending.promise)

    const mine = makeProposal({ id: 'proposal-mine', requestedByUserId: 'u-1' })
    const theirs = makeProposal({ id: 'proposal-theirs', requestedByUserId: 'u-2' })
    const wrapper = await mountView(
      [mine, theirs],
      '/workspace/review#proposal-proposal-mine',
    )

    await wrapper.findAll('.paper-review-rail__pill')[1].trigger('click')
    await flushPromises()

    pending.resolve({ memberCount: 1, hasCollaborators: false })
    await flushPromises()

    // The exact deep-link target is still the active row, and the hash was not
    // rewritten to a fallback proposal.
    expect(wrapper.findAll('.paper-review-q--active').length).toBe(1)
    expect(wrapper.find('.paper-review-q--active').text()).toContain(mine.summary)
  })

  it('does not surrender a deep link to a proposal outside the collapsed filter', async () => {
    const pending = deferred<{ memberCount: number; hasCollaborators: boolean }>()
    mocks.getCollaboration.mockReturnValue(pending.promise)

    const theirs = makeProposal({
      id: 'proposal-theirs',
      requestedByUserId: 'u-2',
      summary: 'Someone else proposed this',
    })
    const wrapper = await mountView(
      [theirs],
      '/workspace/review#proposal-proposal-theirs',
    )

    pending.resolve({ memberCount: 1, hasCollaborators: false })
    await flushPromises()

    expect(pillLabels(wrapper)).toEqual(['Stale'])
    expect(wrapper.find('.paper-review-q--active').text()).toContain(theirs.summary)
  })

  it('does not consult the proposal list or its authors to decide visibility', async () => {
    // The recorded #1940 assumption: author cardinality is not membership. Two
    // distinct authors must not keep the partition alive against a solo answer.
    mocks.getCollaboration.mockResolvedValue({ memberCount: 1, hasCollaborators: false })

    const wrapper = await mountView([
      makeProposal({ id: 'a', requestedByUserId: 'u-1' }),
      makeProposal({ id: 'b', requestedByUserId: 'u-2' }),
      makeProposal({
        id: 'c',
        requestedByUserId: 'u-3',
        createdAt: new Date(Date.now() - STALE_AGE_MS).toISOString(),
      }),
    ])

    expect(pillLabels(wrapper)).toEqual(['Stale'])
  })
})
