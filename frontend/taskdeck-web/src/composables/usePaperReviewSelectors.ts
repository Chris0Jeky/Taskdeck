import { computed, onScopeDispose, ref, watch, type ComputedRef, type Ref } from 'vue'
import { i18n } from '../i18n'
import { captureApi } from '../api/captureApi'
import type { Proposal as ApiProposal } from '../types/automation'
import type { CaptureItem } from '../types/capture'
import { normalizeProposalSourceType } from '../utils/automation'
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
  ProposalProvenanceMetadataDto,
} from '../api/proposalDeepReviewApi'
import {
  proposalIdsEqual,
  proposalRevisionIdentity,
  proposalRevisionMoved,
} from '../utils/proposalIdentity'

export type ProvenanceWeight = 'primary' | 'contextual' | 'excluded' | 'inferred'

export interface ProvenanceRow {
  icon: string
  key: string
  value: string
  weight: ProvenanceWeight
}

/**
 * Server-recorded producer metadata for the active capture-linked proposal.
 * Confidence and latency remain nullable because neither may be invented when
 * their source endpoint does not provide a trustworthy value.
 */
export interface ProvenanceMetadata {
  provider: string
  model: string | null
  promptVersion: string | null
  confidence: number | null
  latencyMs: number | null
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
  overall: number | null
  components: Array<{ key: string; value: number }>
  note?: string
  threshold: null
  source: ConfidenceValueSource
}

export type ConfidenceValueSource =
  | 'model-reported'
  | 'deterministic'
  | 'derived'
  | 'not-reported'

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

/**
 * How one exact-key core evidence batch ended.
 *
 * - `settled`    every one of the six proposal reads landed for the requested key.
 * - `failed`     at least one read rejected on its own; the snapshot is incomplete.
 * - `superseded` the reviewer moved to another proposal or revision mid-flight.
 * - `unavailable` the requested key is not the one this surface is rendering.
 * - `aborted`    the CALLER cancelled through its own signal. Kept distinct from
 *   `failed` because nothing failed: only the caller knows why it cancelled
 *   (a deadline, a teardown), so only the caller may name that reason.
 */
export type CoreSelectorBatchOutcome =
  | 'settled'
  | 'failed'
  | 'superseded'
  | 'unavailable'
  | 'aborted'

/** Caller-owned cancellation for one explicit core-batch wait. */
export interface CoreSelectorBatchWaitOptions {
  signal?: AbortSignal
}

export interface PaperReviewSelectors {
  provenance: ComputedRef<ProvenanceRow[]>
  provenanceMetadata: ComputedRef<ProvenanceMetadata | null>
  evidenceLinks: ComputedRef<EvidenceLink[]>
  sideEffects: ComputedRef<SideEffects>
  confidenceBreakdown: ComputedRef<ConfidenceBreakdown>
  conflicts: ComputedRef<ConflictRow[]>
  history: ComputedRef<HistoryRow[]>
  similarPast: ComputedRef<SimilarPastRow[]>
  similarPastApplyRate: ComputedRef<{ applied: number; total: number; ratio: number }>
  loading: ComputedRef<boolean>
  waitForCoreBatch: (
    proposalId: string,
    revisionIdentity: string | null,
    options?: CoreSelectorBatchWaitOptions,
  ) => Promise<CoreSelectorBatchOutcome>
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
  overall: null,
  components: Object.freeze([] as ConfidenceBreakdown['components']) as ConfidenceBreakdown['components'],
  threshold: null,
  source: 'not-reported',
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

function meaningfulWireValue(value: string | null | undefined): string | null {
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  if (trimmed === '' || trimmed.toLowerCase() === 'unknown') return null
  return trimmed
}

function identifiersEqual(left: string | null | undefined, right: string | null | undefined): boolean {
  const normalizedLeft = meaningfulWireValue(left)?.toLowerCase()
  const normalizedRight = meaningfulWireValue(right)?.toLowerCase()
  return normalizedLeft !== undefined && normalizedLeft === normalizedRight
}

function nullableIdentifiersEqual(
  left: string | null | undefined,
  right: string | null | undefined,
): boolean {
  if (left == null || right == null) return left == null && right == null
  return identifiersEqual(left, right)
}

interface SelectorKey {
  proposalId: string
  captureReference: string | null
  revisionIdentity: string | null
}

function selectorKeyForProposal(proposal: ApiProposal | null): SelectorKey | null {
  if (!proposal?.id) return null
  return {
    proposalId: proposal.id,
    captureReference: captureSourceReference(proposal),
    revisionIdentity: proposalRevisionIdentity(proposal),
  }
}

function selectorKeysEqual(left: SelectorKey | null, right: SelectorKey | null): boolean {
  if (!left || !right) return left === right
  return (
    proposalIdsEqual(left.proposalId, right.proposalId) &&
    nullableIdentifiersEqual(left.captureReference, right.captureReference) &&
    nullableIdentifiersEqual(left.revisionIdentity, right.revisionIdentity)
  )
}

/**
 * Only Queue proposals carry capture ids in `sourceReferenceId`. Chat and Manual
 * references belong to different domains and must never be probed as capture ids.
 */
function captureSourceReference(proposal: ApiProposal | null): string | null {
  if (!proposal) return null
  // Runtime payloads predating sourceType (and intentionally partial test fixtures) fail closed.
  if (typeof proposal.sourceType !== 'string' && typeof proposal.sourceType !== 'number') return null
  if (normalizeProposalSourceType(proposal.sourceType) !== 'Queue') return null
  return meaningfulWireValue(proposal.sourceReferenceId)
}

function mapProvenanceMetadata(
  capture: CaptureItem,
  proposalId: string,
  captureReference: string,
  confidence: ConfidenceBreakdown,
): ProvenanceMetadata | null {
  const provenance = capture.provenance
  if (!provenance) return null

  // Fail closed if a stale sourceReference resolves to a different capture or proposal.
  if (
    !identifiersEqual(capture.id, captureReference) ||
    !identifiersEqual(provenance.captureItemId, captureReference) ||
    !identifiersEqual(provenance.proposalId, proposalId)
  ) {
    return null
  }

  const provider = meaningfulWireValue(provenance.provider)
  if (provider === null) return null

  return {
    provider,
    model: meaningfulWireValue(provenance.model),
    promptVersion: meaningfulWireValue(provenance.promptVersion),
    // `mapConfidence` already suppresses numbers for deterministic/not-reported sources.
    confidence: confidence.overall,
    // Capture provenance does not currently record triage latency. Keep the row absent.
    latencyMs: null,
  }
}

/**
 * Maps the proposal-scoped, server-recorded producer triple (#1987). This is the primary source:
 * it is board-authorized, covers proposals with no capture link, and needs no owner-only read.
 *
 * A null provider means the server recorded no producer, which must render as no claim at all.
 */
function mapServerProvenanceMetadata(
  dto: ProposalProvenanceMetadataDto | null | undefined,
  confidence: ConfidenceBreakdown,
): ProvenanceMetadata | null {
  const provider = meaningfulWireValue(dto?.provider)
  if (provider === null) return null

  return {
    provider,
    model: meaningfulWireValue(dto?.model),
    promptVersion: meaningfulWireValue(dto?.promptVersion),
    // `mapConfidence` already suppresses numbers for deterministic/not-reported sources.
    confidence: confidence.overall,
    // Proposal provenance does not record triage latency. Keep the row absent.
    latencyMs: null,
  }
}

/**
 * Chooses which recorded producer to render. The proposal endpoint wins whenever it recorded one;
 * the #2310 capture-detail path stays as a fallback for proposals stamped before the proposal-side
 * triple existed, and is consulted only when the endpoint recorded nothing AND a capture link
 * exists. Both sources are server-recorded, so neither can be a client-supplied claim, and when
 * neither recorded anything the result is null — no claim rather than a guessed one.
 */
function resolveProvenanceMetadata(
  serverResult: PromiseSettledResult<ProposalProvenanceMetadataDto> | undefined,
  captureResult: PromiseSettledResult<CaptureItem | null> | undefined,
  key: SelectorKey,
  confidence: ConfidenceBreakdown,
): ProvenanceMetadata | null {
  const serverMetadata =
    serverResult?.status === 'fulfilled'
      ? mapServerProvenanceMetadata(serverResult.value, confidence)
      : null
  if (serverMetadata) return serverMetadata

  if (!key.captureReference) return null
  return captureResult?.status === 'fulfilled' && captureResult.value
    ? mapProvenanceMetadata(captureResult.value, key.proposalId, key.captureReference, confidence)
    : null
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

const CONFIDENCE_SOURCES = new Set<ConfidenceValueSource>([
  'model-reported',
  'deterministic',
  'derived',
  'not-reported',
])

function mapConfidence(dto: ConfidenceBreakdownDto): ConfidenceBreakdown {
  const source = CONFIDENCE_SOURCES.has(dto.source) ? dto.source : 'not-reported'
  const canShowNumber = source === 'model-reported' || source === 'derived'
  return {
    overall:
      canShowNumber && typeof dto.overall === 'number' && Number.isFinite(dto.overall)
        ? clamp01(dto.overall)
        : null,
    components: canShowNumber
      ? dto.components
          .filter((component) => Number.isFinite(component.value))
          .map((component) => ({
            key: component.key,
            value: clamp01(component.value),
          }))
      : [],
    note: dto.note ?? undefined,
    threshold: null,
    source,
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
  const provenanceMetadataData: Ref<ProvenanceMetadata | null> = ref(null)
  const evidenceLinksData: Ref<EvidenceLink[]> = ref([])
  // null means "nothing loaded" — the computed below then renders the
  // catalog-driven empty shape, so a language switch re-renders the fallback.
  const sideEffectsData: Ref<SideEffects | null> = ref(null)
  const confidenceData: Ref<ConfidenceBreakdown> = ref(EMPTY_CONFIDENCE)
  const conflictsData: Ref<ConflictRow[]> = ref([])
  const historyData: Ref<HistoryRow[]> = ref([])
  const similarPastData: Ref<SimilarPastRow[]> = ref([])
  const isLoading = ref(false)

  let fetchGeneration = 0
  let abortController: AbortController | null = null
  let settledCoreKey: SelectorKey | null = null
  let settledCaptureMetadata: {
    key: SelectorKey
    value: ProvenanceMetadata | null
  } | null = null
  let activeCoreBatch: {
    key: SelectorKey
    generation: number
    promise: Promise<CoreSelectorBatchOutcome>
    supersede: () => void
  } | null = null
  let captureLookup: {
    reference: string
    request: Promise<CaptureItem | null>
    controller: AbortController
  } | null = null

  function discardCaptureLookup() {
    captureLookup?.controller.abort()
    captureLookup = null
  }

  function getCaptureLookup(
    reference: string,
    reuseExisting: boolean,
  ): Promise<CaptureItem | null> {
    if (reuseExisting && captureLookup && identifiersEqual(captureLookup.reference, reference)) {
      return captureLookup.request
    }

    discardCaptureLookup()
    const controller = new AbortController()
    const request = captureApi.getItem(reference, { signal: controller.signal })
    captureLookup = { reference, request, controller }
    return request
  }

  function clearSelectorData() {
    provenanceData.value = EMPTY_PROVENANCE
    provenanceMetadataData.value = null
    evidenceLinksData.value = EMPTY_EVIDENCE_LINKS
    sideEffectsData.value = null
    confidenceData.value = EMPTY_CONFIDENCE
    conflictsData.value = EMPTY_CONFLICTS
    historyData.value = EMPTY_HISTORY
    similarPastData.value = EMPTY_SIMILAR
  }

  /**
   * Cancel whatever core batch is currently in flight without disturbing the
   * generation bookkeeping. `invalidateCoreBatch` is the stronger neighbour: it
   * also supersedes the batch's waiters. Here the waiter reports its own
   * outcome, so the batch is left to reach its ordinary failure branch (which
   * clears `loading`) instead of being declared superseded.
   */
  function abortInFlightCoreBatch() {
    abortController?.abort()
  }

  function invalidateCoreBatch() {
    fetchGeneration += 1
    activeCoreBatch?.supersede()
    activeCoreBatch = null
    if (abortController) {
      abortController.abort()
      abortController = null
    }
  }

  function refreshCaptureMetadataForSettledKey(key: SelectorKey) {
    const generation = fetchGeneration
    // The proposal-scoped read runs for every proposal, capture-linked or not; the capture
    // lookup is still made only for a genuine capture reference.
    const captureSettlement = Promise.allSettled([
      proposalDeepReviewApi.getProvenanceMetadata(key.proposalId),
      key.captureReference
        ? getCaptureLookup(key.captureReference, true)
        : Promise.resolve(null),
    ])
    void captureSettlement.then(([serverMetadata, capture]) => {
      if (
        generation !== fetchGeneration ||
        !selectorKeysEqual(settledCoreKey, key) ||
        !selectorKeysEqual(selectorKeyForProposal(activeProposal.value), key)
      ) return
      const metadata = resolveProvenanceMetadata(
        serverMetadata,
        capture,
        key,
        confidenceData.value,
      )
      settledCaptureMetadata = { key, value: metadata }
      provenanceMetadataData.value = metadata
    })
  }

  function ensureCoreBatch(key: SelectorKey): Promise<CoreSelectorBatchOutcome> {
    if (!selectorKeysEqual(selectorKeyForProposal(activeProposal.value), key)) {
      return Promise.resolve('unavailable')
    }
    if (selectorKeysEqual(settledCoreKey, key)) {
      // A can already be the rendered settled batch while B is still loading.
      // Returning B -> A must cancel B before taking this cache fast path;
      // otherwise B's ignored continuation leaves `loading` true and its
      // transport work continues after the reviewer has left it.
      if (activeCoreBatch && !selectorKeysEqual(activeCoreBatch.key, key)) {
        invalidateCoreBatch()
        discardCaptureLookup()
      }
      isLoading.value = false
      if (settledCaptureMetadata && selectorKeysEqual(settledCaptureMetadata.key, key)) {
        provenanceMetadataData.value = settledCaptureMetadata.value
      } else {
        provenanceMetadataData.value = null
        // The earlier A metadata request may have been aborted when B became
        // active. Retry only that optional read; the exact A core batch remains
        // settled and the Apply waiter must not wait for this enrichment.
        refreshCaptureMetadataForSettledKey(key)
      }
      return Promise.resolve('settled')
    }
    if (activeCoreBatch && selectorKeysEqual(activeCoreBatch.key, key)) {
      return activeCoreBatch.promise
    }

    const previousKey = activeCoreBatch?.key ?? settledCoreKey
    const proposalChanged =
      !previousKey || !proposalIdsEqual(previousKey.proposalId, key.proposalId)
    const captureChanged =
      !previousKey ||
      !nullableIdentifiersEqual(previousKey.captureReference, key.captureReference)

    invalidateCoreBatch()
    const generation = fetchGeneration
    if (proposalChanged || captureChanged) discardCaptureLookup()

    const controller = new AbortController()
    abortController = controller
    const signal = controller.signal
    isLoading.value = true
    // Never show the previous proposal's producer while the active capture is loading.
    provenanceMetadataData.value = null

    const captureRequest: Promise<CaptureItem | null> = key.captureReference
      ? getCaptureLookup(key.captureReference, !proposalChanged && !captureChanged)
      : Promise.resolve(null)
    // Attach rejection handling immediately, but keep optional producer metadata
    // outside the core waiter. Apply is gated on the six proposal selectors, not on
    // a provenance embellishment that may retry for longer.
    const captureSettlement = Promise.allSettled([
      proposalDeepReviewApi.getProvenanceMetadata(key.proposalId, { signal }),
      captureRequest,
    ])

    let resolveSuperseded!: (outcome: CoreSelectorBatchOutcome) => void
    const superseded = new Promise<CoreSelectorBatchOutcome>((resolve) => {
      resolveSuperseded = resolve
    })
    const work = (async (): Promise<CoreSelectorBatchOutcome> => {
      const results = await Promise.allSettled([
        proposalDeepReviewApi.getProvenance(key.proposalId, { signal }),
        proposalDeepReviewApi.getConfidence(key.proposalId, { signal }),
        proposalDeepReviewApi.getSideEffects(key.proposalId, { signal }),
        proposalDeepReviewApi.getConflicts(key.proposalId, { signal }),
        proposalDeepReviewApi.getHistory(key.proposalId, { signal }),
        proposalDeepReviewApi.getSimilarPast(key.proposalId, { signal }),
      ])

      if (
        generation !== fetchGeneration ||
        !selectorKeysEqual(selectorKeyForProposal(activeProposal.value), key)
      ) return 'superseded'

      const [prov, conf, side, confl, hist, sim] = results

      if (
        prov.status === 'rejected' ||
        conf.status === 'rejected' ||
        side.status === 'rejected' ||
        confl.status === 'rejected' ||
        hist.status === 'rejected' ||
        sim.status === 'rejected'
      ) {
        // These six reads form one evidence snapshot. Publishing successful
        // siblings would turn unavailable evidence into affirmative empty
        // states and let this revision key masquerade as fully refreshed.
        if (settledCoreKey && !selectorKeysEqual(settledCoreKey, key)) {
          // The last coherent batch answers a different key, so it is evidence
          // for another proposal or another revision. Rendering it under this
          // header would present superseded evidence as current, which is
          // worse than an honest empty panel: drop it instead.
          settledCoreKey = null
          settledCaptureMetadata = null
          discardCaptureLookup()
          clearSelectorData()
        }
        isLoading.value = false
        return 'failed'
      }

      provenanceData.value = prov.value.map(mapProvenanceRow)

      evidenceLinksData.value = mapEvidenceLinks(prov.value)

      const mappedConfidence = mapConfidence(conf.value)
      confidenceData.value = mappedConfidence

      sideEffectsData.value = mapSideEffects(side.value)

      conflictsData.value = mapConflicts(confl.value)

      historyData.value = mapHistory(hist.value)

      similarPastData.value = mapSimilarPast(sim.value)

      settledCoreKey = key
      isLoading.value = false

      void captureSettlement.then(([serverMetadata, capture]) => {
        if (
          generation !== fetchGeneration ||
          !selectorKeysEqual(selectorKeyForProposal(activeProposal.value), key)
        ) return
        const metadata = resolveProvenanceMetadata(
          serverMetadata,
          capture,
          key,
          mappedConfidence,
        )
        settledCaptureMetadata = { key, value: metadata }
        provenanceMetadataData.value = metadata
      })

      return 'settled'
    })()

    const promise = Promise.race([work, superseded])
    const batch = {
      key,
      generation,
      promise,
      supersede: () => resolveSuperseded('superseded'),
    }
    activeCoreBatch = batch
    void promise.then((outcome) => {
      // Preserve a failed automatic batch long enough for the next explicit
      // Apply waiter to consume it. The waiter immediately starts the retry
      // within that same action, while a later failed retry remains available
      // for the next deliberate action.
      if (outcome !== 'failed' && activeCoreBatch === batch) activeCoreBatch = null
    })
    return promise
  }

  /**
   * Await the exact-key core batch on behalf of an explicit reviewer action.
   *
   * The optional `signal` belongs to the CALLER, not to this composable. When
   * it fires the wait resolves `aborted` immediately and the in-flight batch is
   * cancelled, so a caller holding a decision lock is released even when the
   * transport (or a test double) ignores cancellation and never settles. The
   * batch's own continuations still run behind their generation guard, so a
   * late answer can neither publish evidence for a key the reviewer has left
   * nor be mistaken for this wait's result.
   */
  function waitForCoreBatch(
    proposalId: string,
    revisionIdentity: string | null,
    options?: CoreSelectorBatchWaitOptions,
  ): Promise<CoreSelectorBatchOutcome> {
    const signal = options?.signal
    if (signal?.aborted) return Promise.resolve('aborted')
    const key = selectorKeyForProposal(activeProposal.value)
    if (
      !key ||
      !proposalIdsEqual(key.proposalId, proposalId) ||
      !nullableIdentifiersEqual(key.revisionIdentity, revisionIdentity)
    ) return Promise.resolve('unavailable')

    if (!signal) return runCoreBatchWithSameActionRetry(key)

    let onAbort: (() => void) | null = null
    const aborted = new Promise<CoreSelectorBatchOutcome>((resolve) => {
      onAbort = () => {
        // Cancel the reads this wait is holding open before reporting, so the
        // abandoned batch stops occupying the transport.
        abortInFlightCoreBatch()
        resolve('aborted')
      }
      signal.addEventListener('abort', onAbort, { once: true })
    })

    return Promise.race([runCoreBatchWithSameActionRetry(key), aborted]).finally(() => {
      if (onAbort) signal.removeEventListener('abort', onAbort)
    })
  }

  /**
   * One explicit wait retries a failed batch once, inside the same action
   * (#2528). A second failure is reported honestly rather than retried again.
   */
  function runCoreBatchWithSameActionRetry(
    key: SelectorKey,
  ): Promise<CoreSelectorBatchOutcome> {
    const promise = ensureCoreBatch(key)
    return promise.then((outcome) => {
      if (
        outcome === 'failed' &&
        activeCoreBatch &&
        selectorKeysEqual(activeCoreBatch.key, key) &&
        activeCoreBatch.promise === promise
      ) {
        activeCoreBatch = null
        return ensureCoreBatch(key)
      }
      return outcome
    })
  }

  watch(
    [
      () => activeProposal.value?.id,
      () => captureSourceReference(activeProposal.value),
      () => proposalRevisionIdentity(activeProposal.value),
    ],
    async (
      [proposalId, captureReference, revisionIdentity],
      previousValues,
    ) => {
      const [previousProposalId, previousCaptureReference, previousRevisionIdentity] =
        previousValues ?? []
      const initialLoad = previousValues === undefined
      const proposalChanged = initialLoad
        ? true
        : previousProposalId == null || proposalId == null
          ? previousProposalId !== proposalId
          : !proposalIdsEqual(previousProposalId, proposalId)
      const captureChanged = initialLoad
        ? true
        : !nullableIdentifiersEqual(previousCaptureReference, captureReference)
      const revisionChanged = initialLoad
        ? true
        : proposalRevisionMoved(previousRevisionIdentity ?? null, revisionIdentity ?? null)

      // Vue also invokes this watcher when a raw revision field changes but its
      // effective identity does not (for example approve pins latest -> null).
      // Keep the current review data in that terminal transition.
      if (!initialLoad && !proposalChanged && !captureChanged && !revisionChanged) return

      if (!proposalId) {
        invalidateCoreBatch()
        settledCoreKey = null
        settledCaptureMetadata = null
        discardCaptureLookup()
        isLoading.value = false
        clearSelectorData()
        return
      }

      const key = selectorKeyForProposal(activeProposal.value)
      if (key) void ensureCoreBatch(key)
    },
    { immediate: true },
  )

  const provenance = computed<ProvenanceRow[]>(() => provenanceData.value)
  const provenanceMetadata = computed<ProvenanceMetadata | null>(
    () => provenanceMetadataData.value,
  )
  const evidenceLinks = computed<EvidenceLink[]>(() => evidenceLinksData.value)
  const sideEffects = computed<SideEffects>(() => sideEffectsData.value ?? emptySideEffects())
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
    // Invalidate the current generation so any in-flight Promise.allSettled
    // continuation short-circuits at the `generation !== fetchGeneration`
    // guard instead of writing reactive state after the scope is gone.
    // (Promise.allSettled resolves even when its inner requests are aborted.)
    invalidateCoreBatch()
    discardCaptureLookup()
  })

  const loading = computed(() => isLoading.value)

  return {
    provenance,
    provenanceMetadata,
    evidenceLinks,
    sideEffects,
    confidenceBreakdown,
    conflicts,
    history,
    similarPast,
    similarPastApplyRate,
    loading,
    waitForCoreBatch,
  }
}
