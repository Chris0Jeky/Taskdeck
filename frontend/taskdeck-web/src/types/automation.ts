export type ProposalSourceType = 'Queue' | 'Chat' | 'Manual'
export type ProposalSourceTypeValue = ProposalSourceType | number
export type ProposalStatus = 'PendingReview' | 'Approved' | 'Rejected' | 'Applied' | 'Failed' | 'Expired' | 'Dismissed'
export type ProposalStatusValue = ProposalStatus | number
export type ProposalRiskLevel = 'Low' | 'Medium' | 'High' | 'Critical'
export type ProposalRiskLevelValue = ProposalRiskLevel | number

export interface ProposalOperation {
  id: string
  proposalId: string
  sequence: number
  actionType: string
  targetType: string
  targetId: string | null
  parameters: string
  idempotencyKey: string
  expectedVersion: string | null
}

export interface ProposalAffectedEntity {
  entityType: string
  entityId: string | null
  label: string
  changeCount: number
}

export interface ProposalPresentation {
  plainSummary: string
  impactSummary: string
  riskCue: string
  sourceCue: string
  operationHeadlines: string[]
  affectedEntities: ProposalAffectedEntity[]
}

export interface Proposal {
  id: string
  sourceType: ProposalSourceTypeValue
  sourceReferenceId: string | null
  boardId: string | null
  requestedByUserId: string
  status: ProposalStatusValue
  riskLevel: ProposalRiskLevelValue
  summary: string
  diffPreview: string | null
  validationIssues: string | null
  createdAt: string
  updatedAt: string
  expiresAt: string
  decidedAt: string | null
  decidedByUserId: string | null
  appliedAt: string | null
  failureReason: string | null
  correlationId: string
  operations: ProposalOperation[]
  presentation?: ProposalPresentation
  /** True when the proposal's expiry time has passed (server-authoritative). */
  isExpired?: boolean
  /** When set and in the future, the proposal is snoozed (deferred) until this UTC instant. */
  deferredUntil?: string | null
  /**
   * The revision pinned at approve time, which Apply executes exactly (`#1428`). Null means the
   * proposal was approved from its original operations. Mirrors
   * `Taskdeck.Application/DTOs/AutomationProposalDtos.cs` `ProposalDto.ApprovedRevisionId`, which the
   * backend documents as being on the DTO "so clients can detect a pinned revision" — every read and
   * decide response carries it, including list items (`#1444`).
   *
   * Contract exposure only: nothing in the UI may assert anything about pinning on the strength of
   * this field without a separate design decision (`#1298` is the standing precedent against the
   * review surface advertising semantics it cannot back).
   */
  approvedRevisionId?: string | null
}

export interface ProposalFilters {
  status?: ProposalStatus
  boardId?: string
  userId?: string
  riskLevel?: ProposalRiskLevel
  limit?: number
}
