import { computed, onScopeDispose, ref, watch, type ComputedRef, type Ref } from 'vue'
import { i18n } from '../i18n'
import type { Proposal as ApiProposal } from '../types/automation'
import {
  proposalDeepReviewApi,
  conflictToneWireValues,
  cardHistoryStatusWireValues,
} from '../api/proposalDeepReviewApi'
import type {
  ProvenanceRowDto,
  ProvenanceEvidenceLinkDto,
  ConfidenceBreakdownDto,
  ProposalSideEffectsDto,
  ConflictRowDto,
  CardHistoryRowDto,
  ConflictToneWireValue,
  CardHistoryStatusWireValue,
  SimilarPastResultDto,
} from '../api/proposalDeepReviewApi'

export type ProvenanceWeight = 'primary' | 'contextual' | 'excluded' | 'inferred'

export interface ProvenanceRow {
  icon: string
  key: string
  value: string
  weight: ProvenanceWeight
}

/**
 * `sourceType` value the backend stamps on evidence that points into a stored
 * transcript (`ProvenanceEvidenceLink.TranscriptSourceType`). Such a link's
 * `sourceId` is the transcript id readable through `transcriptsApi`.
 */
export const TRANSCRIPT_EVIDENCE_SOURCE_TYPE = 'Transcript'

/**
 * One provenance evidence link, flattened onto the field it justifies so the
 * drawer can render source, quote, and span together.
 */
export interface EvidenceLink {
  /** The provenance field this evidence supports. */
  sourceKey: string
  /** Character offsets into the source text, or null when the span is unresolved. */
  span: [number, number] | null
  reason: string
  weight: ProvenanceWeight
  /** Backend source discriminator; `'Transcript'` for transcript evidence. */
  sourceType?: string
  /** Identifier within that source; the transcript id for transcript evidence. */
  sourceId?: string
  /**
   * Server-computed: whether THIS caller can read the linked source. The client cannot
   * derive it — provenance is board-authorized while transcript read is owner-only — so a
   * missing value means "not viewable", never "assume yes".
   */
  viewable?: boolean
}

export interface SideEffectRow {
  key: string
  value: string
  tone: 'active' | 'passive'
}

export interface SideEffects {
  rows: SideEffectRow[]
  applyRisk: {
    summary: string
    description: string
  }
}

export interface ConfidenceBreakdown {
  overall: number
  components: Array<{ key: string; value: number }>
  note?: string
  threshold: number
}

/**
 * Confidence as HELD IN STATE: structurally identical to `ConfidenceBreakdown`,
 * but every `components[].key` is still the raw backend wire key. Translation
 * happens in the exposed `computed` (`localizeConfidence`), never at fetch time.
 */
type StoredConfidenceBreakdown = ConfidenceBreakdown

export interface ConflictRow {
  tone: 'warn' | 'info' | 'ok'
  key: string
  value: string
}

export interface HistoryRow {
  serial: string
  event: string
  age: string
  status: 'pending' | 'applied' | 'past' | 'unknown'
}

export interface SimilarPastRow {
  serial: string
  title: string
  verdict: 'applied' | 'rejected'
  date: string
}

export interface PaperReviewSelectors {
  provenance: ComputedRef<ProvenanceRow[]>
  evidenceLinks: ComputedRef<EvidenceLink[]>
  sideEffects: ComputedRef<SideEffects>
  confidenceBreakdown: ComputedRef<ConfidenceBreakdown>
  conflicts: ComputedRef<ConflictRow[]>
  history: ComputedRef<HistoryRow[]>
  similarPast: ComputedRef<SimilarPastRow[]>
  similarPastApplyRate: ComputedRef<{ applied: number; total: number; ratio: number }>
  loading: ComputedRef<boolean>
}

const EMPTY_PROVENANCE: ProvenanceRow[] = Object.freeze([] as ProvenanceRow[]) as ProvenanceRow[]
const EMPTY_EVIDENCE_LINKS: EvidenceLink[] = Object.freeze([] as EvidenceLink[]) as EvidenceLink[]
const EMPTY_CONFLICTS: ConflictRow[] = Object.freeze([] as ConflictRow[]) as ConflictRow[]
const EMPTY_HISTORY: HistoryRow[] = Object.freeze([] as HistoryRow[]) as HistoryRow[]
const EMPTY_SIMILAR: SimilarPastRow[] = Object.freeze([] as SimilarPastRow[]) as SimilarPastRow[]
/**
 * The empty side-effects shape is BUILT PER CALL, not frozen at module load,
 * because its copy comes from the catalogs: a module-level constant would pin
 * the fallback to whatever locale was active when this module first evaluated
 * and never follow a language switch. `i18n.global.t` reads `i18n.global.locale`
 * internally, so building it inside a `computed` keeps it reactive (ADR-0054).
 */
function emptySideEffects(): SideEffects {
  return {
    rows: EMPTY_SIDE_EFFECT_ROWS,
    applyRisk: {
      summary: i18n.global.t('review.sideEffects.fallback.summary'),
      description: i18n.global.t('review.sideEffects.fallback.description'),
    },
  }
}
const EMPTY_SIDE_EFFECT_ROWS: SideEffectRow[] = Object.freeze(
  [] as SideEffectRow[],
) as SideEffectRow[]
const EMPTY_CONFIDENCE: ConfidenceBreakdown = Object.freeze({
  overall: 0,
  components: Object.freeze([] as ConfidenceBreakdown['components']) as ConfidenceBreakdown['components'],
  threshold: 0.7,
}) as ConfidenceBreakdown

const VALID_WEIGHTS = new Set<ProvenanceWeight>(['primary', 'contextual', 'excluded', 'inferred'])

function resolveWeight(dto: ProvenanceRowDto): ProvenanceWeight {
  const weight = dto.weight.toLowerCase() as ProvenanceWeight
  return VALID_WEIGHTS.has(weight) ? weight : 'contextual'
}

function mapProvenanceRow(dto: ProvenanceRowDto): ProvenanceRow {
  return {
    icon: dto.icon,
    key: dto.key,
    value: dto.value,
    weight: resolveWeight(dto),
  }
}

/**
 * Normalizes a wire span into an ordered pair, or null when either bound is
 * missing or incoherent. A malformed span must degrade to "no deep link"
 * rather than to a highlight over the wrong characters.
 *
 * An empty span (`spanEnd === spanStart`) is rejected along with an inverted one:
 * it highlights nothing, so the affordance it would render can only open a viewer
 * on an unresolved span (#1837 item 2).
 */
function mapSpan(link: ProvenanceEvidenceLinkDto): [number, number] | null {
  const { spanStart, spanEnd } = link
  if (typeof spanStart !== 'number' || typeof spanEnd !== 'number') return null
  if (!Number.isInteger(spanStart) || !Number.isInteger(spanEnd)) return null
  if (spanStart < 0 || spanEnd <= spanStart) return null
  return [spanStart, spanEnd]
}

/**
 * Flattens each row's evidence links into drawer rows, carrying the field name
 * as the source key and falling back to the row's rendered value when the link
 * has no label of its own.
 */
function mapEvidenceLinks(dtos: ProvenanceRowDto[]): EvidenceLink[] {
  return dtos.flatMap((dto) => {
    const weight = resolveWeight(dto)
    return (dto.evidenceLinks ?? []).map((link) => ({
      sourceKey: dto.key,
      span: mapSpan(link),
      reason: link.label ?? dto.value,
      weight,
      sourceType: link.sourceType,
      sourceId: link.sourceId,
      // Fails closed: anything but an explicit server `true` means this caller
      // cannot open the source, so the deep-link affordance stays hidden.
      viewable: link.viewable === true,
    }))
  })
}

function unexpectedWireEnum<T>(name: string, value: unknown, fallback: T): T {
  console.error(`[Paper Review] Unexpected ${name} wire value`, value)
  return fallback
}

function mapConflictTone(tone: ConflictToneWireValue): 'warn' | 'info' | 'ok' {
  switch (tone) {
    case conflictToneWireValues.Warn:
      return 'warn'
    case conflictToneWireValues.Info:
      return 'info'
    case conflictToneWireValues.Ok:
      return 'ok'
    default:
      return unexpectedWireEnum('ConflictTone', tone, 'warn')
  }
}

function mapHistoryStatus(status: CardHistoryStatusWireValue): HistoryRow['status'] {
  switch (status) {
    case cardHistoryStatusWireValues.Pending:
      return 'pending'
    case cardHistoryStatusWireValues.Applied:
      return 'applied'
    case cardHistoryStatusWireValues.Past:
      return 'past'
    default:
      return unexpectedWireEnum('CardHistoryStatus', status, 'unknown')
  }
}

function clamp01(v: number): number {
  return Math.max(0, Math.min(1, v))
}

/**
 * `Reversibility` is a backend WIRE VALUE — the comparison is never translated.
 * Only its display relabel is catalog material; every other component key is
 * server-supplied text this client cannot localise.
 */
const REVERSIBILITY_WIRE_KEY = 'Reversibility'

function mapConfidence(dto: ConfidenceBreakdownDto): StoredConfidenceBreakdown {
  return {
    overall: clamp01(dto.overall),
    // Stored with the WIRE key; `localizeConfidence` relabels at read time.
    components: dto.components.map((c) => ({
      key: c.key,
      value: clamp01(c.value),
    })),
    note: dto.note ?? undefined,
    threshold: dto.threshold,
  }
}

/**
 * Resolves the one catalog-driven component label. Called from a `computed`,
 * NOT from the fetch path: `i18n.global.t` reads `i18n.global.locale`
 * internally, so deriving here is what makes the label follow a language
 * switch instead of freezing the locale that was active at deep-review load
 * (ADR-0054 decision 2, #1857 — the same rule `emptySideEffects()` follows).
 */
function localizeConfidence(stored: StoredConfidenceBreakdown): ConfidenceBreakdown {
  return {
    overall: stored.overall,
    components: stored.components.map((c) => ({
      key:
        c.key === REVERSIBILITY_WIRE_KEY
          ? i18n.global.t('review.author.component.operationSafety')
          : c.key,
      value: c.value,
    })),
    note: stored.note,
    threshold: stored.threshold,
  }
}

function mapSideEffects(dto: ProposalSideEffectsDto): SideEffects {
  return {
    rows: dto.rows.map((r) => ({ key: r.key, value: r.value, tone: r.tone })),
    // The API property name is retained for compatibility. Its current semantics are
    // apply risk/manual recovery, not an available undo action.
    applyRisk: {
      summary: dto.reversibility.summary,
      description: dto.reversibility.description,
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
  const evidenceLinksData: Ref<EvidenceLink[]> = ref([])
  // null means "nothing loaded" — the computed below then renders the
  // catalog-driven empty shape, so a language switch re-renders the fallback.
  const sideEffectsData: Ref<SideEffects | null> = ref(null)
  const confidenceData: Ref<StoredConfidenceBreakdown> = ref(EMPTY_CONFIDENCE)
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
        evidenceLinksData.value = EMPTY_EVIDENCE_LINKS
        sideEffectsData.value = null
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

      evidenceLinksData.value =
        prov.status === 'fulfilled' ? mapEvidenceLinks(prov.value) : EMPTY_EVIDENCE_LINKS

      confidenceData.value =
        conf.status === 'fulfilled' ? mapConfidence(conf.value) : EMPTY_CONFIDENCE

      sideEffectsData.value = side.status === 'fulfilled' ? mapSideEffects(side.value) : null

      conflictsData.value =
        confl.status === 'fulfilled' ? mapConflicts(confl.value) : EMPTY_CONFLICTS

      historyData.value = hist.status === 'fulfilled' ? mapHistory(hist.value) : EMPTY_HISTORY

      similarPastData.value =
        sim.status === 'fulfilled' ? mapSimilarPast(sim.value) : EMPTY_SIMILAR
    },
    { immediate: true },
  )

  const provenance = computed<ProvenanceRow[]>(() => provenanceData.value)
  const evidenceLinks = computed<EvidenceLink[]>(() => evidenceLinksData.value)
  const sideEffects = computed<SideEffects>(() => sideEffectsData.value ?? emptySideEffects())
  const confidenceBreakdown = computed<ConfidenceBreakdown>(() =>
    localizeConfidence(confidenceData.value),
  )
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
    // Invalidate the current generation so any in-flight Promise.allSettled
    // continuation short-circuits at the `generation !== fetchGeneration`
    // guard instead of writing reactive state after the scope is gone.
    // (Promise.allSettled resolves even when its inner requests are aborted.)
    fetchGeneration++
    if (abortController) {
      abortController.abort()
      abortController = null
    }
  })

  const loading = computed(() => isLoading.value)

  return {
    provenance,
    evidenceLinks,
    sideEffects,
    confidenceBreakdown,
    conflicts,
    history,
    similarPast,
    similarPastApplyRate,
    loading,
  }
}
