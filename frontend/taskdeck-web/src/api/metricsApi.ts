import http from './http'
import type { BoardMetricsResponse, MetricsQuery } from '../types/metrics'

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

  async exportBoardMetricsCsv(query: MetricsQuery): Promise<void> {
    const params = new URLSearchParams()
    if (query.from) params.append('from', query.from)
    if (query.to) params.append('to', query.to)
    if (query.labelId) params.append('labelId', query.labelId)

    const qs = params.toString()
    const url = `/metrics/boards/${encodeURIComponent(query.boardId)}/export${qs ? `?${qs}` : ''}`
    const response = await http.get(url, { responseType: 'blob' })

    // Extract filename from Content-Disposition header or use default
    const disposition = response.headers['content-disposition'] as string | undefined
    let filename = 'board-metrics.csv'
    if (disposition) {
      const match = disposition.match(/filename[^;=\n]*=["']?([^"';\n]*)["']?/)
      if (match?.[1]) filename = match[1]
    }

    // Trigger browser download
    const blob = new Blob([response.data as BlobPart], { type: 'text/csv' })
    const link = document.createElement('a')
    link.href = URL.createObjectURL(blob)
    link.download = filename
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    URL.revokeObjectURL(link.href)
  },
}
