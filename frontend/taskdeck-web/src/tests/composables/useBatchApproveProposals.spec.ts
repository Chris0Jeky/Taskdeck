import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { automationApi } from '../../api/automationApi'
import {
  isBatchApproveEligible,
  useBatchApproveProposals,
} from '../../composables/useBatchApproveProposals'
import type { Proposal } from '../../types/automation'

const toast = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
}))

vi.mock('../../api/automationApi', () => ({
  automationApi: {
    approveProposals: vi.fn(),
    executeProposal: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toast,
}))

const NOW = Date.parse('2026-08-28T12:00:00.000Z')

function makeProposal(overrides: Partial<Proposal> = {}): Proposal {
  return {
    id: 'p-1',
    sourceType: 'Queue',
    sourceReferenceId: null,
    boardId: 'b-1',
    requestedByUserId: 'u-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: 'Create a card',
    diffPreview: null,
    validationIssues: null,
    createdAt: new Date(NOW - 60_000).toISOString(),
    updatedAt: new Date(NOW - 60_000).toISOString(),
    expiresAt: new Date(NOW + 60 * 60_000).toISOString(),
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'corr-1',
    approvedRevisionId: null,
    operations: [
      {
        id: 'op-1',
        proposalId: 'p-1',
        sequence: 0,
        actionType: 'create',
        targetType: 'card',
        targetId: null,
        parameters: '{"title":"Task"}',
        idempotencyKey: 'key-1',
        expectedVersion: null,
      },
    ],
    ...overrides,
  } as Proposal
}

describe('isBatchApproveEligible', () => {
  it('admits only an own, fresh, exact PendingReview/Low, bounded create-card proposal', () => {
    expect(isBatchApproveEligible(makeProposal(), 'u-1', NOW)).toBe(true)
    expect(isBatchApproveEligible(makeProposal({ status: 0, riskLevel: 0 }), 'u-1', NOW)).toBe(true)
  })

  it.each([
    ['another user', { requestedByUserId: 'u-2' }],
    ['unknown status', { status: 'UnknownStatus' }],
    ['approved', { status: 'Approved' }],
    ['unknown risk', { riskLevel: 'UnknownRisk' }],
    ['medium risk', { riskLevel: 'Medium' }],
    ['expired flag', { isExpired: true }],
    ['expired time', { expiresAt: new Date(NOW).toISOString() }],
    ['stale boundary', { createdAt: new Date(NOW - 24 * 60 * 60_000).toISOString() }],
    ['future defer', { deferredUntil: new Date(NOW + 1).toISOString() }],
    ['empty operations', { operations: [] }],
    [
      'mixed operation',
      {
        operations: [
          {
            ...makeProposal().operations[0],
            actionType: 'update',
          },
        ],
      },
    ],
    [
      'malformed operation discriminators',
      {
        operations: [
          {
            ...makeProposal().operations[0],
            actionType: null as unknown as string,
          },
        ],
      },
    ],
    [
      'effective risk above Low',
      {
        operations: Array.from({ length: 6 }, (_, sequence) => ({
          ...makeProposal().operations[0],
          id: `op-${sequence}`,
          sequence,
        })),
      },
    ],
  ])('fails closed for %s', (_name, overrides) => {
    expect(isBatchApproveEligible(makeProposal(overrides as Partial<Proposal>), 'u-1', NOW)).toBe(false)
  })
})

describe('useBatchApproveProposals', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('opens an explicit confirmation without calling either approve or execute', () => {
    const proposals = ref([makeProposal()])
    const actions = useBatchApproveProposals(
      proposals,
      ref('u-1'),
      ref(NOW),
      vi.fn().mockResolvedValue(undefined),
    )

    actions.toggleSelection('p-1')
    actions.requestConfirmation()

    expect(actions.confirmationOpen.value).toBe(true)
    expect(automationApi.approveProposals).not.toHaveBeenCalled()
    expect(automationApi.executeProposal).not.toHaveBeenCalled()
  })

  it('prunes selection drift and refuses the POST from an open confirmation', async () => {
    const proposals = ref([makeProposal()])
    const actions = useBatchApproveProposals(
      proposals,
      ref('u-1'),
      ref(NOW),
      vi.fn().mockResolvedValue(undefined),
    )
    actions.toggleSelection('p-1')
    actions.requestConfirmation()

    proposals.value = [makeProposal({ status: 'Approved' })]
    await actions.confirmApproval()

    expect(actions.confirmationOpen.value).toBe(false)
    expect(actions.selectedCount.value).toBe(0)
    expect(automationApi.approveProposals).not.toHaveBeenCalled()
    expect(automationApi.executeProposal).not.toHaveBeenCalled()
  })

  it('invalidates confirmation when only part of the selected set drifts', async () => {
    const proposals = ref([
      makeProposal({ id: 'p-1' }),
      makeProposal({ id: 'p-2', operations: [{ ...makeProposal().operations[0], proposalId: 'p-2' }] }),
    ])
    vi.mocked(automationApi.approveProposals).mockResolvedValue({ approvedIds: ['p-1'] })
    const actions = useBatchApproveProposals(
      proposals,
      ref('u-1'),
      ref(NOW),
      vi.fn().mockResolvedValue(undefined),
    )
    actions.toggleSelection('p-1')
    actions.toggleSelection('p-2')
    actions.requestConfirmation()

    proposals.value = [makeProposal({ id: 'p-1' }), makeProposal({ id: 'p-2', status: 'Approved' })]
    expect(actions.selectedCount.value).toBe(1)
    expect(actions.confirmationOpen.value).toBe(false)
    expect(toast.info).toHaveBeenCalled()

    await actions.confirmApproval()
    expect(automationApi.approveProposals).not.toHaveBeenCalled()

    actions.requestConfirmation()
    expect(actions.confirmationOpen.value).toBe(true)
    await actions.confirmApproval()
    expect(automationApi.approveProposals).toHaveBeenCalledOnce()
    expect(automationApi.approveProposals).toHaveBeenCalledWith(['p-1'])
    expect(automationApi.executeProposal).not.toHaveBeenCalled()
  })

  it('accepts only an exact receipt and reconciles every row to Approved, never Applied', async () => {
    const proposals = ref([
      makeProposal({ id: 'p-1' }),
      makeProposal({ id: 'p-2', operations: [{ ...makeProposal().operations[0], proposalId: 'p-2' }] }),
    ])
    const loadProposals = vi.fn().mockResolvedValue(undefined)
    vi.mocked(automationApi.approveProposals).mockResolvedValue({ approvedIds: ['p-2', 'p-1'] })
    const actions = useBatchApproveProposals(proposals, ref('u-1'), ref(NOW), loadProposals)
    actions.toggleSelection('p-1')
    actions.toggleSelection('p-2')
    actions.requestConfirmation()

    await actions.confirmApproval()

    expect(automationApi.approveProposals).toHaveBeenCalledWith(['p-1', 'p-2'])
    expect(automationApi.executeProposal).not.toHaveBeenCalled()
    expect(proposals.value.map((proposal) => proposal.status)).toEqual(['Approved', 'Approved'])
    expect(proposals.value.every((proposal) => proposal.status !== 'Applied')).toBe(true)
    expect(proposals.value.every((proposal) => proposal.decidedAt === null)).toBe(true)
    expect(actions.selectedCount.value).toBe(0)
    expect(loadProposals).toHaveBeenCalledOnce()
  })

  it('keeps an authoritative success when the follow-up refresh fails', async () => {
    const proposals = ref([makeProposal()])
    const loadProposals = vi.fn().mockRejectedValue(new Error('refresh unavailable'))
    vi.mocked(automationApi.approveProposals).mockResolvedValue({ approvedIds: ['p-1'] })
    const actions = useBatchApproveProposals(proposals, ref('u-1'), ref(NOW), loadProposals)
    actions.toggleSelection('p-1')
    actions.requestConfirmation()

    await expect(actions.confirmApproval()).resolves.toBeUndefined()

    expect(proposals.value[0]?.status).toBe('Approved')
    expect(toast.success).toHaveBeenCalled()
    expect(toast.error).not.toHaveBeenCalled()
    expect(automationApi.executeProposal).not.toHaveBeenCalled()
  })

  it('does not mutate proposal status when the server receipt is incomplete', async () => {
    const proposals = ref([
      makeProposal({ id: 'p-1' }),
      makeProposal({ id: 'p-2', operations: [{ ...makeProposal().operations[0], proposalId: 'p-2' }] }),
    ])
    const loadProposals = vi.fn().mockResolvedValue(undefined)
    vi.mocked(automationApi.approveProposals).mockResolvedValue({ approvedIds: ['p-1'] })
    const actions = useBatchApproveProposals(proposals, ref('u-1'), ref(NOW), loadProposals)
    actions.toggleSelection('p-1')
    actions.toggleSelection('p-2')
    actions.requestConfirmation()

    await actions.confirmApproval()

    expect(proposals.value.map((proposal) => proposal.status)).toEqual([
      'PendingReview',
      'PendingReview',
    ])
    expect(loadProposals).toHaveBeenCalledOnce()
    expect(toast.error).toHaveBeenCalled()
    expect(automationApi.executeProposal).not.toHaveBeenCalled()
  })
})
