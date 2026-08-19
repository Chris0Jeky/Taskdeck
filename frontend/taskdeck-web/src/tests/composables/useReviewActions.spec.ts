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
    getProposal: vi.fn(),
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

  // --- #1818: phase-2 execute is gated by the in-app dialog, not confirm() ---

  it('requesting execute opens the confirmation without calling the API', async () => {
    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    actions.requestExecuteProposal('p-1')
    await nextTick()

    expect(actions.executeConfirmProposalId.value).toBe('p-1')
    expect(actions.executeConfirmProposal.value?.id).toBe('p-1')
    // The invariant this test exists for: opening the gate must NOT execute.
    expect(automationApi.executeProposal).not.toHaveBeenCalled()
  })

  it('should abort execute when the confirmation is cancelled', async () => {
    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    actions.requestExecuteProposal('p-1')
    actions.cancelExecuteProposal()
    await actions.confirmExecuteProposal()

    expect(actions.executeConfirmProposalId.value).toBeNull()
    expect(automationApi.executeProposal).not.toHaveBeenCalled()
  })

  it('should never use the native confirm() for execute', async () => {
    const confirmSpy = vi.spyOn(globalThis, 'confirm').mockReturnValue(true)
    vi.mocked(automationApi.executeProposal).mockResolvedValue(makeProposal({ status: 'Applied' }))

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    actions.requestExecuteProposal('p-1')
    await actions.confirmExecuteProposal()

    expect(confirmSpy).not.toHaveBeenCalled()
    confirmSpy.mockRestore()
  })

  it('should execute a proposal when confirmed', async () => {
    const updated = makeProposal({ status: 'Applied' })
    vi.mocked(automationApi.executeProposal).mockResolvedValue(updated)

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    actions.requestExecuteProposal('p-1')
    await actions.confirmExecuteProposal()

    expect(automationApi.executeProposal).toHaveBeenCalled()
    expect(proposals.value[0].status).toBe('Applied')
    expect(actions.executeConfirmProposalId.value).toBeNull()
  })

  it('closes the confirmation without executing when the proposal leaves the list', async () => {
    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    actions.requestExecuteProposal('p-1')
    proposals.value = []
    await nextTick()

    expect(actions.executeConfirmProposalId.value).toBeNull()
    await actions.confirmExecuteProposal()
    expect(automationApi.executeProposal).not.toHaveBeenCalled()
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

  it('retracts the stored preview when the access re-check returns 403 after reveal (#1414 P2 #2)', async () => {
    // Revealing the stored preview re-authorizes via getProposal; a 403/404 means
    // board access was revoked mid-session, so the locally-cached preview is torn
    // down. Rendered synchronously first (the #1397 no-network-gate invariant),
    // then retracted once the async probe reports revoked access.
    proposals.value = [
      makeProposal({ id: 'p-1', status: 'Expired', diffPreview: 'stored diff text' }),
    ]
    vi.mocked(automationApi.getProposal).mockRejectedValue({ response: { status: 403 } })

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')
    // Let the async access probe settle.
    await new Promise((resolve) => setTimeout(resolve))

    expect(automationApi.getProposal).toHaveBeenCalledWith('p-1')
    // Preview retracted once the probe reports revoked access.
    expect(actions.selectedDiffMode.value).toBeNull()
    expect(actions.selectedDiffProposalId.value).toBeNull()
    expect(actions.selectedDiff.value).toBeNull()
  })

  it('keeps the stored preview when the access re-check returns a transient 500 (#1414 P2 #2)', async () => {
    // Only a genuine 403/404 retracts — a transient error must not tear down an
    // inspectable local preview.
    proposals.value = [
      makeProposal({ id: 'p-1', status: 'Expired', diffPreview: 'stored diff text' }),
    ]
    vi.mocked(automationApi.getProposal).mockRejectedValue({ response: { status: 500 } })

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')
    await new Promise((resolve) => setTimeout(resolve))

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

  it('leaves the invalid reason null for a 400 with a blank message so the card fallback applies (#1397 / #1414)', async () => {
    // A ValidationError 400 with an empty/whitespace message previously mapped to
    // the generic "Please check your input" copy, which then MASKED the card's
    // specific "no operations" fallback. Derive the reason as null instead so the
    // caller-level fallback copy applies.
    proposals.value = [makeProposal({ id: 'p-1', status: 'PendingReview' })]
    vi.mocked(automationApi.getProposalDiff).mockRejectedValue({
      response: { status: 400, data: { errorCode: 'ValidationError', message: '   ' } },
    })

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')

    expect(actions.selectedDiffMode.value).toBe('invalid')
    expect(actions.selectedDiffInvalidReason.value).toBeNull()
    expect(actions.selectedDiffInvalidReason.value).not.toBe('Please check your input and try again.')
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

  it('converts an IN-FLIGHT live diff to stored on read-only flip and discards the late response (#1397 round 3)', async () => {
    // While a live /diff is in flight the pane state is id-set + mode null. The
    // read-only watcher must treat that loading state as convertible too: bump
    // the request id (invalidating the pending response) and present the stored
    // state — otherwise the late live response renders live UI on a proposal
    // that is no longer actionable.
    proposals.value = [
      makeProposal({ id: 'p-1', status: 'PendingReview', diffPreview: 'stored preview' }),
    ]
    let resolveDiff: ((value: string) => void) | undefined
    vi.mocked(automationApi.getProposalDiff).mockImplementation(
      () => new Promise<string>((resolve) => { resolveDiff = resolve }),
    )

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    const togglePromise = actions.handleToggleDiff('p-1')
    await nextTick()

    // In flight: pane anchored, no mode yet.
    expect(actions.selectedDiffProposalId.value).toBe('p-1')
    expect(actions.selectedDiffMode.value).toBeNull()

    // The proposal turns terminal while the fetch is pending.
    proposals.value = [
      makeProposal({ id: 'p-1', status: 'Applied', diffPreview: 'stored preview' }),
    ]
    await nextTick()

    // Converted immediately — before the live response lands.
    expect(actions.selectedDiffMode.value).toBe('stored')
    expect(actions.selectedDiff.value).toBe('stored preview')

    // The late live response must be discarded (request id was bumped).
    resolveDiff?.('live diff content')
    await togglePromise
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
