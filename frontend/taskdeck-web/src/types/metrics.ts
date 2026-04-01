export interface ThroughputDataPoint {
  date: string
  completedCount: number
}

export interface CycleTimeEntry {
  cardId: string
  cardTitle: string
  cycleTimeDays: number
}

export interface WipSnapshot {
  columnId: string
  columnName: string
  cardCount: number
  wipLimit: number | null
}

export interface BlockedCardSummary {
  cardId: string
  cardTitle: string
  blockReason: string | null
  blockedDurationDays: number
}

export interface BoardMetricsResponse {
  boardId: string
  from: string
  to: string
  throughput: ThroughputDataPoint[]
  averageCycleTimeDays: number
  cycleTimeEntries: CycleTimeEntry[]
  wipSnapshots: WipSnapshot[]
  totalWip: number
  blockedCount: number
  blockedCards: BlockedCardSummary[]
}

export interface MetricsQuery {
  boardId: string
  from?: string
  to?: string
  labelId?: string
}
