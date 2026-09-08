export const insightStates = ['available', 'dismissed', 'snoozed', 'resolved', 'muted'] as const

export type InsightState = (typeof insightStates)[number]

export const insightActions = ['dismiss', 'snooze', 'reopen', 'mute'] as const

export type InsightAction = (typeof insightActions)[number]

export const memoryStatuses = ['statement', 'assumption', 'unknown', 'needsReview'] as const

export type MemoryStatus = (typeof memoryStatuses)[number]

export interface Insight {
  id: string
  boardId: string
  cardId: string | null
  memoryId: string | null
  rule: string
  title: string
  detail: string
  state: InsightState
  /** Exact structural evidence shown to the user and echoed when answering. */
  evidence: string
  checkedAt: string
  snoozeUntil: string | null
}

export interface MemoryHistoryEntry {
  text: string
  title: string
  status: MemoryStatus
  revision: number
  recordedAt: string
}

export interface Memory {
  id: string
  boardId: string
  title: string
  text: string
  originalText: string
  originalEvidence: string | null
  status: MemoryStatus
  archived: boolean
  revision: number
  createdAt: string
  history: MemoryHistoryEntry[]
}

export interface AnalyzeInsightsRequest {
  boardId: string
}

export interface AnswerInsightRequest {
  text: string
  status: MemoryStatus
  evidence: string
}

export interface CreateMemoryRequest {
  boardId: string
  title: string
  text: string
  status: MemoryStatus
}

export interface UpdateMemoryRequest {
  title: string
  text: string
  status: MemoryStatus
  revision: number
}

export interface ArchiveMemoryRequest {
  archived: boolean
  revision: number
}
