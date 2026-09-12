export interface EstimateTotals {
  cardCount: number
  knownEstimateMinutes: number
  missingEstimateCount: number
}

export interface BoardEstimateRollup {
  boardId: string
  generatedAt: string
  board: EstimateTotals
  columns: { columnId: string; name: string; totals: EstimateTotals }[]
  participants: { userId: string; username: string; totals: EstimateTotals }[]
  unassigned: EstimateTotals
}
