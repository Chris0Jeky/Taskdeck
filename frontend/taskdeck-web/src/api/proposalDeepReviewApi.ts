import http from './http'

export interface ProvenanceRowDto {
  icon: string
  key: string
  value: string
  weight: string
}

export interface ConfidenceComponentDto {
  key: string
  value: number
}

export interface ConfidenceBreakdownDto {
  overall: number
  components: ConfidenceComponentDto[]
  note: string | null
  threshold: number
  meetsThreshold: boolean
}

export interface SideEffectRowDto {
  key: string
  value: string
  tone: 'active' | 'passive'
}

export interface ReversibilityDto {
  summary: string
  description: string
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

export const proposalDeepReviewApi = {
  async getProvenance(proposalId: string, options?: RequestOptions): Promise<ProvenanceRowDto[]> {
    const { data } = await http.get<ProvenanceRowDto[]>(
      `/automation/proposals/${encodedId(proposalId)}/provenance`,
      { signal: options?.signal },
    )
    return data
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
