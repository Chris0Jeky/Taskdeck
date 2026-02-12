export type QueueStatus = 'Pending' | 'Processing' | 'Completed' | 'Failed' | 'Cancelled'
export type ProposalStatus = 'pending-review' | 'approved' | 'rejected' | 'applied' | 'failed'
export type ProposalOrigin = 'manual' | 'voice' | 'transcript' | 'agent'
export type RiskLevel = 'low' | 'medium' | 'high' | 'critical'

export interface QueueRequest {
  id: string
  userId: string
  requestType: string
  payload: string
  status: QueueStatus
  result: string | null
  errorMessage: string | null
  createdAt: string
  processedAt: string | null
}

export interface CreateQueueRequestDto {
  requestType: string
  payload: string
}

export interface QueueStats {
  pending: number
  processing: number
  completed: number
  failed: number
  cancelled: number
  total: number
}

export interface AutomationProposal {
  id: string
  origin: ProposalOrigin
  boardId: string
  requestedBy: string
  status: ProposalStatus
  intents: ProposalMutationIntent[]
  diffSummary: string | null
  riskLevel: RiskLevel
  rejectionReason: string | null
  createdAt: string
  updatedAt: string
}

export interface ProposalMutationIntent {
  entityType: string
  entityId: string | null
  operation: 'create' | 'update' | 'move' | 'delete' | 'archive' | 'restore'
  fields: Record<string, { before: unknown; after: unknown }>
  riskTags: string[]
}

export interface ProposalDiff {
  proposalId: string
  intents: ProposalMutationIntent[]
  summary: string
}

export interface ProposalDecision {
  action: 'approve' | 'reject' | 'edit'
  reason?: string
  editedIntents?: ProposalMutationIntent[]
}
