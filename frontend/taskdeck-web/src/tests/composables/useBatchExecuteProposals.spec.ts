import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { automationApi } from '../../api/automationApi'
import {
  isBatchExecuteEligible,
  useBatchExecuteProposals,
} from '../../composables/useBatchExecuteProposals'
import type { BatchExecuteProposalsResult, Proposal } from '../../types/automation'

const toast = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
}))

vi.mock('../../api/automationApi', () => ({
  automationApi: {
    executeProposals: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toast,
}))

const NOW = Date.parse('2026-08-30T12:00:00.000Z')

function makeProposal(overrides: Partial<Proposal> = {}): Proposal {
  return {
    id: 'p-1',
    sourceType: 'Queue',
    sourceReferenceId: null,
    boardId: 'b-1',
    requestedByUserId: 'u-1',
    status: 'Approved',
    riskLevel: 'Low',
    summary: 'Create a card',
    diffPreview: null,
    validationIssues: null,
    createdAt: new Date(NOW - 60_000).toISOString(),
    updatedAt: new Date(NOW - 60_000).toISOString(),
    expiresAt: new Date(NOW + 60 * 60_000).toISOString(),
    decidedAt: new Date(NOW - 30_000).toISOString(),
    decidedByUserId: 'u-1',
    appliedAt: null,
    failureReason: null,
    correlationId: 'corr-1',
    approvedRevisionId: null,
    latestRevisionId: null,
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

function harness(proposals: Proposal[]) {
  const rows = ref<Proposal[]>(proposals)
  const loadProposals = vi.fn(async () => {})
  const composable = useBatchExecuteProposals(
    rows,
    ref<string | null>('u-1'),
    ref(NOW),
    loadProposals,
  )
  return { rows, loadProposals, ...composable }
}

function receipt(results: BatchExecuteProposalsResult['results']): BatchExecuteProposalsResult {
  return { results }
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('isBatchExecuteEligible', () => {
  it('admits only the reviewer own, live, exactly-Approved proposal with operations', () => {
    expect(isBatchExecuteEligible(makeProposal(), 'u-1', NOW)).toBe(true)
    expect(isBatchExecuteEligible(makeProposal({ status: 1 }), 'u-1', NOW)).toBe(true)
  })

  it('refuses another reviewer proposal', () => {
    expect(isBatchExecuteEligible(makeProposal({ requestedByUserId: 'u-2' }), 'u-1', NOW)).toBe(false)
    expect(isBatchExecuteEligible(makeProposal(), null, NOW)).toBe(false)
  })

  it('refuses every status that is not exactly Approved, including unknown wire values', () => {
    for (const status of ['PendingReview', 'Applied', 'Rejected', 'Failed', 'Expired', 'Dismissed'] as const) {
      expect(isBatchExecuteEligible(makeProposal({ status }), 'u-1', NOW)).toBe(false)
    }
    // Fail closed: an unrecognised wire value is never read as Approved.
    expect(isBatchExecuteEligible(makeProposal({ status: 'somethingNew' as never }), 'u-1', NOW)).toBe(false)
    expect(isBatchExecuteEligible(makeProposal({ status: 99 as never }), 'u-1', NOW)).toBe(false)
  })

  it('refuses expired, deferred, and zero-operation proposals', () => {
    expect(isBatchExecuteEligible(makeProposal({ isExpired: true }), 'u-1', NOW)).toBe(false)
    expect(isBatchExecuteEligible(
      makeProposal({ expiresAt: new Date(NOW - 1000).toISOString() }), 'u-1', NOW)).toBe(false)
    expect(isBatchExecuteEligible(
      makeProposal({ deferredUntil: new Date(NOW + 60_000).toISOString() }), 'u-1', NOW)).toBe(false)
    expect(isBatchExecuteEligible(makeProposal({ operations: [] }), 'u-1', NOW)).toBe(false)
  })

  it('admits a proposal whose defer window has already elapsed', () => {
    expect(isBatchExecuteEligible(
      makeProposal({ deferredUntil: new Date(NOW - 60_000).toISOString() }), 'u-1', NOW)).toBe(true)
  })
})

describe('useBatchExecuteProposals', () => {
  it('counts only eligible proposals and never posts without an explicit confirmation', async () => {
    const h = harness([
      makeProposal({ id: 'p-1' }),
      makeProposal({ id: 'p-2', status: 'PendingReview' }),
      makeProposal({ id: 'p-3', requestedByUserId: 'u-2' }),
    ])

    expect(h.executableCount.value).toBe(1)

    await h.confirmExecute()
    expect(automationApi.executeProposals).not.toHaveBeenCalled()
  })

  it('sends one idempotency key per proposal and echoes approvedRevisionId verbatim', async () => {
    const h = harness([
      makeProposal({ id: 'p-1', approvedRevisionId: 'rev-1' }),
      makeProposal({ id: 'p-2', approvedRevisionId: null }),
    ])
    vi.mocked(automationApi.executeProposals).mockResolvedValue(receipt([
      { proposalId: 'p-1', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 1 },
      { proposalId: 'p-2', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 1 },
    ]))

    h.requestConfirmation()
    expect(h.confirmationOpen.value).toBe(true)
    await h.confirmExecute()

    const sent = vi.mocked(automationApi.executeProposals).mock.calls[0][0]
    expect(sent.map((s) => s.proposalId)).toEqual(['p-1', 'p-2'])
    expect(sent[0].approvedRevisionId).toBe('rev-1')
    expect(sent[1].approvedRevisionId).toBeNull()
    expect(sent[0].idempotencyKey).toBeTruthy()
    expect(sent[1].idempotencyKey).toBeTruthy()
    expect(sent[0].idempotencyKey).not.toBe(sent[1].idempotencyKey)
  })

  it('renders per-item receipts and reports a partial outcome honestly', async () => {
    const h = harness([
      makeProposal({ id: 'p-1', summary: 'Card one' }),
      makeProposal({ id: 'p-2', summary: 'Card two' }),
      makeProposal({ id: 'p-3', summary: 'Card three' }),
    ])
    vi.mocked(automationApi.executeProposals).mockResolvedValue(receipt([
      { proposalId: 'p-1', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 2 },
      { proposalId: 'p-2', outcome: 'Failed', errorCode: 'Conflict', errorMessage: 'Board moved on', appliedOperations: null },
      { proposalId: 'p-3', outcome: 'Skipped', errorCode: null, errorMessage: null, appliedOperations: null },
    ]))

    h.requestConfirmation()
    await h.confirmExecute()

    expect(h.receipts.value.map((r) => [r.title, r.outcome])).toEqual([
      ['Card one', 'Applied'],
      ['Card two', 'Failed'],
      ['Card three', 'Skipped'],
    ])
    expect(h.receipts.value[1].errorMessage).toBe('Board moved on')
    // A partial outcome must never be reported as a success.
    expect(toast.success).not.toHaveBeenCalled()
    expect(toast.info).toHaveBeenCalled()
    expect(h.loadProposals).toHaveBeenCalled()
  })

  it('reports an all-applied batch as a success and an all-failed batch as an error', async () => {
    const applied = harness([makeProposal({ id: 'p-1' })])
    vi.mocked(automationApi.executeProposals).mockResolvedValue(receipt([
      { proposalId: 'p-1', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 1 },
    ]))
    applied.requestConfirmation()
    await applied.confirmExecute()
    expect(toast.success).toHaveBeenCalled()

    vi.clearAllMocks()
    const failed = harness([makeProposal({ id: 'p-9' })])
    vi.mocked(automationApi.executeProposals).mockResolvedValue(receipt([
      { proposalId: 'p-9', outcome: 'Failed', errorCode: 'Forbidden', errorMessage: 'No access', appliedOperations: null },
    ]))
    failed.requestConfirmation()
    await failed.confirmExecute()
    expect(toast.error).toHaveBeenCalled()
    expect(toast.success).not.toHaveBeenCalled()
  })

  it('leaves no receipts behind when the whole request is rejected', async () => {
    const h = harness([makeProposal({ id: 'p-1' })])
    vi.mocked(automationApi.executeProposals).mockRejectedValue(new Error('403'))

    h.requestConfirmation()
    await h.confirmExecute()

    expect(h.receipts.value).toEqual([])
    expect(h.confirmationOpen.value).toBe(false)
    expect(toast.error).toHaveBeenCalled()
  })

  it('refuses to open a confirmation when nothing is approved', () => {
    const h = harness([makeProposal({ status: 'PendingReview' })])
    h.requestConfirmation()
    expect(h.confirmationOpen.value).toBe(false)
    expect(toast.info).toHaveBeenCalled()
  })

  it('clears the busy flag after a failure so the rail is not wedged', async () => {
    const h = harness([makeProposal({ id: 'p-1' })])
    vi.mocked(automationApi.executeProposals).mockRejectedValue(new Error('boom'))
    h.requestConfirmation()
    await h.confirmExecute()
    expect(h.busy.value).toBe(false)
  })
})
