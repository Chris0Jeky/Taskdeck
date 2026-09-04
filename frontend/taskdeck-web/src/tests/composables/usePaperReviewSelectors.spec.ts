import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { ref, computed, nextTick, effectScope } from 'vue'
import { usePaperReviewSelectors } from '../../composables/usePaperReviewSelectors'
import {
  proposalDeepReviewApi,
  type CardHistoryRowDto,
  type ConflictRowDto,
  type ProvenanceRowDto,
} from '../../api/proposalDeepReviewApi'
import { captureApi } from '../../api/captureApi'
import type { Proposal as ApiProposal } from '../../types/automation'
import type { CaptureItem, CaptureProvenance } from '../../types/capture'

vi.mock('../../api/proposalDeepReviewApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/proposalDeepReviewApi')>()
  return {
    ...actual,
    proposalDeepReviewApi: {
      getProvenance: vi.fn(),
      getConfidence: vi.fn(),
      getSideEffects: vi.fn(),
      getConflicts: vi.fn(),
      getHistory: vi.fn(),
      getSimilarPast: vi.fn(),
      getProvenanceMetadata: vi.fn().mockResolvedValue({
        provider: null,
        model: null,
        promptVersion: null,
      }),
    },
  }
})

vi.mock('../../api/captureApi', () => ({
  captureApi: {
    getItem: vi.fn(),
  },
}))

function makeProposal(overrides: Partial<ApiProposal> = {}): ApiProposal {
  return {
    id: 'p-1',
    status: 'Pending',
    riskLevel: 'Low',
    title: 'Test',
    description: 'desc',
    captureItemId: 'c-1',
    boardId: 'b-1',
    operations: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    requestedByUserId: 'u-1',
    appliedAt: null,
    expiresAt: null,
    ...overrides,
  } as ApiProposal
}

function mockAllEndpointsEmpty() {
  vi.mocked(proposalDeepReviewApi.getProvenance).mockResolvedValue([])
  vi.mocked(proposalDeepReviewApi.getConfidence).mockResolvedValue({
    overall: 0.5,
    components: [{ key: 'Operation 1: create card', value: 0.5 }],
    note: null,
    threshold: null,
    source: 'model-reported',
    meetsThreshold: null,
  })
  vi.mocked(proposalDeepReviewApi.getSideEffects).mockResolvedValue({
    rows: [],
    reversibility: {
      summary: 'Low risk · confirm before apply',
      description: 'Confirm affected items.',
      windowMs: 21600000,
    },
  })
  vi.mocked(proposalDeepReviewApi.getConflicts).mockResolvedValue([])
  vi.mocked(proposalDeepReviewApi.getHistory).mockResolvedValue([])
  vi.mocked(proposalDeepReviewApi.getSimilarPast).mockResolvedValue({
    decisions: [],
    applyRate: 0,
  })
  // Default: the proposal recorded no producer, so the capture-detail fallback decides.
  vi.mocked(proposalDeepReviewApi.getProvenanceMetadata).mockResolvedValue({
    provider: null,
    model: null,
    promptVersion: null,
  })
}

function captureDetail(
  provenance: Partial<CaptureProvenance> = {},
  overrides: Partial<CaptureItem> = {},
): CaptureItem {
  return {
    id: 'capture-1',
    userId: 'u-1',
    boardId: 'b-1',
    status: 'ProposalCreated',
    source: 'TranscriptPaste',
    textExcerpt: 'Captured transcript',
    rawText: 'Captured transcript',
    createdAt: '2026-01-01T00:00:00Z',
    processedAt: '2026-01-01T00:01:00Z',
    retryCount: 0,
    provenance: {
      captureItemId: 'capture-1',
      triageRunId: 'triage-1',
      proposalId: 'p-1',
      promptVersion: 'triage.v1',
      ...provenance,
    },
    ...overrides,
  }
}

describe('usePaperReviewSelectors', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('returns empty state when no proposal is active', async () => {
    const activeProposal = computed(() => null)
    const selectors = usePaperReviewSelectors(activeProposal)

    await nextTick()

    expect(selectors.provenance.value).toEqual([])
    expect(selectors.confidenceBreakdown.value.overall).toBeNull()
    expect(selectors.confidenceBreakdown.value.source).toBe('not-reported')
    expect(selectors.conflicts.value).toEqual([])
    expect(selectors.history.value).toEqual([])
    expect(selectors.similarPast.value).toEqual([])
  })

  it('fetches all endpoints when proposal becomes active', async () => {
    mockAllEndpointsEmpty()

    const proposal = ref<ApiProposal | null>(null)
    const activeProposal = computed(() => proposal.value)
    usePaperReviewSelectors(activeProposal)

    proposal.value = makeProposal()
    await nextTick()
    await vi.waitFor(() => {
      expect(proposalDeepReviewApi.getProvenance).toHaveBeenCalledWith('p-1', expect.objectContaining({ signal: expect.any(AbortSignal) }))
    })

    expect(proposalDeepReviewApi.getConfidence).toHaveBeenCalledWith('p-1', expect.objectContaining({ signal: expect.any(AbortSignal) }))
    expect(proposalDeepReviewApi.getSideEffects).toHaveBeenCalledWith('p-1', expect.objectContaining({ signal: expect.any(AbortSignal) }))
    expect(proposalDeepReviewApi.getConflicts).toHaveBeenCalledWith('p-1', expect.objectContaining({ signal: expect.any(AbortSignal) }))
    expect(proposalDeepReviewApi.getHistory).toHaveBeenCalledWith('p-1', expect.objectContaining({ signal: expect.any(AbortSignal) }))
    expect(proposalDeepReviewApi.getSimilarPast).toHaveBeenCalledWith('p-1', expect.objectContaining({ signal: expect.any(AbortSignal) }))
  })

  it('builds deterministic provenance metadata from the active capture without inventing confidence or latency', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getConfidence).mockResolvedValue({
      overall: 0.91,
      components: [{ key: 'Fabricated', value: 0.91 }],
      note: 'Deterministic extraction does not report model confidence.',
      threshold: null,
      source: 'deterministic',
      meetsThreshold: null,
    })
    vi.mocked(captureApi.getItem).mockResolvedValue(
      captureDetail({
        provider: 'deterministic-extractor',
        model: 'capture-triage-v1',
        promptVersion: 'triage.v1',
      }),
    )

    const selectors = usePaperReviewSelectors(
      computed(() =>
        makeProposal({ sourceType: 'Queue', sourceReferenceId: ' capture-1 ' }),
      ),
    )

    await vi.waitFor(() => {
      expect(selectors.provenanceMetadata.value).toEqual({
        provider: 'deterministic-extractor',
        model: 'capture-triage-v1',
        promptVersion: 'triage.v1',
        confidence: null,
        latencyMs: null,
      })
    })
    expect(captureApi.getItem).toHaveBeenCalledOnce()
    expect(captureApi.getItem).toHaveBeenCalledWith(
      'capture-1',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
  })

  it('builds genuine live-provider capture metadata with the supplied model confidence', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getConfidence).mockResolvedValue({
      overall: 0.83,
      components: [],
      note: null,
      threshold: null,
      source: 'model-reported',
      meetsThreshold: null,
    })
    vi.mocked(captureApi.getItem).mockResolvedValue(
      captureDetail({
        provider: 'OpenAI',
        model: 'gpt-4o-mini',
        promptVersion: 'llm-triage.v2',
      }),
    )

    const selectors = usePaperReviewSelectors(
      computed(() => makeProposal({ sourceType: 'Queue', sourceReferenceId: 'capture-1' })),
    )

    await vi.waitFor(() => {
      expect(selectors.provenanceMetadata.value).toEqual({
        provider: 'OpenAI',
        model: 'gpt-4o-mini',
        promptVersion: 'llm-triage.v2',
        confidence: 0.83,
        latencyMs: null,
      })
    })
  })

  it('fails closed for absent and stale capture references', async () => {
    mockAllEndpointsEmpty()
    const proposal = ref<ApiProposal | null>(
      makeProposal({ sourceType: 'Queue', sourceReferenceId: null }),
    )
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))

    await vi.waitFor(() => {
      expect(selectors.loading.value).toBe(false)
    })
    expect(captureApi.getItem).not.toHaveBeenCalled()
    expect(selectors.provenanceMetadata.value).toBeNull()

    vi.mocked(captureApi.getItem).mockResolvedValue(
      captureDetail({
        proposalId: 'another-proposal',
        provider: 'OpenAI',
        model: 'gpt-4o-mini',
        promptVersion: 'llm-triage.v2',
      }),
    )
    proposal.value = makeProposal({
      id: 'p-2',
      sourceType: 'Queue',
      sourceReferenceId: 'capture-1',
    })

    await vi.waitFor(() => {
      expect(captureApi.getItem).toHaveBeenCalledOnce()
    })
    await vi.waitFor(() => {
      expect(selectors.loading.value).toBe(false)
    })
    expect(selectors.provenanceMetadata.value).toBeNull()
  })

  it('maps provenance weight to lowercase', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getProvenance).mockResolvedValue([
      { icon: '📄', key: 'body', value: 'val', weight: 'Primary' },
      { icon: '⊘', key: 'excl', value: 'val', weight: 'Excluded' },
    ])

    const proposal = ref<ApiProposal | null>(makeProposal())
    const activeProposal = computed(() => proposal.value)
    const selectors = usePaperReviewSelectors(activeProposal)

    await vi.waitFor(() => {
      expect(selectors.provenance.value.length).toBe(2)
    })

    expect(selectors.provenance.value[0].weight).toBe('primary')
    expect(selectors.provenance.value[1].weight).toBe('excluded')
  })

  it('flattens provenance evidence links onto the field they justify', async () => {
    mockAllEndpointsEmpty()
    const transcriptId = '3f1c6a2e-9d55-4a10-8f22-2b6f9a1c7d40'
    vi.mocked(proposalDeepReviewApi.getProvenance).mockResolvedValue([
      {
        icon: '📝',
        key: 'title',
        value: 'Extracted: "ship the export fix" (92% match)',
        weight: 'Primary',
        evidenceLinks: [
          {
            sourceType: 'Transcript',
            sourceId: transcriptId,
            label: 'ship the export fix',
            spanStart: 5,
            spanEnd: 24,
            viewable: true,
          },
        ],
      },
      // No links at all, and a link whose span the backend could not resolve.
      { icon: '📄', key: 'description', value: 'Inferred by model (60% confidence)', weight: 'Inferred' },
      {
        icon: '📥',
        key: 'capture',
        value: 'Source field (80% confidence)',
        weight: 'Contextual',
        evidenceLinks: [
          {
            sourceType: 'Transcript',
            sourceId: transcriptId,
            label: null,
            spanStart: null,
            spanEnd: null,
          },
        ],
      },
    ])

    const activeProposal = computed(() => makeProposal())
    const selectors = usePaperReviewSelectors(activeProposal)

    await vi.waitFor(() => {
      expect(selectors.evidenceLinks.value.length).toBe(2)
    })

    expect(selectors.evidenceLinks.value[0]).toEqual({
      sourceKey: 'title',
      span: [5, 24],
      reason: 'ship the export fix',
      weight: 'primary',
      sourceType: 'Transcript',
      sourceId: transcriptId,
      viewable: true,
    })
    // A link without a label falls back to the row's rendered value. Its wire payload carries
    // no `viewable` flag, which must fail closed rather than default to "followable".
    expect(selectors.evidenceLinks.value[1]).toEqual({
      sourceKey: 'capture',
      span: null,
      reason: 'Source field (80% confidence)',
      weight: 'contextual',
      sourceType: 'Transcript',
      sourceId: transcriptId,
      viewable: false,
    })
  })

  it('carries the server viewable flag through and fails closed for anything but true', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getProvenance).mockResolvedValue([
      {
        icon: '📝',
        key: 'title',
        value: 'v',
        weight: 'Primary',
        evidenceLinks: [
          { sourceType: 'Transcript', sourceId: 't-1', label: null, spanStart: 0, spanEnd: 4, viewable: true },
          { sourceType: 'Transcript', sourceId: 't-2', label: null, spanStart: 0, spanEnd: 4, viewable: false },
          // A collaborator's payload from a server that never sends the flag.
          { sourceType: 'Transcript', sourceId: 't-3', label: null, spanStart: 0, spanEnd: 4 },
        ],
      },
    ])

    const activeProposal = computed(() => makeProposal())
    const selectors = usePaperReviewSelectors(activeProposal)

    await vi.waitFor(() => {
      expect(selectors.evidenceLinks.value.length).toBe(3)
    })

    expect(selectors.evidenceLinks.value.map((link) => link.viewable)).toEqual([
      true,
      false,
      false,
    ])
  })

  it('drops an incoherent evidence span rather than deep-linking to wrong characters', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getProvenance).mockResolvedValue([
      {
        icon: '📝',
        key: 'title',
        value: 'v',
        weight: 'Primary',
        evidenceLinks: [
          { sourceType: 'Transcript', sourceId: 't-1', label: null, spanStart: 40, spanEnd: 12 },
          { sourceType: 'Transcript', sourceId: 't-2', label: null, spanStart: -3, spanEnd: 12 },
          // Zero-length: highlights nothing, so it is not a deep link either (#1837 item 2).
          { sourceType: 'Transcript', sourceId: 't-3', label: null, spanStart: 12, spanEnd: 12 },
          { sourceType: 'Transcript', sourceId: 't-4', label: null, spanStart: 0, spanEnd: 0 },
        ],
      },
    ])

    const activeProposal = computed(() => makeProposal())
    const selectors = usePaperReviewSelectors(activeProposal)

    await vi.waitFor(() => {
      expect(selectors.evidenceLinks.value.length).toBe(4)
    })

    expect(selectors.evidenceLinks.value.map((link) => link.span)).toEqual([
      null,
      null,
      null,
      null,
    ])
  })

  it('keeps a one-character span, the smallest span that highlights anything', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getProvenance).mockResolvedValue([
      {
        icon: '📝',
        key: 'title',
        value: 'v',
        weight: 'Primary',
        evidenceLinks: [
          { sourceType: 'Transcript', sourceId: 't-1', label: null, spanStart: 12, spanEnd: 13 },
        ],
      },
    ])

    const activeProposal = computed(() => makeProposal())
    const selectors = usePaperReviewSelectors(activeProposal)

    await vi.waitFor(() => {
      expect(selectors.evidenceLinks.value.length).toBe(1)
    })

    expect(selectors.evidenceLinks.value[0].span).toEqual([12, 13])
  })

  it('clears evidence links when no proposal is active', async () => {
    const activeProposal = computed(() => null)
    const selectors = usePaperReviewSelectors(activeProposal)

    await nextTick()

    expect(selectors.evidenceLinks.value).toEqual([])
  })

  it('maps serialized numeric conflict tones from the API wire contract', async () => {
    mockAllEndpointsEmpty()
    const serializedRows = JSON.parse(`[
      {"tone":0,"key":"stale","value":"v"},
      {"tone":2,"key":"clear","value":"v"},
      {"tone":1,"key":"note","value":"v"}
    ]`) as ConflictRowDto[]
    vi.mocked(proposalDeepReviewApi.getConflicts).mockResolvedValue(serializedRows)

    const proposal = ref<ApiProposal | null>(makeProposal())
    const activeProposal = computed(() => proposal.value)
    const selectors = usePaperReviewSelectors(activeProposal)

    await vi.waitFor(() => {
      expect(selectors.conflicts.value.length).toBe(3)
    })

    expect(selectors.conflicts.value[0].tone).toBe('warn')
    expect(selectors.conflicts.value[1].tone).toBe('ok')
    expect(selectors.conflicts.value[2].tone).toBe('info')
  })

  it('maps serialized numeric history statuses from the API wire contract', async () => {
    mockAllEndpointsEmpty()
    const serializedRows = JSON.parse(`[
      {"serial":"#1","event":"created","age":"2h","status":0},
      {"serial":"#2","event":"applied","age":"1d","status":1},
      {"serial":"#3","event":"old","age":"5d","status":2}
    ]`) as CardHistoryRowDto[]
    vi.mocked(proposalDeepReviewApi.getHistory).mockResolvedValue(serializedRows)

    const proposal = ref<ApiProposal | null>(makeProposal())
    const activeProposal = computed(() => proposal.value)
    const selectors = usePaperReviewSelectors(activeProposal)

    await vi.waitFor(() => {
      expect(selectors.history.value.length).toBe(3)
    })

    expect(selectors.history.value[0].status).toBe('pending')
    expect(selectors.history.value[1].status).toBe('applied')
    expect(selectors.history.value[2].status).toBe('past')
  })

  it('fails closed for unexpected runtime enum values without crashing', async () => {
    mockAllEndpointsEmpty()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const conflicts = JSON.parse('[{"tone":99,"key":"unknown","value":"v"}]') as ConflictRowDto[]
    const history = JSON.parse('[{"serial":"#1","event":"unknown","age":"now","status":99}]') as CardHistoryRowDto[]
    vi.mocked(proposalDeepReviewApi.getConflicts).mockResolvedValue(conflicts)
    vi.mocked(proposalDeepReviewApi.getHistory).mockResolvedValue(history)

    const proposal = ref<ApiProposal | null>(makeProposal())
    const activeProposal = computed(() => proposal.value)
    const selectors = usePaperReviewSelectors(activeProposal)

    await vi.waitFor(() => {
      expect(selectors.conflicts.value).toHaveLength(1)
      expect(selectors.history.value).toHaveLength(1)
    })

    expect(selectors.conflicts.value[0].tone).toBe('warn')
    expect(selectors.history.value[0].status).toBe('unknown')
    expect(consoleError).toHaveBeenCalledWith(
      '[Paper Review] Unexpected ConflictTone wire value',
      99,
    )
    expect(consoleError).toHaveBeenCalledWith(
      '[Paper Review] Unexpected CardHistoryStatus wire value',
      99,
    )

    consoleError.mockRestore()
  })

  it('computes similarPastApplyRate from decisions', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getSimilarPast).mockResolvedValue({
      decisions: [
        { serial: '#1', title: 'A', verdict: 'Applied', date: 'wk1' },
        { serial: '#2', title: 'B', verdict: 'Rejected', date: 'wk2' },
        { serial: '#3', title: 'C', verdict: 'Applied', date: 'wk3' },
      ],
      applyRate: 0.67,
    })

    const proposal = ref<ApiProposal | null>(makeProposal())
    const activeProposal = computed(() => proposal.value)
    const selectors = usePaperReviewSelectors(activeProposal)

    await vi.waitFor(() => {
      expect(selectors.similarPast.value.length).toBe(3)
    })

    expect(selectors.similarPastApplyRate.value.applied).toBe(2)
    expect(selectors.similarPastApplyRate.value.total).toBe(3)
    expect(selectors.similarPastApplyRate.value.ratio).toBeCloseTo(0.667, 2)
  })

  it('fails an exact batch and retries it instead of caching partial evidence as settled', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getConflicts).mockResolvedValueOnce([
      { tone: 0, key: 'existing-warning', value: 'Review this first' },
    ])
    const proposal = ref<ApiProposal | null>(makeProposal({ latestRevisionId: 'rev-1' }))
    const activeProposal = computed(() => proposal.value)
    const selectors = usePaperReviewSelectors(activeProposal)

    await expect(selectors.waitForCoreBatch('p-1', 'rev-1')).resolves.toBe('settled')
    expect(selectors.conflicts.value[0]?.key).toBe('existing-warning')

    vi.mocked(proposalDeepReviewApi.getHistory).mockRejectedValueOnce(new Error('fail'))
    proposal.value = makeProposal({ latestRevisionId: 'rev-2' })
    await nextTick()

    await expect(selectors.waitForCoreBatch('p-1', 'rev-2')).resolves.toBe('failed')
    expect(selectors.loading.value).toBe(false)
    // A partial rev-2 answer is not published as measured-empty evidence, and
    // the coherent rev-1 batch is dropped rather than shown as rev-2 evidence.
    expect(selectors.conflicts.value).toEqual([])
    expect(selectors.confidenceBreakdown.value.overall).toBeNull()
    expect(proposalDeepReviewApi.getHistory).toHaveBeenCalledTimes(2)

    await expect(selectors.waitForCoreBatch('p-1', 'rev-2')).resolves.toBe('settled')
    expect(proposalDeepReviewApi.getHistory).toHaveBeenCalledTimes(3)
    expect(selectors.conflicts.value).toEqual([])
  })

  it('drops the previous key evidence when the next batch fails', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getConflicts).mockResolvedValueOnce([
      { tone: 0, key: 'existing-warning', value: 'Review this first' },
    ])
    const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))

    await expect(selectors.waitForCoreBatch('p-1', null)).resolves.toBe('settled')
    expect(selectors.conflicts.value[0]?.key).toBe('existing-warning')

    vi.mocked(proposalDeepReviewApi.getHistory).mockRejectedValueOnce(new Error('fail'))
    proposal.value = makeProposal({ id: 'p-2' })
    await nextTick()

    await expect(selectors.waitForCoreBatch('p-2', null)).resolves.toBe('failed')
    // p-1 evidence must never render under the p-2 header. The same drop
    // applies to a revision move within one proposal (covered above).
    expect(selectors.conflicts.value).toEqual([])
    expect(selectors.provenance.value).toEqual([])
    expect(selectors.confidenceBreakdown.value.overall).toBeNull()
    expect(selectors.loading.value).toBe(false)
  })

  it('does not publish successful siblings from an incomplete automatic batch', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getProvenance).mockRejectedValue(new Error('fail'))

    const proposal = ref<ApiProposal | null>(makeProposal())
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))

    await vi.waitFor(() => {
      expect(selectors.loading.value).toBe(false)
    })

    expect(selectors.provenance.value).toEqual([])
    expect(selectors.history.value).toEqual([])
    expect(selectors.conflicts.value).toEqual([])
    expect(selectors.confidenceBreakdown.value.overall).toBeNull()
  })

  it('discards stale responses when proposal changes rapidly', async () => {
    let resolveFirst: (v: never[]) => void
    const firstCall = new Promise<never[]>((r) => { resolveFirst = r })
    vi.mocked(proposalDeepReviewApi.getProvenance)
      .mockImplementationOnce(() => firstCall)
      .mockResolvedValueOnce([{ icon: '✦', key: 'new', value: 'v', weight: 'inferred' }])
    mockAllEndpointsEmpty()

    const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
    const activeProposal = computed(() => proposal.value)
    const selectors = usePaperReviewSelectors(activeProposal)

    await nextTick()
    proposal.value = makeProposal({ id: 'p-2' })
    await nextTick()

    resolveFirst!([{ icon: '📄', key: 'stale', value: 'v', weight: 'primary' }] as never[])
    await nextTick()

    await vi.waitFor(() => {
      expect(selectors.provenance.value.length).toBeGreaterThanOrEqual(0)
    })

    const hasStale = selectors.provenance.value.some((r) => r.key === 'stale')
    expect(hasStale).toBe(false)
  })

  it('starts and awaits one exact revision selector batch without waiting for capture metadata', async () => {
    mockAllEndpointsEmpty()
    let resolveHistory!: (rows: CardHistoryRowDto[]) => void
    vi.mocked(proposalDeepReviewApi.getHistory).mockReturnValueOnce(
      new Promise<CardHistoryRowDto[]>((resolve) => {
        resolveHistory = resolve
      }),
    )
    vi.mocked(captureApi.getItem).mockReturnValueOnce(new Promise<CaptureItem>(() => {}))
    const proposal = ref<ApiProposal | null>(null)
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))

    proposal.value = makeProposal({
      sourceType: 'Queue',
      sourceReferenceId: 'capture-1',
      latestRevisionId: 'rev-2',
    })
    let settled = false
    const batch = selectors.waitForCoreBatch('P-1', 'REV-2').then((outcome) => {
      settled = true
      return outcome
    })
    await nextTick()

    expect(proposalDeepReviewApi.getProvenance).toHaveBeenCalledOnce()
    expect(proposalDeepReviewApi.getConfidence).toHaveBeenCalledOnce()
    expect(proposalDeepReviewApi.getSideEffects).toHaveBeenCalledOnce()
    expect(proposalDeepReviewApi.getConflicts).toHaveBeenCalledOnce()
    expect(proposalDeepReviewApi.getHistory).toHaveBeenCalledOnce()
    expect(proposalDeepReviewApi.getSimilarPast).toHaveBeenCalledOnce()
    expect(captureApi.getItem).toHaveBeenCalledOnce()
    expect(settled).toBe(false)

    resolveHistory([])
    await expect(batch).resolves.toBe('settled')
    expect(settled).toBe(true)
    expect(selectors.loading.value).toBe(false)
  })

  it('supersedes an exact selector waiter when proposal context moves', async () => {
    mockAllEndpointsEmpty()
    let resolveFirst!: (rows: ProvenanceRowDto[]) => void
    vi.mocked(proposalDeepReviewApi.getProvenance).mockReturnValueOnce(
      new Promise<ProvenanceRowDto[]>((resolve) => {
        resolveFirst = resolve
      }),
    )
    const proposal = ref<ApiProposal | null>(makeProposal({ latestRevisionId: 'rev-1' }))
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))
    const first = selectors.waitForCoreBatch('p-1', 'rev-1')

    proposal.value = makeProposal({ id: 'p-2', latestRevisionId: 'rev-1' })
    await nextTick()

    await expect(first).resolves.toBe('superseded')
    resolveFirst([{ icon: 'stale', key: 'stale', value: 'old', weight: 'Primary' }])
    await vi.waitFor(() => {
      expect(selectors.loading.value).toBe(false)
    })
    expect(selectors.provenance.value.some((row) => row.key === 'stale')).toBe(false)
  })

  it('supersedes an active B batch when returning to already-settled A', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getProvenance).mockResolvedValueOnce([
      { icon: 'a', key: 'proposal-a', value: 'A', weight: 'Primary' },
    ])
    const proposal = ref<ApiProposal | null>(
      makeProposal({ id: 'proposal-a', latestRevisionId: 'rev-a' }),
    )
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))
    await expect(selectors.waitForCoreBatch('proposal-a', 'rev-a')).resolves.toBe('settled')

    let resolveB!: (rows: ProvenanceRowDto[]) => void
    let bSignal: AbortSignal | undefined
    vi.mocked(proposalDeepReviewApi.getProvenance).mockImplementationOnce((_id, options) => {
      bSignal = options?.signal
      return new Promise<ProvenanceRowDto[]>((resolve) => {
        resolveB = resolve
      })
    })
    proposal.value = makeProposal({ id: 'proposal-b', latestRevisionId: 'rev-b' })
    await nextTick()
    const bBatch = selectors.waitForCoreBatch('proposal-b', 'rev-b')
    expect(selectors.loading.value).toBe(true)

    proposal.value = makeProposal({ id: 'proposal-a', latestRevisionId: 'rev-a' })
    await expect(selectors.waitForCoreBatch('proposal-a', 'rev-a')).resolves.toBe('settled')

    expect(bSignal?.aborted).toBe(true)
    await expect(bBatch).resolves.toBe('superseded')
    expect(selectors.loading.value).toBe(false)
    expect(selectors.provenance.value[0]?.key).toBe('proposal-a')

    resolveB([{ icon: 'b', key: 'proposal-b', value: 'B', weight: 'Primary' }])
    await nextTick()
    expect(selectors.provenance.value[0]?.key).toBe('proposal-a')
  })

  it('restarts pending capture metadata when returning to settled A', async () => {
    mockAllEndpointsEmpty()
    let aCaptureSignal: AbortSignal | undefined
    let bCaptureSignal: AbortSignal | undefined
    vi.mocked(captureApi.getItem)
      .mockImplementationOnce((_id, options) => {
        aCaptureSignal = options?.signal
        return new Promise<CaptureItem>(() => {})
      })
      .mockImplementationOnce((_id, options) => {
        bCaptureSignal = options?.signal
        return new Promise<CaptureItem>(() => {})
      })
      .mockResolvedValueOnce(
        captureDetail({
          captureItemId: 'capture-a',
          proposalId: 'proposal-a',
          provider: 'OpenAI',
          model: 'gpt-4o-mini',
          promptVersion: 'llm-triage.v2',
        }, { id: 'capture-a' }),
      )
    const proposal = ref<ApiProposal | null>(makeProposal({
      id: 'proposal-a',
      sourceType: 'Queue',
      sourceReferenceId: 'capture-a',
      latestRevisionId: 'rev-a',
    }))
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))
    await expect(selectors.waitForCoreBatch('proposal-a', 'rev-a')).resolves.toBe('settled')

    let resolveB!: (rows: ProvenanceRowDto[]) => void
    vi.mocked(proposalDeepReviewApi.getProvenance).mockImplementationOnce(
      () => new Promise<ProvenanceRowDto[]>((resolve) => { resolveB = resolve }),
    )
    proposal.value = makeProposal({
      id: 'proposal-b',
      sourceType: 'Queue',
      sourceReferenceId: 'capture-b',
      latestRevisionId: 'rev-b',
    })
    await nextTick()
    expect(aCaptureSignal?.aborted).toBe(true)

    proposal.value = makeProposal({
      id: 'proposal-a',
      sourceType: 'Queue',
      sourceReferenceId: 'capture-a',
      latestRevisionId: 'rev-a',
    })
    await expect(selectors.waitForCoreBatch('proposal-a', 'rev-a')).resolves.toBe('settled')

    expect(bCaptureSignal?.aborted).toBe(true)
    expect(captureApi.getItem).toHaveBeenCalledTimes(3)
    expect(proposalDeepReviewApi.getProvenance).toHaveBeenCalledTimes(2)
    await vi.waitFor(() => {
      expect(selectors.provenanceMetadata.value?.provider).toBe('OpenAI')
    })

    resolveB([])
  })

  it('reuses an already-settled exact selector batch without duplicate requests', async () => {
    mockAllEndpointsEmpty()
    const proposal = ref<ApiProposal | null>(
      makeProposal({ latestRevisionId: 'rev-1' }),
    )
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))

    await expect(selectors.waitForCoreBatch('p-1', 'rev-1')).resolves.toBe('settled')
    const calls = [
      proposalDeepReviewApi.getProvenance,
      proposalDeepReviewApi.getConfidence,
      proposalDeepReviewApi.getSideEffects,
      proposalDeepReviewApi.getConflicts,
      proposalDeepReviewApi.getHistory,
      proposalDeepReviewApi.getSimilarPast,
    ].map((endpoint) => vi.mocked(endpoint).mock.calls.length)

    await expect(selectors.waitForCoreBatch('P-1', 'REV-1')).resolves.toBe('settled')
    expect([
      proposalDeepReviewApi.getProvenance,
      proposalDeepReviewApi.getConfidence,
      proposalDeepReviewApi.getSideEffects,
      proposalDeepReviewApi.getConflicts,
      proposalDeepReviewApi.getHistory,
      proposalDeepReviewApi.getSimilarPast,
    ].map((endpoint) => vi.mocked(endpoint).mock.calls.length)).toEqual(calls)
  })

  it('reports unavailable when the requested revision is not the active selector key', async () => {
    mockAllEndpointsEmpty()
    const proposal = ref<ApiProposal | null>(makeProposal({ latestRevisionId: 'rev-2' }))
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))

    await expect(selectors.waitForCoreBatch('p-1', 'rev-1')).resolves.toBe('unavailable')
  })

  it('restarts deep-review selectors for a moved revision and reuses capture metadata', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(captureApi.getItem).mockResolvedValue(
      captureDetail({
        provider: 'OpenAI',
        model: 'gpt-4o-mini',
        promptVersion: 'llm-triage.v2',
      }),
    )

    let resolveFirst: (rows: never[]) => void
    const first = new Promise<never[]>((resolve) => { resolveFirst = resolve })
    let resolveSecond: (rows: never[]) => void
    const second = new Promise<never[]>((resolve) => { resolveSecond = resolve })
    let firstSignal: AbortSignal | undefined
    let secondSignal: AbortSignal | undefined
    vi.mocked(proposalDeepReviewApi.getProvenance)
      .mockImplementationOnce((_id, options) => {
        firstSignal = options?.signal
        return first
      })
      .mockImplementationOnce((_id, options) => {
        secondSignal = options?.signal
        return second
      })
    vi.mocked(proposalDeepReviewApi.getConfidence)
      .mockResolvedValueOnce({
        overall: 0.2,
        components: [],
        note: null,
        threshold: null,
        source: 'model-reported',
        meetsThreshold: null,
      })
      .mockResolvedValueOnce({
        overall: 0.9,
        components: [],
        note: null,
        threshold: null,
        source: 'model-reported',
        meetsThreshold: null,
      })

    const proposal = ref<ApiProposal | null>(makeProposal({
      sourceType: 'Queue',
      sourceReferenceId: 'capture-1',
      latestRevisionId: 'rev-1',
    }))
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))

    await vi.waitFor(() => {
      expect(proposalDeepReviewApi.getProvenance).toHaveBeenCalledOnce()
    })
    expect(selectors.loading.value).toBe(true)

    proposal.value = makeProposal({
      sourceType: 'Queue',
      sourceReferenceId: 'capture-1',
      latestRevisionId: 'rev-2',
    })

    await vi.waitFor(() => {
      expect(proposalDeepReviewApi.getProvenance).toHaveBeenCalledTimes(2)
      expect(proposalDeepReviewApi.getConfidence).toHaveBeenCalledTimes(2)
      expect(proposalDeepReviewApi.getSideEffects).toHaveBeenCalledTimes(2)
      expect(proposalDeepReviewApi.getConflicts).toHaveBeenCalledTimes(2)
      expect(proposalDeepReviewApi.getHistory).toHaveBeenCalledTimes(2)
      expect(proposalDeepReviewApi.getSimilarPast).toHaveBeenCalledTimes(2)
    })
    expect(firstSignal?.aborted).toBe(true)
    expect(secondSignal?.aborted).toBe(false)
    expect(selectors.loading.value).toBe(true)
    expect(captureApi.getItem).toHaveBeenCalledOnce()

    resolveFirst!([{ icon: 'ðŸ“„', key: 'stale', value: 'old', weight: 'primary' }] as never[])
    await nextTick()
    expect(selectors.provenance.value.some((row) => row.key === 'stale')).toBe(false)
    expect(selectors.loading.value).toBe(true)

    resolveSecond!([{ icon: 'âœ¦', key: 'fresh', value: 'new', weight: 'primary' }] as never[])
    await vi.waitFor(() => {
      expect(selectors.loading.value).toBe(false)
      expect(selectors.provenance.value[0]?.key).toBe('fresh')
    })
    expect(selectors.provenanceMetadata.value?.confidence).toBe(0.9)
    expect(captureApi.getItem).toHaveBeenCalledOnce()
  })

  it('does not reload when latest revision moves to its approved revision', async () => {
    mockAllEndpointsEmpty()
    const proposal = ref<ApiProposal | null>(makeProposal({
      latestRevisionId: 'rev-1',
      approvedRevisionId: null,
    }))
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))

    await vi.waitFor(() => {
      expect(selectors.loading.value).toBe(false)
    })
    const calls = [
      proposalDeepReviewApi.getProvenance,
      proposalDeepReviewApi.getConfidence,
      proposalDeepReviewApi.getSideEffects,
      proposalDeepReviewApi.getConflicts,
      proposalDeepReviewApi.getHistory,
      proposalDeepReviewApi.getSimilarPast,
    ].map((endpoint) => vi.mocked(endpoint).mock.calls.length)

    proposal.value = makeProposal({
      latestRevisionId: null,
      approvedRevisionId: 'rev-1',
    })
    await nextTick()
    await nextTick()

    const callsAfterApproval = [
      proposalDeepReviewApi.getProvenance,
      proposalDeepReviewApi.getConfidence,
      proposalDeepReviewApi.getSideEffects,
      proposalDeepReviewApi.getConflicts,
      proposalDeepReviewApi.getHistory,
      proposalDeepReviewApi.getSimilarPast,
    ].map((endpoint) => vi.mocked(endpoint).mock.calls.length)
    expect(callsAfterApproval).toEqual(calls)
  })

  it('refetches capture metadata when the proposal changes with the same reference', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(captureApi.getItem).mockResolvedValue(captureDetail())
    const proposal = ref<ApiProposal | null>(makeProposal({
      sourceType: 'Queue',
      sourceReferenceId: 'capture-1',
    }))
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))

    await vi.waitFor(() => {
      expect(selectors.loading.value).toBe(false)
      expect(captureApi.getItem).toHaveBeenCalledOnce()
    })

    proposal.value = makeProposal({
      id: 'p-2',
      sourceType: 'Queue',
      sourceReferenceId: 'capture-1',
    })
    await vi.waitFor(() => {
      expect(selectors.loading.value).toBe(false)
      expect(captureApi.getItem).toHaveBeenCalledTimes(2)
    })
  })

  it('aborts a pending capture lookup when its queue reference is removed', async () => {
    mockAllEndpointsEmpty()
    let captureSignal: AbortSignal | undefined
    vi.mocked(captureApi.getItem).mockImplementation((_id, options) => {
      captureSignal = options?.signal
      return new Promise<CaptureItem>(() => {})
    })
    const proposal = ref<ApiProposal | null>(makeProposal({
      sourceType: 'Queue',
      sourceReferenceId: 'capture-1',
    }))
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))

    await vi.waitFor(() => {
      expect(selectors.loading.value).toBe(false)
      expect(captureSignal?.aborted).toBe(false)
    })

    proposal.value = makeProposal({
      sourceType: 'Queue',
      sourceReferenceId: null,
    })
    await vi.waitFor(() => {
      expect(captureSignal?.aborted).toBe(true)
      expect(selectors.provenanceMetadata.value).toBeNull()
    })
    expect(captureApi.getItem).toHaveBeenCalledOnce()
  })

  it('commits fresh core review data while optional capture metadata is still pending', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getProvenance)
      .mockResolvedValueOnce([{ icon: '📄', key: 'stale', value: 'old', weight: 'Primary' }])
      .mockResolvedValueOnce([{ icon: '✦', key: 'fresh', value: 'new', weight: 'Primary' }])

    let resolveCapture: (capture: CaptureItem) => void
    vi.mocked(captureApi.getItem).mockImplementation(
      () => new Promise<CaptureItem>((resolve) => { resolveCapture = resolve }),
    )

    const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
    const selectors = usePaperReviewSelectors(computed(() => proposal.value))

    await vi.waitFor(() => {
      expect(selectors.provenance.value[0]?.key).toBe('stale')
    })

    proposal.value = makeProposal({
      id: 'p-2',
      sourceType: 'Queue',
      sourceReferenceId: 'capture-2',
    })

    await vi.waitFor(() => {
      expect(captureApi.getItem).toHaveBeenCalledWith(
        'capture-2',
        expect.objectContaining({ signal: expect.any(AbortSignal) }),
      )
      expect(selectors.provenance.value[0]?.key).toBe('fresh')
      expect(selectors.loading.value).toBe(false)
    })
    expect(selectors.provenanceMetadata.value).toBeNull()

    proposal.value = makeProposal({ id: 'p-3', sourceType: 'Manual' })
    await vi.waitFor(() => {
      expect(selectors.provenance.value).toEqual([])
    })

    resolveCapture!(
      captureDetail(
        {
          captureItemId: 'capture-2',
          proposalId: 'p-2',
          provider: 'OpenAI',
          model: 'gpt-4o-mini',
          promptVersion: 'llm-triage.v2',
        },
        { id: 'capture-2' },
      ),
    )
    await nextTick()
    await nextTick()
    expect(selectors.provenanceMetadata.value).toBeNull()
  })

  it('maps confidence note from null to undefined', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getConfidence).mockResolvedValue({
      overall: 0.9,
      components: [],
      note: null,
      threshold: null,
      source: 'model-reported',
      meetsThreshold: null,
    })

    const proposal = ref<ApiProposal | null>(makeProposal())
    const activeProposal = computed(() => proposal.value)
    const selectors = usePaperReviewSelectors(activeProposal)

    await vi.waitFor(() => {
      expect(selectors.confidenceBreakdown.value.overall).toBe(0.9)
    })

    expect(selectors.confidenceBreakdown.value.note).toBeUndefined()
  })

  it('aborts in-flight requests and discards their result on scope disposal', async () => {
    let resolveProvenance: (v: never[]) => void
    const pending = new Promise<never[]>((r) => { resolveProvenance = r })
    let capturedSignal: AbortSignal | undefined
    vi.mocked(proposalDeepReviewApi.getProvenance).mockImplementation((_id, opts) => {
      capturedSignal = opts?.signal
      return pending
    })
    let resolveCapture: (capture: CaptureItem) => void
    let captureSignal: AbortSignal | undefined
    vi.mocked(captureApi.getItem).mockImplementation((_id, opts) => {
      captureSignal = opts?.signal
      return new Promise<CaptureItem>((resolve) => { resolveCapture = resolve })
    })
    vi.mocked(proposalDeepReviewApi.getConfidence).mockResolvedValue({
      overall: 0.5,
      components: [],
      note: null,
      threshold: null,
      source: 'model-reported',
      meetsThreshold: null,
    })
    vi.mocked(proposalDeepReviewApi.getSideEffects).mockResolvedValue({
      rows: [],
      reversibility: {
        summary: 'Low risk · confirm before apply',
        description: 'Confirm affected items.',
        windowMs: 21600000,
      },
    })
    vi.mocked(proposalDeepReviewApi.getConflicts).mockResolvedValue([])
    vi.mocked(proposalDeepReviewApi.getHistory).mockResolvedValue([])
    vi.mocked(proposalDeepReviewApi.getSimilarPast).mockResolvedValue({ decisions: [], applyRate: 0 })

    const scope = effectScope()
    let selectors!: ReturnType<typeof usePaperReviewSelectors>
    scope.run(() => {
      const proposal = ref<ApiProposal | null>(makeProposal({
        id: 'p-1',
        sourceType: 'Queue',
        sourceReferenceId: 'capture-1',
      }))
      const activeProposal = computed(() => proposal.value)
      selectors = usePaperReviewSelectors(activeProposal)
    })

    await nextTick()
    expect(capturedSignal?.aborted).toBe(false)
    expect(captureSignal?.aborted).toBe(false)

    // Tear the scope down while the provenance request is still in flight.
    scope.stop()
    expect(capturedSignal?.aborted).toBe(true)
    expect(captureSignal?.aborted).toBe(true)

    // Resolve the previously in-flight request after disposal; the generation
    // guard must prevent any reactive write-back.
    resolveProvenance!([{ icon: '📄', key: 'late', value: 'v', weight: 'primary' }] as never[])
    resolveCapture!(captureDetail({
      provider: 'OpenAI',
      model: 'gpt-4o-mini',
      promptVersion: 'llm-triage.v2',
    }))
    await nextTick()
    await nextTick()

    expect(selectors.provenance.value.some((r) => r.key === 'late')).toBe(false)
    expect(selectors.provenanceMetadata.value).toBeNull()
    expect(selectors.loading.value).toBe(true)
  })

  it('maps the stable reversibility contract into an apply-risk posture', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getSideEffects).mockResolvedValue({
      rows: [{ key: 'Cards', value: '1 created', tone: 'active' }],
      reversibility: {
        summary: 'High risk · inspect every change',
        description: 'Review targets and downstream effects before applying.',
        windowMs: 21600000,
      },
    })

    const proposal = ref<ApiProposal | null>(makeProposal())
    const activeProposal = computed(() => proposal.value)
    const selectors = usePaperReviewSelectors(activeProposal)

    await vi.waitFor(() => {
      expect(selectors.sideEffects.value.rows.length).toBe(1)
    })

    expect(selectors.sideEffects.value.applyRisk).toEqual({
      summary: 'High risk · inspect every change',
      description: 'Review targets and downstream effects before applying.',
    })
    expect('windowMs' in selectors.sideEffects.value.applyRisk).toBe(false)
  })

  it('maps exact model-reported operation confidence without relabelling it', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getConfidence).mockResolvedValue({
      overall: 0.8,
      components: [{ key: 'Operation 1: create card', value: 0.75 }],
      note: null,
      threshold: null,
      source: 'model-reported',
      meetsThreshold: null,
    })
    const selectors = usePaperReviewSelectors(computed(() => makeProposal()))

    await vi.waitFor(() => {
      expect(selectors.confidenceBreakdown.value.components).toEqual([
        { key: 'Operation 1: create card', value: 0.75 },
      ])
    })
    expect(selectors.confidenceBreakdown.value.source).toBe('model-reported')
  })

  it('fails deterministic provenance closed even if a malformed response includes numbers', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getConfidence).mockResolvedValue({
      overall: 0.91,
      components: [{ key: 'Fabricated', value: 0.91 }],
      note: 'Deterministic extraction does not report model confidence.',
      threshold: null,
      source: 'deterministic',
      meetsThreshold: null,
    })
    const selectors = usePaperReviewSelectors(computed(() => makeProposal()))

    await vi.waitFor(() => {
      expect(selectors.confidenceBreakdown.value.source).toBe('deterministic')
    })

    expect(selectors.confidenceBreakdown.value.overall).toBeNull()
    expect(selectors.confidenceBreakdown.value.components).toEqual([])
  })

  describe('proposal-scoped provenance metadata (#1987)', () => {
    it('renders the server-recorded triple for a proposal with no capture link', async () => {
      mockAllEndpointsEmpty()
      vi.mocked(proposalDeepReviewApi.getProvenanceMetadata).mockResolvedValue({
        provider: 'openai',
        model: 'gpt-5.6-luna',
        promptVersion: 'llm-triage.v2',
      })

      const selectors = usePaperReviewSelectors(
        computed(() => makeProposal({ sourceType: 'Chat', sourceReferenceId: null })),
      )

      await vi.waitFor(() => {
        expect(selectors.provenanceMetadata.value).toEqual({
          provider: 'openai',
          model: 'gpt-5.6-luna',
          promptVersion: 'llm-triage.v2',
          confidence: 0.5,
          latencyMs: null,
        })
      })
      // The proposal endpoint alone answered; no owner-only capture read was needed.
      expect(captureApi.getItem).not.toHaveBeenCalled()
    })

    it('prefers the server-recorded triple over the capture-detail fallback', async () => {
      mockAllEndpointsEmpty()
      vi.mocked(proposalDeepReviewApi.getProvenanceMetadata).mockResolvedValue({
        provider: 'openai',
        model: 'gpt-5.6-luna',
        promptVersion: 'llm-triage.v2',
      })
      vi.mocked(captureApi.getItem).mockResolvedValue(
        captureDetail({
          provider: 'stale-capture-provider',
          model: 'stale-capture-model',
          promptVersion: 'stale.v0',
        }),
      )

      const selectors = usePaperReviewSelectors(
        computed(() => makeProposal({ sourceType: 'Queue', sourceReferenceId: 'capture-1' })),
      )

      await vi.waitFor(() => {
        expect(selectors.provenanceMetadata.value?.provider).toBe('openai')
      })
      expect(selectors.provenanceMetadata.value?.model).toBe('gpt-5.6-luna')
      expect(selectors.provenanceMetadata.value?.promptVersion).toBe('llm-triage.v2')
    })

    it('falls back to capture detail only when the proposal recorded no producer', async () => {
      mockAllEndpointsEmpty()
      vi.mocked(proposalDeepReviewApi.getProvenanceMetadata).mockResolvedValue({
        provider: null,
        model: null,
        promptVersion: null,
      })
      vi.mocked(captureApi.getItem).mockResolvedValue(
        captureDetail({
          provider: 'deterministic-extractor',
          model: 'capture-triage-v1',
          promptVersion: 'triage.v1',
        }),
      )

      const selectors = usePaperReviewSelectors(
        computed(() => makeProposal({ sourceType: 'Queue', sourceReferenceId: 'capture-1' })),
      )

      await vi.waitFor(() => {
        expect(selectors.provenanceMetadata.value?.provider).toBe('deterministic-extractor')
      })
      expect(selectors.provenanceMetadata.value?.model).toBe('capture-triage-v1')
    })

    it('renders no claim when neither source recorded a producer', async () => {
      mockAllEndpointsEmpty()
      vi.mocked(captureApi.getItem).mockResolvedValue(captureDetail())

      const selectors = usePaperReviewSelectors(
        computed(() => makeProposal({ sourceType: 'Queue', sourceReferenceId: 'capture-1' })),
      )

      await vi.waitFor(() => {
        expect(proposalDeepReviewApi.getProvenanceMetadata).toHaveBeenCalled()
      })
      await nextTick()
      expect(selectors.provenanceMetadata.value).toBeNull()
    })

    it('renders no claim when the metadata lookup fails', async () => {
      mockAllEndpointsEmpty()
      vi.mocked(proposalDeepReviewApi.getProvenanceMetadata).mockRejectedValue(
        new Error('Network error'),
      )

      const selectors = usePaperReviewSelectors(
        computed(() => makeProposal({ sourceType: 'Chat', sourceReferenceId: null })),
      )

      // A failed optional read must not become a producer claim, and must not stall the
      // six core selectors that gate Apply.
      await vi.waitFor(() => {
        expect(selectors.loading.value).toBe(false)
      })
      expect(selectors.provenanceMetadata.value).toBeNull()
    })

    it('does not delay the core selectors on the optional metadata read', async () => {
      mockAllEndpointsEmpty()
      let releaseMetadata: (value: {
        provider: string | null
        model: string | null
        promptVersion: string | null
      }) => void = () => {}
      vi.mocked(proposalDeepReviewApi.getProvenanceMetadata).mockReturnValue(
        new Promise((resolve) => {
          releaseMetadata = resolve
        }),
      )

      const selectors = usePaperReviewSelectors(
        computed(() => makeProposal({ sourceType: 'Chat', sourceReferenceId: null })),
      )

      await vi.waitFor(() => {
        expect(selectors.loading.value).toBe(false)
      })
      expect(selectors.provenanceMetadata.value).toBeNull()

      releaseMetadata({ provider: 'openai', model: 'm', promptVersion: 'v' })
      await vi.waitFor(() => {
        expect(selectors.provenanceMetadata.value?.provider).toBe('openai')
      })
    })
  })
})
