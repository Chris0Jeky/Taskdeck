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
    approvedRevisionId: null,
    latestRevisionId: null,
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
    vi.resetAllMocks()
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

  it('never evicts the active proposal even when it is the least-recently-used history', async () => {
    // p-0 must become the OLDEST entry while it is on screen: its history is
    // created by a save (the pending GET never answers), 63 other proposals are
    // walked after it, and a stale save continuation for a 65th proposal then
    // lands while p-0 is active again. The eviction candidate is p-0, and the
    // guard must skip it so its save-proven revision survives.
    const pendingGets = new Map<string, Array<(revisions: ProposalRevision[]) => void>>()
    vi.mocked(proposalRevisionsApi.getRevisions).mockImplementation((proposalId) => {
      if (proposalId === 'p-0' || proposalId === 'p-64') {
        return new Promise<ProposalRevision[]>((resolve) => {
          const resolvers = pendingGets.get(proposalId) ?? []
          resolvers.push(resolve)
          pendingGets.set(proposalId, resolvers)
        })
      }
      return Promise.resolve([makeRevision(proposalId, 1)])
    })
    let resolveStaleSave!: (revision: ProposalRevision) => void
    vi.mocked(proposalRevisionsApi.createRevision).mockImplementation((proposalId) => {
      if (proposalId === 'p-64') {
        return new Promise<ProposalRevision>((resolve) => { resolveStaleSave = resolve })
      }
      return Promise.resolve(makeRevision(proposalId, 1))
    })

    const activeProposal = ref<ApiProposal | null>(makeProposal('p-0'))
    const revisions = useProposalRevisions(activeProposal)
    await flushMicrotasks()
    await revisions.saveRevision({ revisedPayload: '{}', reason: 'Proven by save' })
    expect(revisions.revisionCount.value).toBe(1)
    expect(revisions.revisionsLoaded.value).toBe(true)

    for (let index = 1; index <= 63; index += 1) {
      activeProposal.value = makeProposal(`p-${index}`)
      await flushMicrotasks()
      expect(revisions.latestRevision.value?.proposalId).toBe(`p-${index}`)
    }

    // p-64 has no history yet (its GET never answers); its save is still in
    // flight when the reviewer returns to p-0.
    activeProposal.value = makeProposal('p-64')
    await flushMicrotasks()
    const staleSave = revisions.saveRevision({ revisedPayload: '{}', reason: 'Stale' })
    await vi.waitFor(() => expect(resolveStaleSave).toBeTypeOf('function'))

    activeProposal.value = makeProposal('p-0')
    await flushMicrotasks()
    expect(revisions.revisionsLoaded.value).toBe(false)

    // The 65th history arrives from the stale continuation: eviction pressure
    // with p-0 both active and least recently used.
    resolveStaleSave(makeRevision('p-64', 1))
    await staleSave
    await flushMicrotasks()

    // A pre-save read for p-0 answers empty. With p-0's history retained, the
    // save-proven revision wins; had p-0 been evicted, the empty answer would
    // publish an authoritative zero over a persisted revision.
    const p0Gets = pendingGets.get('p-0') ?? []
    expect(p0Gets).toHaveLength(2)
    p0Gets[1]([])
    await flushMicrotasks()

    expect(revisions.revisionsLoaded.value).toBe(true)
    expect(revisions.revisionCount.value).toBe(1)
    expect(revisions.latestRevision.value?.id).toBe('revision-p-0-1')
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
    await flushMicrotasks()
    expect(revisions.latestRevision.value?.proposalId).toBe('p-0')

    // The production cache retains 64 proposal histories. Walking one more
    // distinct proposal must evict p-0 rather than growing for the whole queue.
    for (let index = 1; index <= 64; index += 1) {
      const proposalId = `p-${index}`
      activeProposal.value = makeProposal(proposalId)
      await flushMicrotasks()
      expect(revisions.latestRevision.value?.proposalId).toBe(proposalId)
    }

    activeProposal.value = makeProposal('p-0')
    await flushMicrotasks()

    expect(callsByProposal.get('p-0')).toBe(2)
    expect(revisions.revisionsLoaded.value).toBe(true)
    expect(revisions.revisionCount.value).toBe(0)
    expect(revisions.latestRevision.value).toBeNull()
  })
})
