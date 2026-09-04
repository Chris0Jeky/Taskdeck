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

async function arrangeOverlappingSaves() {
  const saveResolvers: Array<(revision: ProposalRevision) => void> = []
  vi.mocked(proposalRevisionsApi.createRevision).mockImplementation(
    () => new Promise((resolve) => saveResolvers.push(resolve)),
  )
  vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([])

  const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
  const revisions = useProposalRevisions(proposal)
  await vi.waitFor(() => expect(revisions.revisionsLoaded.value).toBe(true))

  revisions.startEditing()
  const firstSave = revisions.saveRevision({ revisedPayload: '{"title":"A1"}', reason: 'A1' })
  await vi.waitFor(() => expect(saveResolvers).toHaveLength(1))

  proposal.value = makeProposal({ id: 'p-2' })
  await nextTick()
  await flushMicrotasks()
  proposal.value = makeProposal({ id: 'p-1' })
  await vi.waitFor(() => expect(revisions.revisionsLoaded.value).toBe(true))

  revisions.startEditing()
  const secondSave = revisions.saveRevision({ revisedPayload: '{"title":"A2"}', reason: 'A2' })
  await vi.waitFor(() => expect(saveResolvers).toHaveLength(2))

  return { proposal, revisions, saveResolvers, firstSave, secondSave }
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

  it('invalidates uncertain queue reads without claiming the save succeeded', async () => {
    vi.mocked(proposalRevisionsApi.createRevision).mockRejectedValueOnce(
      new Error('Request timed out after commit'),
    )
    const onRevisionSaved = vi.fn()
    const onRevisionStateUncertain = vi.fn()
    const proposal = ref<ApiProposal | null>(makeProposal())
    const { editing, revisionCount, revisionsLoaded, startEditing, saveRevision } =
      useProposalRevisions(proposal, { onRevisionSaved, onRevisionStateUncertain })

    startEditing()
    await expect(
      saveRevision({ revisedPayload: '{"title":"Edited"}', reason: 'Timeout' }),
    ).resolves.toEqual({ proposalId: 'p-1', outcome: 'indeterminate', current: true })

    expect(onRevisionSaved).not.toHaveBeenCalled()
    expect(onRevisionStateUncertain).toHaveBeenCalledOnce()
    expect(editing.value).toBe(true)
    expect(revisionCount.value).toBe(0)
    expect(revisionsLoaded.value).toBe(false)
  })

  it('preserves known revision metadata for a definite 4xx rejection', async () => {
    const knownRevision = makeRevision({
      revisedPayload: '{"title":"Existing revision"}',
    })
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValueOnce([knownRevision])
    vi.mocked(proposalRevisionsApi.createRevision).mockRejectedValueOnce({
      response: {
        status: 400,
        data: {
          errorCode: 'ValidationError',
          message: 'Revision payload is invalid',
        },
      },
    })
    const onRevisionSaved = vi.fn()
    const onRevisionStateUncertain = vi.fn()
    const proposal = ref<ApiProposal | null>(makeProposal())
    const {
      editing,
      revisionCount,
      latestRevision,
      revisionsLoaded,
      startEditing,
      saveRevision,
    } = useProposalRevisions(proposal, { onRevisionSaved, onRevisionStateUncertain })

    await vi.waitFor(() => expect(revisionsLoaded.value).toBe(true))
    startEditing()

    await expect(
      saveRevision({ revisedPayload: '{"title":"Invalid"}', reason: 'Try invalid payload' }),
    ).resolves.toEqual({ proposalId: 'p-1', outcome: 'rejected', current: true })

    expect(onRevisionSaved).not.toHaveBeenCalled()
    expect(onRevisionStateUncertain).not.toHaveBeenCalled()
    expect(editing.value).toBe(true)
    expect(revisionCount.value).toBe(1)
    expect(latestRevision.value).toEqual(knownRevision)
    expect(revisionsLoaded.value).toBe(true)
    expect(toastMocks.error).toHaveBeenCalledWith('Revision payload is invalid')
  })

  it('converges on both persisted revisions when A1 resolves before A2', async () => {
    const { revisions, saveResolvers, firstSave, secondSave } = await arrangeOverlappingSaves()

    saveResolvers[0](makeRevision({ id: 'rev-a1', revisionNumber: 1 }))
    await flushMicrotasks()

    // A1 proves the whole chain on its own, so it publishes straight away
    // instead of waiting for A2 to land.
    expect(revisions.revisionCount.value).toBe(1)
    expect(revisions.latestRevision.value?.id).toBe('rev-a1')
    expect(revisions.revisionsLoaded.value).toBe(true)

    saveResolvers[1](makeRevision({ id: 'rev-a2', revisionNumber: 2 }))
    await Promise.all([firstSave, secondSave])

    // Monotonic: the later response raises the published count, never lowers it.
    expect(revisions.revisionCount.value).toBe(2)
    expect(revisions.latestRevision.value?.id).toBe('rev-a2')
    expect(revisions.revisionsLoaded.value).toBe(true)
  })

  it('converges on both persisted revisions when A2 resolves before A1', async () => {
    const { revisions, saveResolvers, firstSave, secondSave } = await arrangeOverlappingSaves()

    saveResolvers[1](makeRevision({ id: 'rev-a2', revisionNumber: 2 }))
    await flushMicrotasks()
    expect(revisions.revisionsLoaded.value).toBe(false)
    saveResolvers[0](makeRevision({ id: 'rev-a1', revisionNumber: 1 }))
    await Promise.all([firstSave, secondSave])

    expect(revisions.revisionCount.value).toBe(2)
    expect(revisions.latestRevision.value?.id).toBe('rev-a2')
    expect(revisions.revisionsLoaded.value).toBe(true)
  })

  it('merges a newer revision GET that was in flight when a save landed (#2524)', async () => {
    // Another session saved a third revision while this session's own POST was
    // in flight. Discarding the GET that was already running would publish a
    // complete-looking 1..2 chain and let the next edit build on a superseded
    // revision until the ~15 s poll moved latestRevisionId.
    let resolveLoad!: (revisions: ProposalRevision[]) => void
    let resolveSave!: (revision: ProposalRevision) => void
    vi.mocked(proposalRevisionsApi.getRevisions)
      .mockResolvedValueOnce([makeRevision({ id: 'rev-1', revisionNumber: 1 })])
      .mockImplementationOnce(
        () => new Promise((resolve) => { resolveLoad = resolve }),
      )
    vi.mocked(proposalRevisionsApi.createRevision).mockImplementationOnce(
      () => new Promise((resolve) => { resolveSave = resolve }),
    )

    const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
    const {
      revisionCount,
      latestRevision,
      revisionsLoaded,
      startEditing,
      saveRevision,
      loadRevisionState,
    } = useProposalRevisions(proposal)
    await vi.waitFor(() => expect(revisionsLoaded.value).toBe(true))

    startEditing()
    const savePromise = saveRevision({ revisedPayload: '{"title":"Edited"}', reason: 'Edit' })
    void loadRevisionState('p-1')
    await vi.waitFor(() => expect(resolveLoad).toBeTypeOf('function'))

    resolveSave(makeRevision({ id: 'rev-2', revisionNumber: 2 }))
    await savePromise

    // The POST alone proves 1..2, so the intermediate publish still happens.
    expect(revisionCount.value).toBe(2)
    expect(latestRevision.value?.id).toBe('rev-2')
    expect(revisionsLoaded.value).toBe(true)

    resolveLoad([
      makeRevision({ id: 'rev-1', revisionNumber: 1 }),
      makeRevision({ id: 'rev-2', revisionNumber: 2 }),
      makeRevision({ id: 'rev-3-other-session', revisionNumber: 3 }),
    ])
    await flushMicrotasks()

    expect(revisionCount.value).toBe(3)
    expect(latestRevision.value?.id).toBe('rev-3-other-session')
    expect(revisionsLoaded.value).toBe(true)
  })

  it('leaves revision metadata unknown when a GET reports another proposal (#2524)', async () => {
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValueOnce([
      makeRevision({ id: 'rev-1', revisionNumber: 1 }),
      makeRevision({ id: 'rev-foreign', proposalId: 'p-9', revisionNumber: 2 }),
    ])

    const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
    const { revisionCount, latestRevision, revisionsLoaded } = useProposalRevisions(proposal)
    await vi.waitFor(() =>
      expect(proposalRevisionsApi.getRevisions).toHaveBeenCalledTimes(1),
    )
    await flushMicrotasks()

    expect(revisionCount.value).toBe(0)
    expect(latestRevision.value).toBeNull()
    expect(revisionsLoaded.value).toBe(false)
  })

  it('recovers revision metadata once a consistent GET follows an inconsistent one (#2524)', async () => {
    // One number reported under two ids is not a trustworthy answer, but it
    // must not blind the composable for the rest of its lifetime.
    vi.mocked(proposalRevisionsApi.getRevisions)
      .mockResolvedValueOnce([
        makeRevision({ id: 'rev-1', revisionNumber: 1 }),
        makeRevision({ id: 'rev-1-conflict', revisionNumber: 1 }),
      ])
      .mockResolvedValueOnce([
        makeRevision({ id: 'rev-1', revisionNumber: 1 }),
        makeRevision({ id: 'rev-2', revisionNumber: 2 }),
      ])

    const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
    const { revisionCount, latestRevision, revisionsLoaded, loadRevisionState } =
      useProposalRevisions(proposal)
    await vi.waitFor(() =>
      expect(proposalRevisionsApi.getRevisions).toHaveBeenCalledTimes(1),
    )
    await flushMicrotasks()

    expect(revisionCount.value).toBe(0)
    expect(latestRevision.value).toBeNull()
    expect(revisionsLoaded.value).toBe(false)

    await loadRevisionState('p-1')

    expect(revisionCount.value).toBe(2)
    expect(latestRevision.value?.id).toBe('rev-2')
    expect(revisionsLoaded.value).toBe(true)
  })

  it('does not clear an inconsistency on a response that skips the disputed number (#2524 review)', async () => {
    // A partial answer is trustworthy revision by revision, but it proves
    // nothing about the number that was reported twice. Without the completeness
    // requirement it would clear the flag and republish the FIRST id seen for
    // that number as authoritative, even though the server contradicted it.
    // Scripted rather than a `mockResolvedValueOnce` queue: an assertion that
    // fails part way through a queue leaves its remaining entries armed for the
    // NEXT test, which turns one red into a cascade of unrelated ones.
    const responses: ProposalRevision[][] = [
      [makeRevision({ id: 'rev-1', revisionNumber: 1 })],
      [
        makeRevision({ id: 'rev-1', revisionNumber: 1 }),
        makeRevision({ id: 'rev-1-conflict', revisionNumber: 1 }),
      ],
      [makeRevision({ id: 'rev-2', revisionNumber: 2 })],
      [
        makeRevision({ id: 'rev-1', revisionNumber: 1 }),
        makeRevision({ id: 'rev-2', revisionNumber: 2 }),
      ],
    ]
    let call = 0
    vi.mocked(proposalRevisionsApi.getRevisions).mockImplementation(() =>
      Promise.resolve(responses[call++] ?? []),
    )

    const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
    const { revisionCount, latestRevision, revisionsLoaded, loadRevisionState } =
      useProposalRevisions(proposal)
    await vi.waitFor(() => expect(revisionsLoaded.value).toBe(true))
    expect(revisionCount.value).toBe(1)

    // The server now reports revision 1 under two ids.
    await loadRevisionState('p-1')
    expect(revisionCount.value).toBe(0)
    expect(revisionsLoaded.value).toBe(false)

    // Revision 2 alone would complete the stored chain 1..2, but revision 1 is
    // exactly the number in dispute, so the metadata stays unknown.
    await loadRevisionState('p-1')
    expect(revisionCount.value).toBe(0)
    expect(latestRevision.value).toBeNull()
    expect(revisionsLoaded.value).toBe(false)

    // A whole chain settles it.
    await loadRevisionState('p-1')
    expect(revisionCount.value).toBe(2)
    expect(latestRevision.value?.id).toBe('rev-2')
    expect(revisionsLoaded.value).toBe(true)
  })

  it.each([404, 409, 500])(
    'treats HTTP %s as indeterminate because revision persistence is unknown',
    async (status) => {
      const knownRevision = makeRevision()
      vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValueOnce([knownRevision])
      vi.mocked(proposalRevisionsApi.createRevision).mockRejectedValueOnce({
        response: { status },
      })
      const onRevisionSaved = vi.fn()
      const onRevisionStateUncertain = vi.fn()
      const proposal = ref<ApiProposal | null>(makeProposal())
      const {
        editing,
        revisionCount,
        latestRevision,
        revisionsLoaded,
        startEditing,
        saveRevision,
      } = useProposalRevisions(proposal, { onRevisionSaved, onRevisionStateUncertain })

      await vi.waitFor(() => expect(revisionsLoaded.value).toBe(true))
      startEditing()

      await expect(
        saveRevision({ revisedPayload: '{"title":"Uncertain"}', reason: 'Check boundary' }),
      ).resolves.toEqual({ proposalId: 'p-1', outcome: 'indeterminate', current: true })

      expect(onRevisionSaved).not.toHaveBeenCalled()
      expect(onRevisionStateUncertain).toHaveBeenCalledOnce()
      expect(editing.value).toBe(true)
      expect(revisionCount.value).toBe(0)
      expect(latestRevision.value).toBeNull()
      expect(revisionsLoaded.value).toBe(false)
    },
  )

  it('merges a pre-save revision load that resolves after the save without lowering the count', async () => {
    // Codex review: a getRevisions request in flight when a save lands must not
    // overwrite the save's state when it resolves with the pre-save (empty)
    // list. Since #2524 that answer is merged rather than dropped, which is the
    // same outcome here: merging only ever adds, so an older, emptier list
    // cannot take the saved revision back out.
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

    // The stale load now resolves with the OLD (empty) list — it adds nothing.
    resolveLoad([])
    await nextTick()
    await nextTick()

    expect(revisionCount.value).toBe(1)
    expect(latestRevision.value).toEqual(saved)
    expect(revisionsLoaded.value).toBe(true)
  })

  it('invalidates re-entered A metadata and its pending GET before a stale A save succeeds', async () => {
    let resolveReopenedA!: (revisions: ProposalRevision[]) => void
    let resolveSave!: (revision: ProposalRevision) => void
    vi.mocked(proposalRevisionsApi.getRevisions)
      .mockResolvedValueOnce([makeRevision({ id: 'rev-a', proposalId: 'p-1' })])
      .mockResolvedValueOnce([makeRevision({ id: 'rev-b', proposalId: 'p-2' })])
      .mockImplementationOnce(
        () => new Promise((resolve) => { resolveReopenedA = resolve }),
      )
    vi.mocked(proposalRevisionsApi.createRevision).mockImplementationOnce(
      () => new Promise((resolve) => { resolveSave = resolve }),
    )

    const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
    const { revisionCount, latestRevision, revisionsLoaded, startEditing, saveRevision } =
      useProposalRevisions(proposal)
    await vi.waitFor(() => expect(latestRevision.value?.proposalId).toBe('p-1'))

    startEditing()
    const savePromise = saveRevision({ revisedPayload: '{"title":"Saved"}', reason: 'Save A' })
    proposal.value = makeProposal({ id: 'p-2' })
    await vi.waitFor(() => expect(latestRevision.value?.proposalId).toBe('p-2'))
    proposal.value = makeProposal({ id: 'p-1' })
    await vi.waitFor(() => expect(resolveReopenedA).toBeTypeOf('function'))

    resolveSave(makeRevision({ id: 'rev-saved', proposalId: 'p-1', revisionNumber: 2 }))
    await expect(savePromise).resolves.toEqual({
      proposalId: 'p-1',
      outcome: 'persisted',
      current: false,
    })

    // The response belongs to an old edit session, but the previously loaded
    // revision 1 plus the returned revision 2 prove the complete chain. The
    // pending GET must not replace that authoritative metadata with its old
    // empty answer.
    expect(revisionCount.value).toBe(2)
    expect(latestRevision.value?.id).toBe('rev-saved')
    expect(revisionsLoaded.value).toBe(true)

    resolveReopenedA([])
    await flushMicrotasks()

    expect(revisionCount.value).toBe(2)
    expect(latestRevision.value?.id).toBe('rev-saved')
    expect(revisionsLoaded.value).toBe(true)
  })

  it('keeps the proven metadata when the re-entered A GET REJECTS after the save (#2524 review)', async () => {
    // Same interleaving as the test above, except the pending GET fails instead
    // of answering. A failed GET proves nothing, so it must not zero a count a
    // save already proved: `revisionsLoaded` true beside `revisionCount` 0 is
    // the state that makes PaperReviewView render the diff as no-operations,
    // block Apply with a false zero-op toast, and pin the editor to the
    // pre-revision operations so the next save discards the saved revision.
    let rejectReopenedA!: (error: Error) => void
    let resolveSave!: (revision: ProposalRevision) => void
    vi.mocked(proposalRevisionsApi.getRevisions)
      .mockResolvedValueOnce([makeRevision({ id: 'rev-a', proposalId: 'p-1' })])
      .mockResolvedValueOnce([makeRevision({ id: 'rev-b', proposalId: 'p-2' })])
      .mockImplementationOnce(
        () => new Promise((_resolve, reject) => { rejectReopenedA = reject }),
      )
    vi.mocked(proposalRevisionsApi.createRevision).mockImplementationOnce(
      () => new Promise((resolve) => { resolveSave = resolve }),
    )

    const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
    const { revisionCount, latestRevision, revisionsLoaded, startEditing, saveRevision } =
      useProposalRevisions(proposal)
    await vi.waitFor(() => expect(latestRevision.value?.proposalId).toBe('p-1'))

    startEditing()
    const savePromise = saveRevision({ revisedPayload: '{"title":"Saved"}', reason: 'Save A' })
    proposal.value = makeProposal({ id: 'p-2' })
    await vi.waitFor(() => expect(latestRevision.value?.proposalId).toBe('p-2'))
    proposal.value = makeProposal({ id: 'p-1' })
    await vi.waitFor(() => expect(rejectReopenedA).toBeTypeOf('function'))

    resolveSave(makeRevision({ id: 'rev-saved', proposalId: 'p-1', revisionNumber: 2 }))
    await expect(savePromise).resolves.toEqual({
      proposalId: 'p-1',
      outcome: 'persisted',
      current: false,
    })
    expect(revisionCount.value).toBe(2)
    expect(revisionsLoaded.value).toBe(true)

    rejectReopenedA(new Error('network'))
    await flushMicrotasks()

    expect(revisionCount.value).toBe(2)
    expect(latestRevision.value?.id).toBe('rev-saved')
    expect(revisionsLoaded.value).toBe(true)
    // The invariant that must hold whatever else changes: never authoritative
    // and empty at the same time.
    expect(revisionsLoaded.value && revisionCount.value === 0).toBe(false)
  })

  it('keeps B metadata authoritative when a stale A save succeeds while B is active', async () => {
    let resolveSave!: (revision: ProposalRevision) => void
    vi.mocked(proposalRevisionsApi.getRevisions)
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([makeRevision({ id: 'rev-b', proposalId: 'p-2' })])
    vi.mocked(proposalRevisionsApi.createRevision).mockImplementationOnce(
      () => new Promise((resolve) => { resolveSave = resolve }),
    )

    const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
    const { revisionCount, latestRevision, revisionsLoaded, startEditing, saveRevision } =
      useProposalRevisions(proposal)
    await vi.waitFor(() => expect(revisionsLoaded.value).toBe(true))

    startEditing()
    const savePromise = saveRevision({ revisedPayload: '{"title":"Saved"}', reason: 'Save A' })
    proposal.value = makeProposal({ id: 'p-2' })
    await vi.waitFor(() => expect(latestRevision.value?.proposalId).toBe('p-2'))

    resolveSave(makeRevision({ id: 'rev-saved', proposalId: 'p-1' }))
    await expect(savePromise).resolves.toEqual({
      proposalId: 'p-1',
      outcome: 'persisted',
      current: false,
    })

    expect(revisionCount.value).toBe(1)
    expect(latestRevision.value?.proposalId).toBe('p-2')
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

  it('does not publish a failed resync as authoritative, and stays silent (#2215 round 1 M-1)', async () => {
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([makeRevision({ id: 'rev-1' })])
    const proposal = ref<ApiProposal | null>(makeProposal({ latestRevisionId: 'rev-1' }))
    const { revisionCount, revisionsLoaded } = useProposalRevisions(proposal)
    await vi.waitFor(() => {
      expect(revisionsLoaded.value).toBe(true)
    })
    expect(revisionCount.value).toBe(1)

    // The resync GET fails. Its catch zeroes revisionCount and nulls
    // latestRevision; if revisionsLoaded stayed true, PaperReviewView would read
    // that as "authoritatively no revisions" and short-circuit Apply with a
    // false zero-op toast, while editablePayload fell back to the raw
    // pre-revision operations.
    vi.mocked(proposalRevisionsApi.getRevisions).mockRejectedValue(new Error('network'))
    proposal.value = makeProposal({ latestRevisionId: 'rev-2' })
    await nextTick()
    await flushMicrotasks()

    expect(revisionsLoaded.value).toBe(false)
    // And a poll the reviewer never asked for must not raise a toast — the same
    // silent doctrine refreshProposals holds on the far side of the tick.
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('does not re-read revisions when a poll brings an equivalent proposal (#2215 round 1 M-4)', async () => {
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([makeRevision({ id: 'rev-1' })])
    const proposal = ref<ApiProposal | null>(makeProposal({ latestRevisionId: 'rev-1' }))
    useProposalRevisions(proposal)
    await vi.waitFor(() => {
      expect(proposalRevisionsApi.getRevisions).toHaveBeenCalledTimes(1)
    })

    // A 15 s tick that changes nothing still replaces the proposal OBJECT, and
    // the watch getter builds a fresh tuple every evaluation — so without the
    // key comparison this would re-read the revision list on every tick.
    proposal.value = makeProposal({ latestRevisionId: 'rev-1' })
    await nextTick()
    await flushMicrotasks()
    proposal.value = makeProposal({ latestRevisionId: 'rev-1' })
    await nextTick()
    await flushMicrotasks()

    expect(proposalRevisionsApi.getRevisions).toHaveBeenCalledTimes(1)
  })

  it('does not resync when a revised proposal is approved (#2215 round 2)', async () => {
    const revision = makeRevision({ id: 'rev-1' })
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([revision])
    const proposal = ref<ApiProposal | null>(
      makeProposal({ status: 'PendingReview', latestRevisionId: 'rev-1' }),
    )
    const { revisionCount, latestRevision, revisionsLoaded } = useProposalRevisions(proposal)
    await vi.waitFor(() => {
      expect(revisionsLoaded.value).toBe(true)
    })
    expect(proposalRevisionsApi.getRevisions).toHaveBeenCalledTimes(1)

    // rev-1 -> null with the revision pinned into approvedRevisionId. Nothing
    // moved, so the state stays authoritative and no read is issued.
    proposal.value = makeProposal({
      status: 'Approved',
      latestRevisionId: null,
      approvedRevisionId: 'rev-1',
    })
    await nextTick()
    await flushMicrotasks()

    expect(proposalRevisionsApi.getRevisions).toHaveBeenCalledTimes(1)
    expect(revisionsLoaded.value).toBe(true)
    expect(revisionCount.value).toBe(1)
    expect(latestRevision.value).toEqual(revision)
  })

  it('treats a stale pre-approval read as the same revision, not a new one (#2215 round 2)', async () => {
    const revision = makeRevision({ id: 'rev-1' })
    vi.mocked(proposalRevisionsApi.getRevisions).mockResolvedValue([revision])
    const proposal = ref<ApiProposal | null>(
      makeProposal({ status: 'PendingReview', latestRevisionId: 'rev-1' }),
    )
    const { revisionsLoaded } = useProposalRevisions(proposal)
    await vi.waitFor(() => {
      expect(revisionsLoaded.value).toBe(true)
    })
    expect(proposalRevisionsApi.getRevisions).toHaveBeenCalledTimes(1)

    proposal.value = makeProposal({
      status: 'Approved',
      latestRevisionId: null,
      approvedRevisionId: 'rev-1',
    })
    await nextTick()
    await flushMicrotasks()

    // A queue read issued BEFORE the approval can land after it, restoring the
    // pending shape with `latestRevisionId: rev-1`. That is the SAME revision
    // the approval pinned, so it must not read as a collaborator edit. This is
    // the case the `approvedRevisionId` half of the identity carries: without
    // it the approved row's identity is null, and this read looks like
    // null -> rev-1, which IS a genuine move.
    proposal.value = makeProposal({ status: 'PendingReview', latestRevisionId: 'rev-1' })
    await nextTick()
    await flushMicrotasks()

    expect(proposalRevisionsApi.getRevisions).toHaveBeenCalledTimes(1)
    expect(revisionsLoaded.value).toBe(true)
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
