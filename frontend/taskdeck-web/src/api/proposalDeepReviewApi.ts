import http from './http'

/**
 * Opaque evidence-link metadata attached to a provenance row. `sourceType` is
 * `'Transcript'` when `sourceId` is a transcript id readable through
 * `transcriptsApi`; span bounds are character offsets into that transcript's
 * LF-normalized text.
 */
export interface ProvenanceEvidenceLinkDto {
  sourceType: string
  sourceId: string
  label: string | null
  spanStart: number | null
  spanEnd: number | null
  /**
   * Server-computed from the caller's claims: true only when this caller can actually open
   * the source through its read endpoint. Provenance is board-authorized while transcript
   * read stays owner-only, so a board collaborator gets `false` here and must not be offered
   * a deep link that can only 404. Absent on responses predating the flag — treat as false.
   */
  viewable?: boolean
}

export interface ProposalProvenanceMetadataDto {
  /** Server-recorded producer, or null when nothing was recorded. */
  provider: string | null
  /** Server-recorded model identifier; null whenever `provider` is null. */
  model: string | null
  /** Server-recorded prompt contract version, or null. */
  promptVersion: string | null
}

export interface ProvenanceRowDto {
  icon: string
  key: string
  value: string
  weight: string
  /** Absent on responses predating the typed evidence-link contract. */
  evidenceLinks?: ProvenanceEvidenceLinkDto[] | null
}

export interface ConfidenceComponentDto {
  key: string
  value: number
}

export interface ConfidenceBreakdownDto {
  overall: number | null
  components: ConfidenceComponentDto[]
  note: string | null
  /** Nullable compatibility field; confidence never controls apply eligibility. */
  threshold: null
  /** Nullable compatibility field; confidence never controls apply eligibility. */
  meetsThreshold: null
  source: 'model-reported' | 'deterministic' | 'derived' | 'not-reported'
}

export interface SideEffectRowDto {
  key: string
  value: string
  tone: 'active' | 'passive'
}

export interface ReversibilityDto {
  /** Apply-risk summary exposed through the endpoint's stable historical field name. */
  summary: string
  /** Manual-recovery/impact guidance; does not promise an undo capability. */
  description: string
  /** Legacy review-attention metadata retained for compatibility. */
  windowMs: number
}

export interface ProposalSideEffectsDto {
  rows: SideEffectRowDto[]
  reversibility: ReversibilityDto
}

/**
 * Numeric System.Text.Json enum values emitted by the deep-review API.
 * Keep these ordinals aligned with the backend enums; API contract coverage
 * pins the serialized values so an enum reorder cannot silently break Paper.
 */
export const conflictToneWireValues = {
  Warn: 0,
  Info: 1,
  Ok: 2,
} as const

export type ConflictToneWireValue =
  (typeof conflictToneWireValues)[keyof typeof conflictToneWireValues]

export const cardHistoryStatusWireValues = {
  Pending: 0,
  Applied: 1,
  Past: 2,
} as const

export type CardHistoryStatusWireValue =
  (typeof cardHistoryStatusWireValues)[keyof typeof cardHistoryStatusWireValues]

export interface ConflictRowDto {
  tone: ConflictToneWireValue
  key: string
  value: string
}

export interface CardHistoryRowDto {
  serial: string
  event: string
  age: string
  status: CardHistoryStatusWireValue
}

export interface SimilarPastDecisionDto {
  serial: string
  title: string
  verdict: string
  date: string
}

export interface SimilarPastResultDto {
  decisions: SimilarPastDecisionDto[]
  applyRate: number
}

export interface RequestOptions {
  signal?: AbortSignal
}

function encodedId(id: string): string {
  return encodeURIComponent(id)
}

/** The all-null payload: no producer was recorded, so no claim may be rendered. */
const UNRECORDED_PROVENANCE_METADATA: ProposalProvenanceMetadataDto = {
  provider: null,
  model: null,
  promptVersion: null,
}

export const proposalDeepReviewApi = {
  async getProvenance(proposalId: string, options?: RequestOptions): Promise<ProvenanceRowDto[]> {
    const { data } = await http.get<ProvenanceRowDto[]>(
      `/automation/proposals/${encodedId(proposalId)}/provenance`,
      { signal: options?.signal },
    )
    return data
  },

  /**
   * Server-recorded producer metadata for a proposal (#1987). Board-authorized and
   * proposal-scoped, so a collaborator reviewing another owner's proposal reaches it without
   * capture-detail access.
   *
   * A proposal with nothing recorded answers 200 with all-null fields. An authorization or
   * not-found answer is normalized to that same all-null shape rather than thrown: both render
   * as no producer claim, which is the honest outcome either way. Telling "lookup failed" apart
   * from "genuinely not recorded" is deliberately out of scope here and tracked by #2315.
   * Every other failure (network, 5xx, abort) still rejects.
   */
  async getProvenanceMetadata(
    proposalId: string,
    options?: RequestOptions,
  ): Promise<ProposalProvenanceMetadataDto> {
    try {
      const { data } = await http.get<ProposalProvenanceMetadataDto>(
        `/automation/proposals/${encodedId(proposalId)}/provenance/metadata`,
        { signal: options?.signal, expectedStatuses: [403, 404] },
      )
      return data
    } catch (e: unknown) {
      const status = (e as { response?: { status?: number } } | null)?.response?.status
      if (status === 403 || status === 404) return UNRECORDED_PROVENANCE_METADATA
      throw e
    }
  },

  async getConfidence(proposalId: string, options?: RequestOptions): Promise<ConfidenceBreakdownDto> {
    const { data } = await http.get<ConfidenceBreakdownDto>(
      `/automation/proposals/${encodedId(proposalId)}/confidence`,
      { signal: options?.signal },
    )
    return data
  },

  async getSideEffects(proposalId: string, options?: RequestOptions): Promise<ProposalSideEffectsDto> {
    const { data } = await http.get<ProposalSideEffectsDto>(
      `/automation/proposals/${encodedId(proposalId)}/side-effects`,
      { signal: options?.signal },
    )
    return data
  },

  async getConflicts(proposalId: string, options?: RequestOptions): Promise<ConflictRowDto[]> {
    const { data } = await http.get<ConflictRowDto[]>(
      `/automation/proposals/${encodedId(proposalId)}/conflicts`,
      { signal: options?.signal },
    )
    return data
  },

  async getHistory(proposalId: string, options?: RequestOptions): Promise<CardHistoryRowDto[]> {
    const { data } = await http.get<CardHistoryRowDto[]>(
      `/automation/proposals/${encodedId(proposalId)}/history`,
      { signal: options?.signal },
    )
    return data
  },

  async getSimilarPast(proposalId: string, options?: RequestOptions): Promise<SimilarPastResultDto> {
    const { data } = await http.get<SimilarPastResultDto>(
      `/automation/proposals/${encodedId(proposalId)}/similar-past`,
      { signal: options?.signal },
    )
    return data
  },
}
