import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref, computed } from 'vue'
import { useReviewActions } from '../../composables/useReviewActions'
import { automationApi } from '../../api/automationApi'
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

  it('renders the invalid verdict (not a cleared pane) when /diff returns a 400 ValidationError', async () => {
    proposals.value = [makeProposal({ id: 'p-1', status: 'PendingReview' })]
    vi.mocked(automationApi.getProposalDiff).mockRejectedValue({
      response: { status: 400, data: { errorCode: 'ValidationError', message: 'Proposal must contain at least one operation' } },
    })

    const actions = useReviewActions(proposals, dismissableIds, loadProposals)
    await actions.handleToggleDiff('p-1')

    expect(automationApi.getProposalDiff).toHaveBeenCalledWith('p-1')
    // The pane stays open on this proposal, presenting the invalid verdict.
    expect(actions.selectedDiffProposalId.value).toBe('p-1')
    expect(actions.selectedDiffMode.value).toBe('invalid')
    expect(actions.selectedDiff.value).toBeNull()
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
