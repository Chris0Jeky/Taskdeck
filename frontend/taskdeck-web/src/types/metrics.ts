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

export interface ConfidenceBand {
  lowEstimate: string | null
  expectedEstimate: string
  highEstimate: string | null
  lowThroughputPerDay: number
  expectedThroughputPerDay: number
  highThroughputPerDay: number
}

export interface BoardForecastResponse {
  boardId: string
  remainingCards: number
  completedCards: number
  averageThroughputPerDay: number
  throughputStdDev: number
  averageCycleTimeDays: number
  estimatedCompletionDate: string | null
  confidenceBand: ConfidenceBand | null
  dataPointCount: number
  historyDaysUsed: number
  assumptions: string[]
  caveats: string[]
}

export interface ForecastQuery {
  boardId: string
  historyDays?: number
}
