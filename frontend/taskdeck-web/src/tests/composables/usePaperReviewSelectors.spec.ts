import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { ref, computed, nextTick, effectScope } from 'vue'
import { usePaperReviewSelectors } from '../../composables/usePaperReviewSelectors'
import {
  proposalDeepReviewApi,
  type CardHistoryRowDto,
  type ConflictRowDto,
} from '../../api/proposalDeepReviewApi'
import type { Proposal as ApiProposal } from '../../types/automation'

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
    },
  }
})

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
    components: [{ key: 'Pattern', value: 0.5 }],
    note: null,
    threshold: 0.7,
    meetsThreshold: false,
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
    expect(selectors.confidenceBreakdown.value.overall).toBe(0)
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

  it('gracefully handles individual endpoint failures', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getProvenance).mockRejectedValue(new Error('fail'))
    vi.mocked(proposalDeepReviewApi.getHistory).mockRejectedValue(new Error('fail'))

    const proposal = ref<ApiProposal | null>(makeProposal())
    const activeProposal = computed(() => proposal.value)
    const selectors = usePaperReviewSelectors(activeProposal)

    await vi.waitFor(() => {
      expect(selectors.confidenceBreakdown.value.overall).toBe(0.5)
    })

    expect(selectors.provenance.value).toEqual([])
    expect(selectors.history.value).toEqual([])
    expect(selectors.conflicts.value).toEqual([])
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

  it('maps confidence note from null to undefined', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getConfidence).mockResolvedValue({
      overall: 0.9,
      components: [],
      note: null,
      threshold: 0.7,
      meetsThreshold: true,
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
    vi.mocked(proposalDeepReviewApi.getConfidence).mockResolvedValue({
      overall: 0.5,
      components: [],
      note: null,
      threshold: 0.7,
      meetsThreshold: false,
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
      const proposal = ref<ApiProposal | null>(makeProposal({ id: 'p-1' }))
      const activeProposal = computed(() => proposal.value)
      selectors = usePaperReviewSelectors(activeProposal)
    })

    await nextTick()
    expect(capturedSignal?.aborted).toBe(false)

    // Tear the scope down while the provenance request is still in flight.
    scope.stop()
    expect(capturedSignal?.aborted).toBe(true)

    // Resolve the previously in-flight request after disposal; the generation
    // guard must prevent any reactive write-back.
    resolveProvenance!([{ icon: '📄', key: 'late', value: 'v', weight: 'primary' }] as never[])
    await nextTick()
    await nextTick()

    expect(selectors.provenance.value.some((r) => r.key === 'late')).toBe(false)
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

  it('maps the stable confidence key to truthful user-facing copy', async () => {
    mockAllEndpointsEmpty()
    vi.mocked(proposalDeepReviewApi.getConfidence).mockResolvedValue({
      overall: 0.8,
      components: [{ key: 'Reversibility', value: 0.75 }],
      note: null,
      threshold: 0.7,
      meetsThreshold: true,
    })
    const selectors = usePaperReviewSelectors(computed(() => makeProposal()))

    await vi.waitFor(() => {
      expect(selectors.confidenceBreakdown.value.components).toEqual([
        { key: 'Operation safety', value: 0.75 },
      ])
    })
  })
})
