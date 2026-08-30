import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises, enableAutoUnmount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Proposal } from '../../../../types/automation'
import PaperReviewView from '../../../../views/paper/PaperReviewView.vue'
import { REVIEW_QUEUE_REFRESH_MS } from '../../../../composables/useReviewProposals'
import { resetProposalDisplayNamesForTests } from '../../../../composables/useProposalDisplayNames'

/**
 * #2215 A/B — the active proposal must not change under a live reviewer.
 *
 * PR #2208 gave the open Review page a bounded queue poll. A poll that drops or
 * reorders away the row the reviewer is on used to slide the selection onto the
 * next pending proposal: `ReviewMain` is keyed on the proposal id so it was
 * re-created (focus left the decision controls) while the window-level review
 * keymap stayed enabled — so the next keystroke decided a record the reviewer
 * never chose. The same poll could bring a revision another session had saved
 * while an already-rendered diff kept describing the previous one.
 */

const mocks = vi.hoisted(() => ({
  getProposals: vi.fn(),
  getProposal: vi.fn(),
  approveProposal: vi.fn(),
  approveProposals: vi.fn(),
  rejectProposal: vi.fn(),
  deferProposal: vi.fn(),
  executeProposal: vi.fn(),
  getProposalDiff: vi.fn(),
  dismissProposals: vi.fn(),
  reportBadSuggestion: vi.fn(),
  getBoards: vi.fn(),
  getColumns: vi.fn(),
  createRevision: vi.fn(),
  getRevisions: vi.fn(),
  getLatestRevision: vi.fn(),
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
    approveProposals: mocks.approveProposals,
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

vi.mock('../../../../api/workspaceApi', () => ({
  workspaceApi: {
    getCollaboration: vi.fn().mockResolvedValue({ memberCount: 2, hasCollaborators: true }),
  },
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
    getConfidence: vi.fn().mockResolvedValue({
      overall: 0.8,
      components: [],
      note: null,
      threshold: null,
      source: 'model-reported',
      meetsThreshold: null,
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

vi.mock('../../../../store/workspaceStore', () => ({
  useWorkspaceStore: () => ({ refreshWorkloadCounts: mocks.refreshWorkloadCounts }),
}))

enableAutoUnmount(afterEach)

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
        proposalId: overrides.id ?? 'proposal-001',
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
    latestRevisionId: null,
    ...overrides,
  }
}

async function mountView(proposals: Proposal[], options: { attachTo?: boolean } = {}) {
  mocks.getProposals.mockResolvedValue(proposals)
  mocks.getBoards.mockResolvedValue([])
  mocks.getColumns.mockResolvedValue([])
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/workspace/review', name: 'workspace-review', component: PaperReviewView }],
  })
  router.push('/workspace/review')
  await router.isReady()

  const wrapper = mount(PaperReviewView, {
    global: { plugins: [router] },
    ...(options.attachTo ? { attachTo: document.body } : {}),
  })
  await flushPromises()
  return wrapper
}

/** Land one background queue poll answering with `next`. */
async function pollWith(wrapper: ReturnType<typeof mount>, next: Proposal[]) {
  mocks.getProposals.mockResolvedValue(next)
  vi.advanceTimersByTime(REVIEW_QUEUE_REFRESH_MS)
  await flushPromises()
  await wrapper.vm.$nextTick()
}

beforeEach(() => {
  vi.clearAllMocks()
  resetProposalDisplayNamesForTests()
  mocks.sessionState.userId = 'u-1'
  mocks.getRevisions.mockResolvedValue([])
  mocks.getProposal.mockResolvedValue(makeProposal())
})

describe('PaperReviewView — active proposal stability under polling (#2215 A)', () => {
  it('does not promote the next row when a poll takes the active proposal', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    try {
      const wrapper = await mountView([
        makeProposal({ id: 'active-1', summary: 'Under review right now' }),
        makeProposal({ id: 'next-2', summary: 'The one that must not be promoted' }),
      ])
      expect(wrapper.text()).toContain('Under review right now')

      // Another session rejects `active-1`; the poll's answer no longer carries it.
      await pollWith(wrapper, [makeProposal({ id: 'next-2', summary: 'The one that must not be promoted' })])

      expect(wrapper.find('[data-testid="paper-review-settled-elsewhere"]').exists()).toBe(true)
      expect(wrapper.text()).toContain('This proposal is no longer pending.')
      // The decision column must NOT have swapped to the next queue item.
      expect(wrapper.find('[data-testid="paper-review-main"]').exists()).toBe(false)
    } finally {
      vi.useRealTimers()
    }
  })

  it('announces the notice in a live region', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    try {
      const wrapper = await mountView([
        makeProposal({ id: 'active-1' }),
        makeProposal({ id: 'next-2' }),
      ])
      await pollWith(wrapper, [makeProposal({ id: 'next-2' })])

      const live = wrapper.find('[data-testid="paper-review-empty"] p[role="status"]')
      expect(live.exists()).toBe(true)
      expect(live.attributes('aria-live')).toBe('polite')
    } finally {
      vi.useRealTimers()
    }
  })

  it('silences the review keymap so a stray key cannot decide the promoted proposal', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    try {
      const wrapper = await mountView([
        makeProposal({ id: 'active-1' }),
        makeProposal({ id: 'next-2' }),
      ])
      await pollWith(wrapper, [makeProposal({ id: 'next-2' })])

      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }))
      await flushPromises()

      // This is the whole point of the notice: ⏎ was aimed at `active-1`.
      expect(mocks.approveProposal).not.toHaveBeenCalled()
    } finally {
      vi.useRealTimers()
    }
  })

  it('moves focus to the notice control, and returning restores the ordinary selection', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    try {
      const wrapper = await mountView(
        [makeProposal({ id: 'active-1' }), makeProposal({ id: 'next-2', summary: 'Now selectable' })],
        { attachTo: true },
      )
      await pollWith(wrapper, [makeProposal({ id: 'next-2', summary: 'Now selectable' })])

      const returnButton = wrapper.find('[data-testid="paper-review-settled-elsewhere-return"]')
      expect(returnButton.exists()).toBe(true)
      expect(document.activeElement).toBe(returnButton.element)

      await returnButton.trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="paper-review-settled-elsewhere"]').exists()).toBe(false)
      expect(wrapper.text()).toContain('Now selectable')
    } finally {
      vi.useRealTimers()
    }
  })

  it('leaves the selection alone when the poll keeps the active proposal', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    try {
      const wrapper = await mountView([
        makeProposal({ id: 'active-1', summary: 'Under review right now' }),
        makeProposal({ id: 'next-2' }),
      ])

      await pollWith(wrapper, [
        makeProposal({ id: 'active-1', summary: 'Under review right now' }),
        makeProposal({ id: 'next-2' }),
        makeProposal({ id: 'new-3' }),
      ])

      // A poll that only ADDS work must stay invisible to the reviewer's focus.
      expect(wrapper.find('[data-testid="paper-review-settled-elsewhere"]').exists()).toBe(false)
      expect(wrapper.text()).toContain('Under review right now')
    } finally {
      vi.useRealTimers()
    }
  })
})

describe('PaperReviewView — diff keyed on latestRevisionId (#2215 B)', () => {
  it('drops an open diff when a poll brings a newer revision for the same proposal', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    try {
      mocks.getProposalDiff.mockResolvedValue('--- before\n+++ after\n+Add column "Done"')
      const wrapper = await mountView([makeProposal({ id: 'diff-1', latestRevisionId: null })])

      window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
      await flushPromises()
      expect(wrapper.find('[data-testid="paper-review-diff-pre"]').exists()).toBe(true)

      // A collaborator saves a revision. The rendered diff was computed for the
      // PREVIOUS one, while Approve pins and Apply executes the server's latest.
      await pollWith(wrapper, [makeProposal({ id: 'diff-1', latestRevisionId: 'rev-2' })])

      expect(wrapper.find('[data-testid="paper-review-diff"]').exists()).toBe(false)
    } finally {
      vi.useRealTimers()
    }
  })

  it('keeps an open diff when a poll brings the same revision', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    try {
      mocks.getProposalDiff.mockResolvedValue('--- before\n+++ after\n+Add column "Done"')
      const wrapper = await mountView([makeProposal({ id: 'diff-1', latestRevisionId: 'rev-1' })])

      window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', cancelable: true }))
      await flushPromises()
      expect(wrapper.find('[data-testid="paper-review-diff-pre"]').exists()).toBe(true)

      await pollWith(wrapper, [makeProposal({ id: 'diff-1', latestRevisionId: 'rev-1' })])

      // An unchanged revision must not blink the pane the reviewer is reading.
      expect(wrapper.find('[data-testid="paper-review-diff-pre"]').exists()).toBe(true)
    } finally {
      vi.useRealTimers()
    }
  })
})
