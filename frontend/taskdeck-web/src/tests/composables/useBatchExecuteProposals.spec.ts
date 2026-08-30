import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, ref } from 'vue'
import { automationApi } from '../../api/automationApi'
import {
  isBatchExecuteEligible,
  useBatchExecuteProposals,
} from '../../composables/useBatchExecuteProposals'
import { isBatchApproveEligible } from '../../composables/useBatchApproveProposals'
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

/**
 * `emptiesQueueOnRefresh` models what the REAL refresh does after a successful batch: the applied
 * proposals stop being eligible, so the queue the composable watches shrinks. The original harness
 * always resolved without touching `rows`, which is precisely why the receipt-destroying auto-close
 * went unnoticed - the condition that triggers it could never arise in a test.
 */
function harness(
  proposals: Proposal[],
  options: { emptiesQueueOnRefresh?: boolean; scope?: string } = {},
) {
  const rows = ref<Proposal[]>(proposals)
  const scope = ref(options.scope ?? JSON.stringify({ boardId: null, history: 'live' }))
  const loadProposals = vi.fn(async () => {
    if (options.emptiesQueueOnRefresh) rows.value = []
  })
  const composable = useBatchExecuteProposals(
    rows,
    ref<string | null>('u-1'),
    ref(NOW),
    loadProposals,
    scope,
  )
  return { rows, scope, loadProposals, ...composable }
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
    expect(h.confirmationCount.value).toBe(2)

    // Reordering and wire-casing changes preserve identity, but the submitted values remain the
    // exact spellings and revision pins captured when the reviewer opened confirmation.
    h.rows.value = [
      makeProposal({ id: 'P-2', approvedRevisionId: null }),
      makeProposal({ id: 'P-1', approvedRevisionId: 'REV-1' }),
    ]
    await nextTick()
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

  it('invalidates a same-count nonempty replacement and never submits unseen proposals', async () => {
    const h = harness([
      makeProposal({ id: 'p-1' }),
      makeProposal({ id: 'p-2' }),
    ])

    h.requestConfirmation()
    expect(h.confirmationOpen.value).toBe(true)

    h.rows.value = [
      makeProposal({ id: 'p-3', boardId: 'b-2' }),
      makeProposal({ id: 'p-4', boardId: 'b-2' }),
    ]
    await nextTick()

    expect(h.executableCount.value).toBe(2)
    expect(h.confirmationOpen.value).toBe(false)
    expect(h.confirmationCount.value).toBe(0)
    await h.confirmExecute()
    expect(automationApi.executeProposals).not.toHaveBeenCalled()
  })

  it('invalidates a board/history scope change before the old queue is replaced', async () => {
    const h = harness([
      makeProposal({ id: 'p-1', boardId: 'b-1' }),
      makeProposal({ id: 'p-2', boardId: 'b-1' }),
    ], { scope: JSON.stringify({ boardId: 'b-1', history: 'live' }) })

    h.requestConfirmation()
    h.scope.value = JSON.stringify({ boardId: 'b-2', history: 'live' })

    expect(h.rows.value.map((proposal) => proposal.id)).toEqual(['p-1', 'p-2'])
    expect(h.confirmationOpen.value).toBe(false)
    await h.confirmExecute()
    expect(automationApi.executeProposals).not.toHaveBeenCalled()
  })

  it('renders per-item receipts without duplicating their partial outcome in a toast', async () => {
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
    // The durable receipt dialog owns every item-level completion announcement.
    expect(toast.success).not.toHaveBeenCalled()
    expect(toast.info).not.toHaveBeenCalled()
    expect(toast.error).not.toHaveBeenCalled()
    expect(h.loadProposals).toHaveBeenCalled()
  })

  it('keeps all-applied and all-failed outcomes in receipts without completion toasts', async () => {
    const applied = harness([makeProposal({ id: 'p-1' })])
    vi.mocked(automationApi.executeProposals).mockResolvedValue(receipt([
      { proposalId: 'p-1', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 1 },
    ]))
    applied.requestConfirmation()
    await applied.confirmExecute()
    expect(applied.receipts.value.map((item) => item.outcome)).toEqual(['Applied'])
    expect(toast.success).not.toHaveBeenCalled()
    expect(toast.info).not.toHaveBeenCalled()
    expect(toast.error).not.toHaveBeenCalled()

    vi.clearAllMocks()
    const failed = harness([makeProposal({ id: 'p-9' })])
    vi.mocked(automationApi.executeProposals).mockResolvedValue(receipt([
      { proposalId: 'p-9', outcome: 'Failed', errorCode: 'Forbidden', errorMessage: 'No access', appliedOperations: null },
    ]))
    failed.requestConfirmation()
    await failed.confirmExecute()
    expect(failed.receipts.value.map((item) => item.outcome)).toEqual(['Failed'])
    expect(toast.success).not.toHaveBeenCalled()
    expect(toast.info).not.toHaveBeenCalled()
    expect(toast.error).not.toHaveBeenCalled()
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

describe('useBatchExecuteProposals receipts survive the post-apply refresh', () => {
  it('keeps the dialog open with receipts after the queue empties', async () => {
    // The regression this exists for: `confirmExecute` sets receipts, then awaits the refresh; the
    // refresh removes every applied proposal; `executableCount` hits 0; and the watcher used to
    // close the very dialog the receipts are rendered in. The better the batch went, the more
    // certain the reviewer was to see nothing.
    const h = harness(
      [makeProposal({ id: 'p-1' }), makeProposal({ id: 'p-2' })],
      { emptiesQueueOnRefresh: true },
    )
    vi.mocked(automationApi.executeProposals).mockResolvedValue(receipt([
      { proposalId: 'p-1', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 1 },
      { proposalId: 'p-2', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 1 },
    ]))

    h.requestConfirmation()
    await h.confirmExecute()
    await nextTick()

    expect(h.loadProposals).toHaveBeenCalled()
    expect(h.rows.value).toHaveLength(0)
    expect(h.executableCount.value).toBe(0)
    expect(h.confirmationOpen.value).toBe(true)
    expect(h.receipts.value).toHaveLength(2)
  })

  it('keeps the captured request and receipt titles when queue and scope drift after Confirm', async () => {
    const h = harness(
      [makeProposal({ id: 'p-1', boardId: 'b-1', summary: 'Confirmed card' })],
      { scope: JSON.stringify({ boardId: 'b-1', history: 'live' }) },
    )
    let resolveExecute!: (value: BatchExecuteProposalsResult) => void
    vi.mocked(automationApi.executeProposals).mockImplementationOnce(
      () => new Promise<BatchExecuteProposalsResult>((resolve) => { resolveExecute = resolve }),
    )

    h.requestConfirmation()
    const execution = h.confirmExecute()
    await nextTick()
    expect(h.busy.value).toBe(true)

    h.scope.value = JSON.stringify({ boardId: 'b-2', history: 'live' })
    h.rows.value = [makeProposal({ id: 'p-9', boardId: 'b-2', summary: 'Unseen card' })]
    expect(h.confirmationOpen.value).toBe(true)

    resolveExecute(receipt([
      { proposalId: 'p-1', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 1 },
    ]))
    await execution

    expect(vi.mocked(automationApi.executeProposals).mock.calls[0][0]
      .map((selection) => selection.proposalId)).toEqual(['p-1'])
    expect(h.receipts.value.map((row) => row.title)).toEqual(['Confirmed card'])
    expect(h.confirmationOpen.value).toBe(true)
  })

  it('still closes an unconfirmed dialog when the queue empties under it', async () => {
    // The auto-close itself must survive: with no receipts there is nothing to protect, and an
    // empty confirmation must not linger.
    const h = harness([makeProposal({ id: 'p-1' })])
    h.requestConfirmation()
    expect(h.confirmationOpen.value).toBe(true)

    h.rows.value = []
    await nextTick()

    expect(h.confirmationOpen.value).toBe(false)
  })

  it('lets the reviewer dismiss receipts while the follow-up refresh is still running', async () => {
    // The Done button is enabled during the secondary refresh, so close must work then. Receipts
    // record writes that already happened; withholding them behind a spinner is pointless.
    const h = harness([makeProposal({ id: 'p-1' })])
    vi.mocked(automationApi.executeProposals).mockResolvedValue(receipt([
      { proposalId: 'p-1', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 1 },
    ]))
    h.requestConfirmation()
    await h.confirmExecute()

    h.busy.value = true
    h.cancelConfirmation()

    expect(h.confirmationOpen.value).toBe(false)
    expect(h.receipts.value).toHaveLength(0)
  })

  it('still refuses to close an in-flight confirmation that has no receipts', () => {
    const h = harness([makeProposal({ id: 'p-1' })])
    h.requestConfirmation()
    h.busy.value = true

    h.cancelConfirmation()

    expect(h.confirmationOpen.value).toBe(true)
  })

  it('forceClose drops the dialog and its receipts unconditionally', async () => {
    const h = harness([makeProposal({ id: 'p-1' })])
    vi.mocked(automationApi.executeProposals).mockResolvedValue(receipt([
      { proposalId: 'p-1', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 1 },
    ]))
    h.requestConfirmation()
    await h.confirmExecute()
    expect(h.receipts.value).toHaveLength(1)

    h.busy.value = true
    h.forceClose()

    expect(h.confirmationOpen.value).toBe(false)
    expect(h.receipts.value).toHaveLength(0)
  })

  it('uses one fallback toast when context closes before in-flight receipts arrive', async () => {
    const h = harness([
      makeProposal({ id: 'p-1' }),
      makeProposal({ id: 'p-2' }),
    ])
    let resolveExecute!: (value: BatchExecuteProposalsResult) => void
    vi.mocked(automationApi.executeProposals).mockImplementationOnce(
      () => new Promise<BatchExecuteProposalsResult>((resolve) => { resolveExecute = resolve }),
    )

    h.requestConfirmation()
    const execution = h.confirmExecute()
    await nextTick()
    expect(h.busy.value).toBe(true)

    h.forceClose()
    resolveExecute(receipt([
      { proposalId: 'p-1', outcome: 'Applied', errorCode: null, errorMessage: null, appliedOperations: 1 },
      { proposalId: 'p-2', outcome: 'Failed', errorCode: 'Conflict', errorMessage: 'Board moved on', appliedOperations: null },
    ]))
    await execution

    expect(h.confirmationOpen.value).toBe(false)
    expect(h.receipts.value.map((item) => item.outcome)).toEqual(['Applied', 'Failed'])
    expect(toast.success).not.toHaveBeenCalled()
    expect(toast.error).not.toHaveBeenCalled()
    expect(toast.info).toHaveBeenCalledTimes(1)
    expect(vi.mocked(toast.info).mock.calls[0][0]).toContain('Applied 1; 1 failed')
  })

  it('keeps an all-skipped outcome in receipts without a completion toast', async () => {
    const h = harness([makeProposal({ id: 'p-1' }), makeProposal({ id: 'p-2' })])
    vi.mocked(automationApi.executeProposals).mockResolvedValue(receipt([
      { proposalId: 'p-1', outcome: 'Skipped', errorCode: null, errorMessage: null, appliedOperations: null },
      { proposalId: 'p-2', outcome: 'Skipped', errorCode: null, errorMessage: null, appliedOperations: null },
    ]))

    h.requestConfirmation()
    await h.confirmExecute()

    expect(h.receipts.value.map((item) => item.outcome)).toEqual(['Skipped', 'Skipped'])
    expect(toast.success).not.toHaveBeenCalled()
    expect(toast.error).not.toHaveBeenCalled()
    expect(toast.info).not.toHaveBeenCalled()
  })
})

describe('isBatchExecuteEligible matches its batch-approve sibling', () => {
  it('refuses anything above Low risk', () => {
    for (const riskLevel of ['Medium', 'High', 'Critical'] as const) {
      expect(isBatchExecuteEligible(makeProposal({ riskLevel }), 'u-1', NOW)).toBe(false)
    }
    // Fail closed on an unrecognised risk value, exactly as batch approve does.
    expect(isBatchExecuteEligible(makeProposal({ riskLevel: 'Unknown' as never }), 'u-1', NOW)).toBe(false)
    expect(isBatchExecuteEligible(makeProposal({ riskLevel: 0 }), 'u-1', NOW)).toBe(true)
  })

  it('refuses any operation that is not a card creation', () => {
    const archive = makeProposal({
      operations: [{ ...makeProposal().operations[0], actionType: 'archive' }],
    })
    const moveTarget = makeProposal({
      operations: [{ ...makeProposal().operations[0], targetType: 'board' }],
    })
    expect(isBatchExecuteEligible(archive, 'u-1', NOW)).toBe(false)
    expect(isBatchExecuteEligible(moveTarget, 'u-1', NOW)).toBe(false)
  })

  it('refuses a proposal carrying more than five operations', () => {
    const base = makeProposal().operations[0]
    const five = makeProposal({
      operations: Array.from({ length: 5 }, (_, i) => ({ ...base, id: `op-${i}`, sequence: i })),
    })
    const six = makeProposal({
      operations: Array.from({ length: 6 }, (_, i) => ({ ...base, id: `op-${i}`, sequence: i })),
    })
    expect(isBatchExecuteEligible(five, 'u-1', NOW)).toBe(true)
    expect(isBatchExecuteEligible(six, 'u-1', NOW)).toBe(false)
  })

  it('admits exactly what batch approve admits, once status is set aside', () => {
    // The drift guard. Everything below differs only in status, so both predicates must agree.
    const shared = { riskLevel: 'Low' as const, requestedByUserId: 'u-1' }
    const approvable = makeProposal({ ...shared, status: 'PendingReview' })
    const executable = makeProposal({ ...shared, status: 'Approved' })
    expect(isBatchApproveEligible(approvable, 'u-1', NOW)).toBe(true)
    expect(isBatchExecuteEligible(executable, 'u-1', NOW)).toBe(true)

    for (const overrides of [
      { riskLevel: 'High' as const },
      { operations: [{ ...makeProposal().operations[0], actionType: 'archive' }] },
      { isExpired: true },
      { requestedByUserId: 'someone-else' },
    ]) {
      expect(isBatchApproveEligible(
        makeProposal({ ...shared, status: 'PendingReview', ...overrides }), 'u-1', NOW)).toBe(false)
      expect(isBatchExecuteEligible(
        makeProposal({ ...shared, status: 'Approved', ...overrides }), 'u-1', NOW)).toBe(false)
    }
  })
})
