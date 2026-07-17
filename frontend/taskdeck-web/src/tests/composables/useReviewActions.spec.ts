import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref, computed, nextTick } from 'vue'
import { useReviewActions } from '../../composables/useReviewActions'
import { automationApi } from '../../api/automationApi'
import { proposalRevisionsApi } from '../../api/proposalRevisionsApi'
import type { Proposal as ApiProposal } from '../../types/automation'

vi.mock('../../api/automationApi', () => ({
  automationApi: {
    approveProposal: vi.fn(),
    rejectProposal: vi.fn(),
    executeProposal: vi.fn(),
    dismissProposals: vi.fn(),
    getProposalDiff: vi.fn(),
  },
}))

vi.mock('../../api/proposalRevisionsApi', () => ({
  proposalRevisionsApi: {
    getRevisions: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  }),
}))

vi.mock('../../composables/usePerformanceMark', () => ({
  usePerformanceMark: () => ({
    start: vi.fn(),
    end: vi.fn(),
  }),
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

describe('useReviewActions', () => {
  let proposals: ReturnType<typeof ref<ApiProposal[]>>
  let dismissableIds: ReturnType<typeof computed<string[]>>
  let loadProposals: ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.clearAllMocks()
    proposals = ref([makeProposal()])
    dismissableIds = computed(() => [])
    loadProposals = vi.fn().mockResolvedValue(undefined)
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([])
  })

  it('should approve a proposal and update the list', async () => {
    const updated = makeProposal({ status: 'Approved' })
    vi.mocked(automationApi.approveProposal).mockResolvedValue(updated)

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleApproveProposal('p-1')

    expect(automationApi.approveProposal).toHaveBeenCalledWith('p-1')
    expect(proposals.value[0].status).toBe('Approved')
    expect(actions.proposalActionBusyId.value).toBeNull()
  })

  it('should handle approve error gracefully', async () => {
    vi.mocked(automationApi.approveProposal).mockRejectedValue(new Error('Network error'))

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleApproveProposal('p-1')

    expect(actions.proposalActionBusyId.value).toBeNull()
  })

  it('should reject a proposal with Low risk and no reason', async () => {
    const updated = makeProposal({ status: 'Rejected' })
    vi.mocked(automationApi.rejectProposal).mockResolvedValue(updated)
    vi.spyOn(globalThis, 'prompt').mockReturnValue('')

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleRejectProposal('p-1', 'Low')

    expect(automationApi.rejectProposal).toHaveBeenCalledWith('p-1', null)
  })

  it('should abort reject when prompt is cancelled', async () => {
    vi.spyOn(globalThis, 'prompt').mockReturnValue(null)

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleRejectProposal('p-1', 'Low')

    expect(automationApi.rejectProposal).not.toHaveBeenCalled()
  })

  it('should require reason for High risk proposals', async () => {
    vi.spyOn(globalThis, 'prompt').mockReturnValue('')

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleRejectProposal('p-1', 'High')

    expect(automationApi.rejectProposal).not.toHaveBeenCalled()
  })

  it('should accept reason for High risk proposals', async () => {
    const updated = makeProposal({ status: 'Rejected' })
    vi.mocked(automationApi.rejectProposal).mockResolvedValue(updated)
    vi.spyOn(globalThis, 'prompt').mockReturnValue('Not needed')

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleRejectProposal('p-1', 'High')

    expect(automationApi.rejectProposal).toHaveBeenCalledWith('p-1', 'Not needed')
  })

  it('should abort execute when confirm is cancelled', async () => {
    vi.spyOn(globalThis, 'confirm').mockReturnValue(false)

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleExecuteProposal('p-1')

    expect(automationApi.executeProposal).not.toHaveBeenCalled()
  })

  it('should execute a proposal when confirmed', async () => {
    const updated = makeProposal({ status: 'Applied' })
    vi.mocked(automationApi.executeProposal).mockResolvedValue(updated)
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleExecuteProposal('p-1')

    expect(automationApi.executeProposal).toHaveBeenCalled()
    expect(proposals.value[0].status).toBe('Applied')
  })

  it('should toggle diff on', async () => {
    vi.mocked(automationApi.getProposalDiff).mockResolvedValue('diff content')

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')

    expect(actions.selectedDiffProposalId.value).toBe('p-1')
    expect(actions.selectedDiff.value).toBe('diff content')
  })

  it('should toggle diff off when same proposal is selected', async () => {
    vi.mocked(automationApi.getProposalDiff).mockResolvedValue('diff content')

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')
    await actions.handleToggleDiff('p-1')

    expect(actions.selectedDiffProposalId.value).toBeNull()
    expect(actions.selectedDiff.value).toBeNull()
    expect(actions.selectedDiffMode.value).toBeNull()
  })

  // --- #1397: read-only / expired proposals never fire the live diff ---

  it('presents the stored preview without fetching for an expired proposal', async () => {
    proposals.value = [
      makeProposal({ id: 'p-1', status: 'Expired', diffPreview: 'stored diff text' }),
    ]

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')

    expect(automationApi.getProposalDiff).not.toHaveBeenCalled()
    expect(actions.selectedDiffProposalId.value).toBe('p-1')
    expect(actions.selectedDiffMode.value).toBe('stored')
    expect(actions.selectedDiff.value).toBe('stored diff text')
  })

  it('presents the stored mode with no content when a read-only proposal has no stored preview', async () => {
    proposals.value = [makeProposal({ id: 'p-1', status: 'Applied', diffPreview: null })]

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')

    expect(automationApi.getProposalDiff).not.toHaveBeenCalled()
    expect(actions.selectedDiffMode.value).toBe('stored')
    expect(actions.selectedDiff.value).toBeNull()
  })

  it('classifies a proposal as read-only via the injected expiry rule (client-side clock)', async () => {
    // Status is still PendingReview, but the surface reports it expired (its
    // expiresAt passed mid-session). It must be treated as read-only, not fetched.
    proposals.value = [
      makeProposal({ id: 'p-1', status: 'PendingReview', diffPreview: 'stored' }),
    ]

    const actions = useReviewActions(
      proposals,
      dismissableIds,
      loadProposals,
      () => true, // isProposalExpired
    )
    await actions.handleToggleDiff('p-1')

    expect(automationApi.getProposalDiff).not.toHaveBeenCalled()
    expect(actions.selectedDiffMode.value).toBe('stored')
    expect(actions.selectedDiff.value).toBe('stored')
  })

  it('renders the invalid verdict with the backend reason when /diff returns a 400 ValidationError', async () => {
    proposals.value = [makeProposal({ id: 'p-1', status: 'PendingReview' })]
    vi.mocked(automationApi.getProposalDiff).mockRejectedValue({
      response: { status: 400, data: { errorCode: 'ValidationError', message: 'Proposal must contain at least one operation' } },
    })

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')

    expect(automationApi.getProposalDiff).toHaveBeenCalledWith('p-1')
    // The pane stays open on this proposal, presenting the invalid verdict with
    // the backend's ACTUAL reason (#1397 MEDIUM-1).
    expect(actions.selectedDiffProposalId.value).toBe('p-1')
    expect(actions.selectedDiffMode.value).toBe('invalid')
    expect(actions.selectedDiffInvalidReason.value).toBe('Proposal must contain at least one operation')
    expect(actions.selectedDiff.value).toBeNull()
  })

  it('carries the backend expiry reason for a 400 on the expiry race, not the zero-op copy', async () => {
    // The 60s review clock can lag a server-side expiry: the proposal still
    // classifies live client-side, the live diff fires, and the backend answers
    // 400 "Proposal has expired". The presentation must carry THAT reason —
    // mislabeling it as a zero-op structure problem is factually wrong
    // (#1397 MEDIUM-1).
    proposals.value = [makeProposal({ id: 'p-1', status: 'PendingReview' })]
    vi.mocked(automationApi.getProposalDiff).mockRejectedValue({
      response: { status: 400, data: { errorCode: 'ValidationError', message: 'Proposal has expired' } },
    })

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')

    expect(actions.selectedDiffMode.value).toBe('invalid')
    expect(actions.selectedDiffInvalidReason.value).toBe('Proposal has expired')
    expect(actions.selectedDiffInvalidReason.value).not.toContain('operation')
  })

  it('marks the stored preview as revised when the proposal has saved revisions', async () => {
    // diffPreview is creation-time content revisions never update: a revised
    // terminal proposal's stored preview shows the ORIGINAL submission, so the
    // banner must disclose the revision (#1397 MEDIUM-2).
    proposals.value = [
      makeProposal({ id: 'p-1', status: 'Applied', diffPreview: 'original ops' }),
    ]
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([
      {
        id: 'rev-1', proposalId: 'p-1', revisionNumber: 1, editorUserId: 'u-1',
        revisedPayload: '{"operations":[]}', revisedAt: new Date().toISOString(),
        reason: 'edit', createdAt: new Date().toISOString(),
      },
    ])

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')
    // Settle the fire-and-forget revision fetch.
    await Promise.resolve()
    await Promise.resolve()

    expect(actions.selectedDiffMode.value).toBe('stored')
    expect(actions.selectedDiffRevised.value).toBe(true)
  })

  it('leaves the revised flag unknown (null) when the revision fetch fails', async () => {
    proposals.value = [
      makeProposal({ id: 'p-1', status: 'Expired', diffPreview: 'stored' }),
    ]
    vi.mocked(proposalRevisionsApi.getRevisions).mockRejectedValue(new Error('boom'))

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')
    await Promise.resolve()
    await Promise.resolve()

    // The stored preview still renders; the disclosure just stays generic.
    expect(actions.selectedDiffMode.value).toBe('stored')
    expect(actions.selectedDiff.value).toBe('stored')
    expect(actions.selectedDiffRevised.value).toBeNull()
  })

  it('re-derives an open live pane to the stored presentation when the proposal turns read-only', async () => {
    // #1397 LOW-5: a proposal can flip to terminal/expired WHILE its live pane is
    // open (status change or clock tick). The pane must switch to the stored
    // read-only presentation instead of keeping a live-looking diff.
    proposals.value = [
      makeProposal({ id: 'p-1', status: 'PendingReview', diffPreview: 'stored preview' }),
    ]
    vi.mocked(automationApi.getProposalDiff).mockResolvedValue('live diff')

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')
    expect(actions.selectedDiffMode.value).toBe('live')
    expect(actions.selectedDiff.value).toBe('live diff')

    // The proposal turns terminal (e.g. a refresh maps in an Applied state).
    proposals.value = [
      makeProposal({ id: 'p-1', status: 'Applied', diffPreview: 'stored preview' }),
    ]
    await nextTick()

    expect(actions.selectedDiffMode.value).toBe('stored')
    expect(actions.selectedDiff.value).toBe('stored preview')
  })

  it('tears the pane down and toasts for a non-validation diff error (e.g. 404)', async () => {
    proposals.value = [makeProposal({ id: 'p-1', status: 'PendingReview' })]
    vi.mocked(automationApi.getProposalDiff).mockRejectedValue({
      response: { status: 404, data: { errorCode: 'NotFound', message: 'Proposal not found' } },
    })

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')

    expect(actions.selectedDiffProposalId.value).toBeNull()
    expect(actions.selectedDiffMode.value).toBeNull()
    expect(actions.selectedDiff.value).toBeNull()
  })

  it('should dismiss a proposal successfully', async () => {
    vi.mocked(automationApi.dismissProposals).mockResolvedValue({ dismissed: 1 })

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleDismissProposal('p-1')

    expect(proposals.value).toHaveLength(0)
  })

  it('should reload when dismiss returns 0', async () => {
    vi.mocked(automationApi.dismissProposals).mockResolvedValue({ dismissed: 0 })

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleDismissProposal('p-1')

    expect(loadProposals).toHaveBeenCalled()
  })

  it('should show info when no dismissable proposals', async () => {
    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleDismissApplied()

    expect(automationApi.dismissProposals).not.toHaveBeenCalled()
  })

  it('should dismiss all applied proposals', async () => {
    const p1 = makeProposal({ id: 'p-1' })
    const p2 = makeProposal({ id: 'p-2' })
    proposals.value = [p1, p2]
    dismissableIds = computed(() => ['p-1'])
    vi.mocked(automationApi.dismissProposals).mockResolvedValue({ dismissed: 1 })

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleDismissApplied()

    expect(proposals.value).toHaveLength(1)
    expect(proposals.value[0].id).toBe('p-2')
  })

  it('should reload when partial dismiss occurs', async () => {
    dismissableIds = computed(() => ['p-1', 'p-2'])
    vi.mocked(automationApi.dismissProposals).mockResolvedValue({ dismissed: 1 })

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleDismissApplied()

    expect(loadProposals).toHaveBeenCalled()
  })
})
