import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, ref } from 'vue'
import { useProposalRevisions } from '../../composables/useProposalRevisions'
import { proposalRevisionsApi, type ProposalRevision } from '../../api/proposalRevisionsApi'
import type { Proposal as ApiProposal } from '../../types/automation'

vi.mock('../../api/proposalRevisionsApi', () => ({
  proposalRevisionsApi: {
    createRevision: vi.fn(),
    getRevisions: vi.fn(),
    getLatestRevision: vi.fn(),
  },
}))

const toastMocks = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

function makeProposal(id = 'p-1'): ApiProposal {
  return {
    id,
    sourceType: 'Chat',
    sourceReferenceId: null,
    boardId: 'b-1',
    requestedByUserId: 'u-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: `Proposal ${id}`,
    diffPreview: null,
    validationIssues: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    expiresAt: '2026-01-02T00:00:00Z',
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: `correlation-${id}`,
    operations: [],
  } as ApiProposal
}

function makeRevision(
  proposalId = 'p-1',
  revisionNumber = 1,
  id = `revision-${proposalId}-${revisionNumber}`,
): ProposalRevision {
  return {
    id,
    proposalId,
    revisionNumber,
    editorUserId: 'u-1',
    revisedPayload: '{}',
    revisedAt: '2026-01-01T00:00:00Z',
    reason: 'Test',
    createdAt: '2026-01-01T00:00:00Z',
  }
}

async function flushMicrotasks() {
  await Promise.resolve()
  await Promise.resolve()
  await nextTick()
}

describe('useProposalRevisions residual hardening', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('keeps metadata unknown when a self-complete stale prefix omits the disputed number', async () => {
    const revision1 = makeRevision('p-1', 1)
    const revision2 = makeRevision('p-1', 2)
    const conflictingRevision2 = makeRevision('p-1', 2, 'revision-p-1-2-conflict')
    const responses: ProposalRevision[][] = [
      [revision1, revision2],
      [revision1, revision2, conflictingRevision2],
      [revision1],
      [revision1, revision2],
    ]
    let call = 0
    vi.mocked(proposalRevisionsApi.getRevisions).mockImplementation(() =>
      Promise.resolve(responses[call++] ?? []),
    )

    const activeProposal = ref<ApiProposal | null>(makeProposal())
    const revisions = useProposalRevisions(activeProposal)
    await vi.waitFor(() => expect(revisions.revisionCount.value).toBe(2))

    await revisions.loadRevisionState('p-1')
    expect(revisions.revisionCount.value).toBe(0)
    expect(revisions.revisionsLoaded.value).toBe(false)

    await revisions.loadRevisionState('p-1')
    expect(revisions.revisionCount.value).toBe(0)
    expect(revisions.latestRevision.value).toBeNull()
    expect(revisions.revisionsLoaded.value).toBe(false)

    await revisions.loadRevisionState('p-1')
    expect(revisions.revisionCount.value).toBe(2)
    expect(revisions.latestRevision.value?.id).toBe(revision2.id)
    expect(revisions.revisionsLoaded.value).toBe(true)
  })

  it('does not show a failed-history toast after a save has already proven current metadata', async () => {
    let rejectInitialLoad!: (error: Error) => void
    vi.mocked(proposalRevisionsApi.getRevisions).mockImplementationOnce(
      () => new Promise((_resolve, reject) => { rejectInitialLoad = reject }),
    )
    vi.mocked(proposalRevisionsApi.createRevision).mockResolvedValueOnce(
      makeRevision('p-1', 1),
    )

    const activeProposal = ref<ApiProposal | null>(makeProposal())
    const revisions = useProposalRevisions(activeProposal)
    await vi.waitFor(() => expect(rejectInitialLoad).toBeTypeOf('function'))

    await revisions.saveRevision({ revisedPayload: '{}', reason: 'Persisted' })
    expect(revisions.revisionCount.value).toBe(1)
    expect(revisions.revisionsLoaded.value).toBe(true)

    rejectInitialLoad(new Error('stale history request failed'))
    await flushMicrotasks()

    expect(revisions.revisionCount.value).toBe(1)
    expect(revisions.revisionsLoaded.value).toBe(true)
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('evicts the least-recently-used proposal history after a bounded queue walk', async () => {
    const callsByProposal = new Map<string, number>()
    vi.mocked(proposalRevisionsApi.getRevisions).mockImplementation((proposalId) => {
      const call = (callsByProposal.get(proposalId) ?? 0) + 1
      callsByProposal.set(proposalId, call)
      if (proposalId === 'p-0' && call > 1) return Promise.resolve([])
      return Promise.resolve([makeRevision(proposalId, 1)])
    })

    const activeProposal = ref<ApiProposal | null>(makeProposal('p-0'))
    const revisions = useProposalRevisions(activeProposal)
    await vi.waitFor(() => expect(revisions.latestRevision.value?.proposalId).toBe('p-0'))

    // The production cache retains 64 proposal histories. Walking one more
    // distinct proposal must evict p-0 rather than growing for the whole queue.
    for (let index = 1; index <= 64; index += 1) {
      const proposalId = `p-${index}`
      activeProposal.value = makeProposal(proposalId)
      await vi.waitFor(() =>
        expect(revisions.latestRevision.value?.proposalId).toBe(proposalId),
      )
    }

    activeProposal.value = makeProposal('p-0')
    await vi.waitFor(() => expect(callsByProposal.get('p-0')).toBe(2))
    await vi.waitFor(() => expect(revisions.revisionsLoaded.value).toBe(true))

    expect(revisions.revisionCount.value).toBe(0)
    expect(revisions.latestRevision.value).toBeNull()
  })
})
