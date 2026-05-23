import { computed, onScopeDispose, ref, watch, type ComputedRef, type Ref } from 'vue'
import type { Proposal as ApiProposal } from '../types/automation'
import { proposalDeepReviewApi } from '../api/proposalDeepReviewApi'
import type {
  ProvenanceRowDto,
  ConfidenceBreakdownDto,
  ProposalSideEffectsDto,
  ConflictRowDto,
  CardHistoryRowDto,
  SimilarPastResultDto,
} from '../api/proposalDeepReviewApi'

export type ProvenanceWeight = 'primary' | 'contextual' | 'excluded' | 'inferred'

export interface ProvenanceRow {
  icon: string
  key: string
  value: string
  weight: ProvenanceWeight
}

export interface SideEffectRow {
  key: string
  value: string
  tone: 'active' | 'passive'
}

export interface SideEffects {
  rows: SideEffectRow[]
  reversibility: {
    summary: string
    description: string
    windowMs: number
    appliedAt: number | null
  }
}

export interface ConfidenceBreakdown {
  overall: number
  components: Array<{ key: string; value: number }>
  note?: string
  threshold: number
}

export interface ConflictRow {
  tone: 'warn' | 'info' | 'ok'
  key: string
  value: string
}

export interface HistoryRow {
  serial: string
  event: string
  age: string
  status: 'pending' | 'applied' | 'past'
}

export interface SimilarPastRow {
  serial: string
  title: string
  verdict: 'applied' | 'rejected'
  date: string
}

export interface PaperReviewSelectors {
  provenance: ComputedRef<ProvenanceRow[]>
  sideEffects: ComputedRef<SideEffects>
  confidenceBreakdown: ComputedRef<ConfidenceBreakdown>
  conflicts: ComputedRef<ConflictRow[]>
  history: ComputedRef<HistoryRow[]>
  similarPast: ComputedRef<SimilarPastRow[]>
  similarPastApplyRate: ComputedRef<{ applied: number; total: number; ratio: number }>
  loading: ComputedRef<boolean>
}

const EMPTY_PROVENANCE: ProvenanceRow[] = Object.freeze([] as ProvenanceRow[]) as ProvenanceRow[]
const EMPTY_CONFLICTS: ConflictRow[] = Object.freeze([] as ConflictRow[]) as ConflictRow[]
const EMPTY_HISTORY: HistoryRow[] = Object.freeze([] as HistoryRow[]) as HistoryRow[]
const EMPTY_SIMILAR: SimilarPastRow[] = Object.freeze([] as SimilarPastRow[]) as SimilarPastRow[]
const EMPTY_SIDE_EFFECTS: SideEffects = Object.freeze({
  rows: Object.freeze([] as SideEffectRow[]) as SideEffectRow[],
  reversibility: Object.freeze({
    summary: '6 hours · single keystroke',
    description: 'Undo restores the prior state. Nothing is lost.',
    windowMs: 6 * 60 * 60 * 1000,
    appliedAt: null,
  }) as SideEffects['reversibility'],
}) as SideEffects
const EMPTY_CONFIDENCE: ConfidenceBreakdown = Object.freeze({
  overall: 0,
  components: Object.freeze([] as ConfidenceBreakdown['components']) as ConfidenceBreakdown['components'],
  threshold: 0.7,
}) as ConfidenceBreakdown

const VALID_WEIGHTS = new Set<ProvenanceWeight>(['primary', 'contextual', 'excluded', 'inferred'])

function mapProvenanceRow(dto: ProvenanceRowDto): ProvenanceRow {
  const weight = dto.weight.toLowerCase() as ProvenanceWeight
  return {
    icon: dto.icon,
    key: dto.key,
    value: dto.value,
    weight: VALID_WEIGHTS.has(weight) ? weight : 'contextual',
  }
}

function mapConflictTone(tone: string): 'warn' | 'info' | 'ok' {
  switch (tone.toLowerCase()) {
    case 'warn':
      return 'warn'
    case 'ok':
      return 'ok'
    default:
      return 'info'
  }
}

function mapHistoryStatus(status: string): 'pending' | 'applied' | 'past' {
  switch (status.toLowerCase()) {
    case 'pending':
      return 'pending'
    case 'applied':
      return 'applied'
    default:
      return 'past'
  }
}

function clamp01(v: number): number {
  return Math.max(0, Math.min(1, v))
}

function mapConfidence(dto: ConfidenceBreakdownDto): ConfidenceBreakdown {
  return {
    overall: clamp01(dto.overall),
    components: dto.components.map((c) => ({ key: c.key, value: clamp01(c.value) })),
    note: dto.note ?? undefined,
    threshold: dto.threshold,
  }
}

function mapSideEffects(dto: ProposalSideEffectsDto, appliedAt: number | null): SideEffects {
  return {
    rows: dto.rows.map((r) => ({ key: r.key, value: r.value, tone: r.tone })),
    reversibility: {
      summary: dto.reversibility.summary,
      description: dto.reversibility.description,
      windowMs: dto.reversibility.windowMs,
      appliedAt,
    },
  }
}

function mapConflicts(dtos: ConflictRowDto[]): ConflictRow[] {
  return dtos.map((d) => ({ tone: mapConflictTone(d.tone), key: d.key, value: d.value }))
}

function mapHistory(dtos: CardHistoryRowDto[]): HistoryRow[] {
  return dtos.map((d) => ({
    serial: d.serial,
    event: d.event,
    age: d.age,
    status: mapHistoryStatus(d.status),
  }))
}

function mapSimilarPast(dto: SimilarPastResultDto): SimilarPastRow[] {
  return dto.decisions.map((d) => ({
    serial: d.serial,
    title: d.title,
    verdict: d.verdict.toLowerCase() === 'applied' ? 'applied' : 'rejected',
    date: d.date,
  }))
}

export function usePaperReviewSelectors(
  activeProposal: ComputedRef<ApiProposal | null>,
): PaperReviewSelectors {
  const provenanceData: Ref<ProvenanceRow[]> = ref([])
  const sideEffectsData: Ref<SideEffects> = ref(EMPTY_SIDE_EFFECTS)
  const confidenceData: Ref<ConfidenceBreakdown> = ref(EMPTY_CONFIDENCE)
  const conflictsData: Ref<ConflictRow[]> = ref([])
  const historyData: Ref<HistoryRow[]> = ref([])
  const similarPastData: Ref<SimilarPastRow[]> = ref([])
  const isLoading = ref(false)

  let fetchGeneration = 0
  let abortController: AbortController | null = null

  watch(
    () => activeProposal.value?.id,
    async (proposalId) => {
      // Abort any in-flight requests from the previous watcher invocation
      if (abortController) {
        abortController.abort()
        abortController = null
      }

      if (!proposalId) {
        isLoading.value = false
        provenanceData.value = EMPTY_PROVENANCE
        sideEffectsData.value = EMPTY_SIDE_EFFECTS
        confidenceData.value = EMPTY_CONFIDENCE
        conflictsData.value = EMPTY_CONFLICTS
        historyData.value = EMPTY_HISTORY
        similarPastData.value = EMPTY_SIMILAR
        return
      }

      const generation = ++fetchGeneration
      const controller = new AbortController()
      abortController = controller
      const signal = controller.signal

      const proposal = activeProposal.value
      const appliedAt = proposal?.appliedAt ? new Date(proposal.appliedAt).getTime() : null

      isLoading.value = true

      const results = await Promise.allSettled([
        proposalDeepReviewApi.getProvenance(proposalId, { signal }),
        proposalDeepReviewApi.getConfidence(proposalId, { signal }),
        proposalDeepReviewApi.getSideEffects(proposalId, { signal }),
        proposalDeepReviewApi.getConflicts(proposalId, { signal }),
        proposalDeepReviewApi.getHistory(proposalId, { signal }),
        proposalDeepReviewApi.getSimilarPast(proposalId, { signal }),
      ])

      if (generation !== fetchGeneration) return

      isLoading.value = false

      const [prov, conf, side, confl, hist, sim] = results

      provenanceData.value =
        prov.status === 'fulfilled' ? prov.value.map(mapProvenanceRow) : EMPTY_PROVENANCE

      confidenceData.value =
        conf.status === 'fulfilled' ? mapConfidence(conf.value) : EMPTY_CONFIDENCE

      sideEffectsData.value =
        side.status === 'fulfilled' ? mapSideEffects(side.value, appliedAt) : EMPTY_SIDE_EFFECTS

      conflictsData.value =
        confl.status === 'fulfilled' ? mapConflicts(confl.value) : EMPTY_CONFLICTS

      historyData.value = hist.status === 'fulfilled' ? mapHistory(hist.value) : EMPTY_HISTORY

      similarPastData.value =
        sim.status === 'fulfilled' ? mapSimilarPast(sim.value) : EMPTY_SIMILAR
    },
    { immediate: true },
  )

  const provenance = computed<ProvenanceRow[]>(() => provenanceData.value)
  const sideEffects = computed<SideEffects>(() => sideEffectsData.value)
  const confidenceBreakdown = computed<ConfidenceBreakdown>(() => confidenceData.value)
  const conflicts = computed<ConflictRow[]>(() => conflictsData.value)
  const history = computed<HistoryRow[]>(() => historyData.value)
  const similarPast = computed<SimilarPastRow[]>(() => similarPastData.value)

  const similarPastApplyRate = computed(() => {
    const rows = similarPast.value
    const applied = rows.filter((r) => r.verdict === 'applied').length
    const total = rows.length
    return { applied, total, ratio: total === 0 ? 0 : applied / total }
  })

  onScopeDispose(() => {
    if (abortController) {
      abortController.abort()
      abortController = null
    }
  })

  const loading = computed(() => isLoading.value)

  return {
    provenance,
    sideEffects,
    confidenceBreakdown,
    conflicts,
    history,
    similarPast,
    similarPastApplyRate,
    loading,
  }
}
