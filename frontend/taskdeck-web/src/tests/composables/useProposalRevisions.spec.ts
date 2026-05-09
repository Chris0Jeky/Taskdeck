import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref, nextTick } from 'vue'
import { useProposalRevisions } from '../../composables/useProposalRevisions'
import { proposalRevisionsApi } from '../../api/proposalRevisionsApi'
import type { Proposal as ApiProposal } from '../../types/automation'
import type { ProposalRevision } from '../../api/proposalRevisionsApi'

vi.mock('../../api/proposalRevisionsApi', () => ({
  proposalRevisionsApi: {
    createRevision: vi.fn(),
    getRevisions: vi.fn(),
    getLatestRevision: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  }),
}))

function makeProposal(overrides: Partial<ApiProposal> = {}): ApiProposal {
  return {
    id: 'p-1',
    sourceType: 'Chat',
    sourceReferenceId: null,
    boardId: 'b-1',
    requestedByUserId: 'u-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: 'Test',
    diffPreview: null,
    validationIssues: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 3600000).toISOString(),
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'c-1',
    operations: [
      {
        id: 'op-1',
        proposalId: 'p-1',
        sequence: 1,
        actionType: 'CreateCard',
        targetType: 'Card',
        targetId: null,
        parameters: '{"title":"Test"}',
        idempotencyKey: 'ik-1',
        expectedVersion: null,
      },
    ],
    ...overrides,
  } as ApiProposal
}

function makeRevision(overrides: Partial<ProposalRevision> = {}): ProposalRevision {
  return {
    id: 'rev-1',
    proposalId: 'p-1',
    revisionNumber: 1,
    editorUserId: 'u-1',
    revisedPayload: '{"title":"Edited"}',
    revisedAt: '2026-01-01T00:00:00Z',
    reason: 'Fix',
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('useProposalRevisions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([])
  })

  it('starts with editing false and zero revision count', async () => {
    const proposal = ref<ApiProposal | null>(null)
    const { editing, revisionCount } = useProposalRevisions(proposal)

    expect(editing.value).toBe(false)
    expect(revisionCount.value).toBe(0)
  })

  it('loads revisions when activeProposal changes', async () => {
    const revisions = [makeRevision()]
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue(revisions)

    const proposal = ref<ApiProposal | null>(null)
    const { revisionCount, latestRevision } = useProposalRevisions(proposal)

    proposal.value = makeProposal()
    await nextTick()
    await vi.waitFor(() => {
      expect(revisionCount.value).toBe(1)
    })
    expect(latestRevision.value).toEqual(revisions[0])
  })

  it('opens and closes editing mode', () => {
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([])
    const proposal = ref<ApiProposal | null>(makeProposal())
    const { editing, startEditing, cancelEditing } = useProposalRevisions(proposal)

    startEditing()
    expect(editing.value).toBe(true)

    cancelEditing()
    expect(editing.value).toBe(false)
  })

  it('saves a revision and updates state', async () => {
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([])
    const newRevision = makeRevision({ revisionNumber: 1 })
    vi.mocked(proposalRevisionsApi.createRevision).mockResolvedValue(newRevision)

    const proposal = ref<ApiProposal | null>(makeProposal())
    const { editing, revisionCount, latestRevision, startEditing, saveRevision } =
      useProposalRevisions(proposal)

    startEditing()
    expect(editing.value).toBe(true)

    await saveRevision({ revisedPayload: '{"title":"Edited"}', reason: 'Fix' })

    expect(proposalRevisionsApi.createRevision).toHaveBeenCalledWith('p-1', {
      revisedPayload: '{"title":"Edited"}',
      reason: 'Fix',
    })
    expect(editing.value).toBe(false)
    expect(revisionCount.value).toBe(1)
    expect(latestRevision.value).toEqual(newRevision)
  })

  it('resets editing when proposal changes', async () => {
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([])
    const proposal = ref<ApiProposal | null>(makeProposal())
    const { editing, startEditing } = useProposalRevisions(proposal)

    startEditing()
    expect(editing.value).toBe(true)

    proposal.value = makeProposal({ id: 'p-2' })
    await nextTick()

    expect(editing.value).toBe(false)
  })

  it('discards stale load responses when proposal switches quickly', async () => {
    const staleRevisions = [makeRevision({ proposalId: 'p-1', revisionNumber: 1 })]
    const freshRevisions = [
      makeRevision({ proposalId: 'p-2', revisionNumber: 1 }),
      makeRevision({ proposalId: 'p-2', revisionNumber: 2 }),
    ]

    let resolveStale!: (v: typeof staleRevisions) => void
    vi.mocked(proposalRevisionsApi.getRevisions)
      .mockImplementationOnce(() => new Promise((r) => { resolveStale = r }))
      .mockResolvedValueOnce(freshRevisions)

    const proposal = ref<ApiProposal | null>(null)
    const { revisionCount, latestRevision } = useProposalRevisions(proposal)

    proposal.value = makeProposal({ id: 'p-1' })
    await nextTick()

    proposal.value = makeProposal({ id: 'p-2' })
    await nextTick()

    await vi.waitFor(() => {
      expect(revisionCount.value).toBe(2)
    })

    resolveStale(staleRevisions)
    await nextTick()

    expect(revisionCount.value).toBe(2)
    expect(latestRevision.value?.proposalId).toBe('p-2')
  })
})
