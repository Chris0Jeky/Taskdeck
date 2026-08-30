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

const toastMocks = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
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

async function flushMicrotasks() {
  await Promise.resolve()
  await Promise.resolve()
  await nextTick()
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

  it('ignores a pre-save revision load that resolves after the save (no stale overwrite)', async () => {
    // Codex review: a getRevisions request in flight when a save lands must not
    // overwrite the save's state when it resolves with the pre-save (empty) list.
    let resolveLoad: (v: ProposalRevision[]) => void = () => {}
    const loadPromise = new Promise<ProposalRevision[]>((r) => {
      resolveLoad = r
    })
    vi.mocked(proposalRevisionsApi.getRevisions).mockReturnValueOnce(loadPromise)
    const saved = makeRevision({ revisionNumber: 1 })
    vi.mocked(proposalRevisionsApi.createRevision).mockResolvedValue(saved)

    const proposal = ref<ApiProposal | null>(makeProposal())
    const { revisionCount, latestRevision, revisionsLoaded, saveRevision } =
      useProposalRevisions(proposal)
    await nextTick()

    // Save lands while the initial load is still pending.
    await saveRevision({ revisedPayload: '{"title":"Edited"}', reason: 'Fix' })
    expect(revisionCount.value).toBe(1)
    expect(revisionsLoaded.value).toBe(true)

    // The stale load now resolves with the OLD (empty) list — must be ignored.
    resolveLoad([])
    await nextTick()
    await nextTick()

    expect(revisionCount.value).toBe(1)
    expect(latestRevision.value).toEqual(saved)
    expect(revisionsLoaded.value).toBe(true)
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

  it('clears revision state immediately when proposal changes', async () => {
    vi.mocked(proposalRevisionsApi.getRevisions)
      .mockResolvedValueOnce([makeRevision()])
      .mockImplementationOnce(() => new Promise(() => undefined))
    const proposal = ref<ApiProposal | null>(makeProposal())
    const { editing, revisionCount, latestRevision, startEditing } = useProposalRevisions(proposal)

    await vi.waitFor(() => {
      expect(revisionCount.value).toBe(1)
    })

    startEditing()
    proposal.value = makeProposal({ id: 'p-2' })
    await nextTick()

    expect(editing.value).toBe(false)
    expect(revisionCount.value).toBe(0)
    expect(latestRevision.value).toBeNull()
  })

  it('ignores stale save responses after switching proposals', async () => {
    let resolveSave!: (revision: ProposalRevision) => void
    vi.mocked(proposalRevisionsApi.createRevision).mockImplementationOnce(
      () => new Promise((resolve) => {
        resolveSave = resolve
      }),
    )

    const proposal = ref<ApiProposal | null>(makeProposal())
    const { saving, revisionCount, latestRevision, startEditing, saveRevision } =
      useProposalRevisions(proposal)

    startEditing()
    const savePromise = saveRevision({ revisedPayload: '{"title":"Edited"}', reason: 'Fix' })
    await nextTick()

    expect(saving.value).toBe(true)

    proposal.value = makeProposal({ id: 'p-2' })
    await nextTick()

    expect(saving.value).toBe(false)

    resolveSave(makeRevision({ proposalId: 'p-1' }))
    await savePromise

    expect(revisionCount.value).toBe(0)
    expect(latestRevision.value).toBeNull()
  })

  it('suppresses the failure toast for a silent load while keeping the state non-authoritative (#1397 round 3)', async () => {
    // The augment-only callers (read-only stored preview) load revision metadata
    // silently: no error toast on failure, but revisionsLoaded must STILL stay
    // false so authoritative consumers never trust the unknown state.
    vi.mocked(proposalRevisionsApi.getRevisions).mockRejectedValue(new Error('boom'))

    const proposal = ref<ApiProposal | null>(makeProposal())
    const { revisionsLoaded, revisionCount, loadRevisionState } = useProposalRevisions(proposal)

    // The mount-time background load is NOT silent — it toasts.
    await vi.waitFor(() => {
      expect(toastMocks.error).toHaveBeenCalled()
    })
    toastMocks.error.mockClear()

    await loadRevisionState('p-1', { silent: true })

    expect(toastMocks.error).not.toHaveBeenCalled()
    expect(revisionsLoaded.value).toBe(false)
    expect(revisionCount.value).toBe(0)
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
  // #2215 B -----------------------------------------------------------------

  it('resyncs revision state when a poll brings a newer latestRevisionId for the same proposal (#2215 B)', async () => {
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([])
    const proposal = ref<ApiProposal | null>(makeProposal({ latestRevisionId: null }))
    const { revisionCount, latestRevision } = useProposalRevisions(proposal)
    await vi.waitFor(() => {
      expect(proposalRevisionsApi.getRevisions).toHaveBeenCalledTimes(1)
    })
    expect(revisionCount.value).toBe(0)

    // Another reviewer saves a revision; the queue poll replaces the row with a
    // new `latestRevisionId` and the SAME proposal id. Watching the id alone
    // left revisionCount at 0 and latestRevision null, so `editablePayload`
    // kept offering the pre-revision operations.
    const collaboratorRevision = makeRevision({ id: 'rev-9', revisionNumber: 1 })
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([collaboratorRevision])
    proposal.value = makeProposal({ latestRevisionId: 'rev-9' })
    await nextTick()

    await vi.waitFor(() => {
      expect(revisionCount.value).toBe(1)
    })
    expect(latestRevision.value).toEqual(collaboratorRevision)
  })

  it('does not close an open editor when only the revision moves under it (#2215 B)', async () => {
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([])
    const proposal = ref<ApiProposal | null>(makeProposal({ latestRevisionId: null }))
    const { editing, startEditing } = useProposalRevisions(proposal)
    await vi.waitFor(() => {
      expect(proposalRevisionsApi.getRevisions).toHaveBeenCalledTimes(1)
    })

    startEditing()
    expect(editing.value).toBe(true)

    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([makeRevision({ id: 'rev-9' })])
    proposal.value = makeProposal({ latestRevisionId: 'rev-9' })
    await nextTick()
    await flushMicrotasks()

    // The resync is a read, not a reset: closing the composer would discard a
    // half-written edit over a change that happened somewhere else.
    expect(editing.value).toBe(true)
  })

  it('still fully resets when the proposal itself changes (#2215 B guard)', async () => {
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([makeRevision()])
    const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
    const { editing, revisionCount, startEditing } = useProposalRevisions(proposal)
    await vi.waitFor(() => {
      expect(revisionCount.value).toBe(1)
    })
    startEditing()
    expect(editing.value).toBe(true)

    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([])
    proposal.value = makeProposal({ id: 'p-2', latestRevisionId: 'rev-other' })
    await nextTick()
    await flushMicrotasks()

    expect(editing.value).toBe(false)
    expect(revisionCount.value).toBe(0)
  })
})
