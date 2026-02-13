export type ProposalSourceType = 'Queue' | 'Chat' | 'Manual'
export type ProposalSourceTypeValue = ProposalSourceType | number
export type ProposalStatus = 'PendingReview' | 'Approved' | 'Rejected' | 'Applied' | 'Failed' | 'Expired'
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
}

export interface ProposalFilters {
  status?: ProposalStatus
  boardId?: string
  userId?: string
  riskLevel?: ProposalRiskLevel
  limit?: number
}
