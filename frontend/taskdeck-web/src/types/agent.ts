export type AgentScopeType = 'Workspace' | 'Board'

/** Raw value from backend -- may be a numeric enum index or a string */
export type AgentScopeTypeValue = AgentScopeType | number

export type AgentRunStatus =
  | 'Queued'
  | 'GatheringContext'
  | 'Planning'
  | 'ProposalCreated'
  | 'WaitingForReview'
  | 'Applying'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'

/** Raw value from backend -- may be a numeric enum index or a string */
export type AgentRunStatusValue = AgentRunStatus | number

const scopeTypeByIndex: readonly AgentScopeType[] = ['Workspace', 'Board'] as const

const runStatusByIndex: readonly AgentRunStatus[] = [
  'Queued',
  'GatheringContext',
  'Planning',
  'ProposalCreated',
  'WaitingForReview',
  'Applying',
  'Completed',
  'Failed',
  'Cancelled',
] as const

/** Normalize a scope type from the backend (may arrive as number or string) */
export function normalizeScopeType(value: AgentScopeTypeValue): AgentScopeType {
  if (typeof value === 'number') {
    return scopeTypeByIndex[value] ?? 'Workspace'
  }
  const found = scopeTypeByIndex.find((v) => v.toLowerCase() === value.toLowerCase())
  return found ?? 'Workspace'
}

/** Normalize a run status from the backend (may arrive as number or string) */
export function normalizeRunStatus(value: AgentRunStatusValue): AgentRunStatus {
  if (typeof value === 'number') {
    return runStatusByIndex[value] ?? 'Queued'
  }
  const found = runStatusByIndex.find((v) => v.toLowerCase() === value.toLowerCase())
  return found ?? 'Queued'
}

export interface AgentProfile {
  id: string
  userId: string
  name: string
  description: string
  templateKey: string
  scopeType: AgentScopeType
  scopeBoardId: string | null
  policyJson: string
  isEnabled: boolean
  createdAt: string
  updatedAt: string
}

export interface AgentRun {
  id: string
  agentProfileId: string
  userId: string
  boardId: string | null
  triggerType: string
  objective: string
  status: AgentRunStatus
  summary: string | null
  failureReason: string | null
  proposalId: string | null
  stepsExecuted: number
  tokensUsed: number
  approxCostUsd: number | null
  startedAt: string
  completedAt: string | null
  createdAt: string
  updatedAt: string
}

export interface AgentRunEvent {
  id: string
  runId: string
  sequenceNumber: number
  eventType: string
  payload: string
  timestamp: string
}

export interface AgentRunDetail extends AgentRun {
  events: AgentRunEvent[]
}

/** Human-readable labels for run statuses */
export const runStatusLabels: Record<AgentRunStatus, string> = {
  Queued: 'Queued',
  GatheringContext: 'Gathering context',
  Planning: 'Planning',
  ProposalCreated: 'Proposal created',
  WaitingForReview: 'Waiting for review',
  Applying: 'Applying changes',
  Completed: 'Completed',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
}

/** CSS modifier suffix for status badges */
export const runStatusVariant: Record<AgentRunStatus, string> = {
  Queued: 'neutral',
  GatheringContext: 'info',
  Planning: 'info',
  ProposalCreated: 'warning',
  WaitingForReview: 'warning',
  Applying: 'info',
  Completed: 'success',
  Failed: 'error',
  Cancelled: 'neutral',
}

/** Whether a status is terminal (no further transitions expected) */
export function isTerminalStatus(status: AgentRunStatus): boolean {
  return status === 'Completed' || status === 'Failed' || status === 'Cancelled'
}
