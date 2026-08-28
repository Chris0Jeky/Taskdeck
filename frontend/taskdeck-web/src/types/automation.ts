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
   * The revision pinned at approve time, which Apply executes exactly (`#1428`). Mirrors
   * `ProposalDto.ApprovedRevisionId` (`Taskdeck.Application/DTOs/AutomationProposalDtos.cs`).
   *
   * **Null does NOT mean "approved from the originals".** `AutomationProposal.ApprovedRevisionId` is
   * written only by `Approve`, so null covers *approved from the original operations* AND *not yet
   * decided* AND rejected/expired/dismissed — i.e. most proposals in a queue. Reading null as an
   * approval is the specific mistake this note exists to prevent; a non-null value is the only
   * positive signal.
   *
   * Present on every REST proposal payload — list, single read, and every decide response — because
   * they all map through `MapToDto`, and true since `#1439` (not `#1444`, which changed which
   * *operations* list reads return, not whether the pin is carried). Scope: the REST `ProposalDto`
   * only. The MCP `proposal_detail` resource projects its own object that omits this field entirely,
   * so an MCP-facing surface cannot rely on it.
   *
   * Declared required (not `?:`) deliberately, unlike its optional neighbours above: `MapToDto`
   * always assigns it, and the API serializes with default options (bare `AddControllers()` in
   * `Program.cs`, no `DefaultIgnoreCondition`), so an unpinned proposal arrives as an explicit
   * `null`, never an absent key. `?:` would model a wire-level omission that does not occur, and
   * would let a field-by-field rebuild drop the pin without a compile error.
   *
   * Contract exposure only: nothing in the UI may assert anything about pinning on the strength of
   * this field without a separate design decision (`#1298` is the standing precedent against the
   * review surface advertising semantics it cannot back).
   */
  approvedRevisionId: string | null
  /** Latest effective revision while PendingReview; null for original content or decided rows. */
  latestRevisionId: string | null
}

/**
 * The pinned-revision id as the wire carries it. Exported deliberately, and referenced from
 * production source rather than a spec, so that `Proposal.approvedRevisionId` survives a dead-code
 * sweep: the field still has no consumer (`#1298` forbids a UI claim without a design decision), so
 * deleting the interface member is otherwise a silent no-op. Deleting it makes this alias fail to
 * compile.
 *
 * Since `#1468` the spec tree is type-checked too (`tsconfig.vitest.json`), and
 * `src/tests/api/automationApi.spec.ts` carries an `expectTypeOf` pin on the same member. This alias
 * is kept as the belt to that braces: it holds from inside production source, so it survives even if
 * that spec were quarantined, and it is what a dead-code sweep of `src/` actually sees.
 */
export type ProposalApprovedRevisionId = Proposal['approvedRevisionId']

export interface ProposalFilters {
  status?: ProposalStatus
  boardId?: string
  userId?: string
  riskLevel?: ProposalRiskLevel
  limit?: number
}

/** Receipt from the all-or-none approve-only batch endpoint. */
export interface BatchApproveProposalsResult {
  approvedIds: string[]
}

/** Exact reviewer-selected snapshot submitted to the all-or-none batch endpoint. */
export interface BatchApproveProposalSelection {
  id: string
  expectedProposalUpdatedAt: string
  expectedLatestRevisionId: string | null
}
