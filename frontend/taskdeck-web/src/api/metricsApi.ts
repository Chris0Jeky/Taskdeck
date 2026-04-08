import http from './http'
import type { BoardMetricsResponse, BoardForecastResponse, MetricsQuery, ForecastQuery } from '../types/metrics'

export const metricsApi = {
  async getBoardMetrics(query: MetricsQuery): Promise<BoardMetricsResponse> {
    const params = new URLSearchParams()
    if (query.from) params.append('from', query.from)
    if (query.to) params.append('to', query.to)
    if (query.labelId) params.append('labelId', query.labelId)

    const qs = params.toString()
    const url = `/metrics/boards/${encodeURIComponent(query.boardId)}${qs ? `?${qs}` : ''}`
    const { data } = await http.get<BoardMetricsResponse>(url)
    return data
  },

  async getBoardForecast(query: ForecastQuery): Promise<BoardForecastResponse> {
    const params = new URLSearchParams()
    if (query.historyDays != null) params.append('historyDays', String(query.historyDays))

    const qs = params.toString()
    const url = `/forecast/board/${encodeURIComponent(query.boardId)}${qs ? `?${qs}` : ''}`
    const { data } = await http.get<BoardForecastResponse>(url)
    return data
  },
}
