import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, type Ref } from 'vue'
import { useReviewActions } from '../../composables/useReviewActions'
import { automationApi } from '../../api/automationApi'
import { proposalRevisionsApi } from '../../api/proposalRevisionsApi'
import type { ToastLabel, ToastOptions } from '../../store/toastStore'
import type { Proposal as ApiProposal } from '../../types/automation'

/**
 * Which outcome word the review decisions stamp on their toast (GH-1970).
 *
 * `PaperToastContainer.spec.ts` proves the RENDERER turns a label into the
 * right localized word and falls back to a severity word without one. This
 * file proves the other half — that the two decisions which have a real
 * outcome word name it, and that `applied` stays reserved for the one action
 * that actually writes a board.
 *
 * It lives apart from `useReviewActions.spec.ts` because that file's toast
 * mock builds fresh `vi.fn()`s on every `useToastStore()` call, so nothing it
 * records can be inspected.
 */

const toastMocks = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../api/automationApi', () => ({
  automationApi: {
    approveProposal: vi.fn(),
    rejectProposal: vi.fn(),
    deferProposal: vi.fn(),
    executeProposal: vi.fn(),
    dismissProposals: vi.fn(),
    getProposalDiff: vi.fn(),
    getProposal: vi.fn(),
  },
}))

vi.mock('../../api/proposalRevisionsApi', () => ({
  proposalRevisionsApi: {
    getRevisions: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../composables/usePerformanceMark', () => ({
  usePerformanceMark: () => ({ start: vi.fn(), end: vi.fn() }),
}))

function makeProposal(overrides: Partial<ApiProposal> = {}): ApiProposal {
  return {
    id: 'p-1',
    status: 'Pending',
    riskLevel: 'Low',
    title: 'Test proposal',
    description: 'desc',
    captureItemId: 'c-1',
    boardId: 'b-1',
    changes: [],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  } as ApiProposal
}

/** The `label` each recorded `toast.success(...)` carried, `undefined` if none. */
function successLabels(): Array<ToastLabel | undefined> {
  return toastMocks.success.mock.calls.map((call) => (call[2] as ToastOptions | undefined)?.label)
}

describe('useReviewActions outcome labels', () => {
  let proposals: Ref<ApiProposal[]>
  let actions: ReturnType<typeof useReviewActions>

  beforeEach(() => {
    vi.clearAllMocks()
    proposals = ref([makeProposal()])
    const dismissableIds = computed(() => ['p-1'])
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([])
    actions = useReviewActions(proposals, dismissableIds, vi.fn().mockResolvedValue(undefined))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('stamps an approval APPROVED, never APPLIED', async () => {
    vi.mocked(automationApi.approveProposal).mockResolvedValue(makeProposal({ status: 'Approved' }))

    await actions.handleApproveProposal('p-1')

    // Approve is phase 1 of 2. The board has NOT been written, and the pane
    // says so in the same breath — the stamp must agree with it.
    expect(toastMocks.success).toHaveBeenCalledWith(
      'Proposal approved for board application',
      undefined,
      { label: 'approved' },
    )
    expect(successLabels()).not.toContain('applied')
  })

  it('stamps a confirmed execute APPLIED', async () => {
    vi.mocked(automationApi.executeProposal).mockResolvedValue(makeProposal({ status: 'Applied' }))

    actions.requestExecuteProposal('p-1')
    await actions.confirmExecuteProposal()

    expect(toastMocks.success).toHaveBeenCalledWith('Proposal applied to board', undefined, {
      label: 'applied',
    })
  })

  it('reserves APPLIED for execute — no other review decision claims it', async () => {
    // `applied` is opt-in per caller, so the guard that keeps it honest is
    // that every OTHER success path leaves it alone. Reject/snooze/dismiss and
    // the bulk clear name no action word at all and degrade to "Done".
    vi.mocked(automationApi.rejectProposal).mockResolvedValue(makeProposal({ status: 'Rejected' }))
    // A snooze leaves the proposal pending — only its deferredUntil moves.
    vi.mocked(automationApi.deferProposal).mockResolvedValue(
      makeProposal({ status: 'PendingReview' }),
    )
    vi.mocked(automationApi.dismissProposals).mockResolvedValue({ dismissed: 1 })

    actions.requestRejectProposal('p-1')
    await actions.confirmRejectProposal('not useful')
    await actions.handleDeferProposal('p-1')
    proposals.value = [makeProposal()]
    await actions.handleDismissProposal('p-1')
    proposals.value = [makeProposal()]
    await actions.handleDismissApplied()

    expect(toastMocks.success.mock.calls.length).toBeGreaterThanOrEqual(4)
    expect(successLabels()).not.toContain('applied')
    expect(successLabels()).not.toContain('approved')
  })
})
