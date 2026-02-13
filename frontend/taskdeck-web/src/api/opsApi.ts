import http from './http'
import type {
  CommandRun,
  CommandRunDetail,
  CommandRunLog,
  CommandTemplate,
  LogEntry,
  LogQuery,
  RunCommandRequest,
} from '../types/ops'

function toQuery(query?: LogQuery): string {
  if (!query) {
    return ''
  }

  const params = new URLSearchParams()
  if (query.level) params.set('level', query.level)
  if (query.source) params.set('source', query.source)
  if (query.userId) params.set('userId', query.userId)
  if (query.boardId) params.set('boardId', query.boardId)
  if (query.correlationId) params.set('correlationId', query.correlationId)
  if (query.from) params.set('from', query.from)
  if (query.to) params.set('to', query.to)
  if (query.limit !== undefined) params.set('limit', String(query.limit))

  const asText = params.toString()
  return asText.length > 0 ? `?${asText}` : ''
}

export const opsApi = {
  async getTemplates(): Promise<CommandTemplate[]> {
    const { data } = await http.get<CommandTemplate[]>('/ops/cli/templates')
    return data
  },

  async runCommand(request: RunCommandRequest): Promise<CommandRun> {
    const { data } = await http.post<CommandRun>('/ops/cli/run', request)
    return data
  },

  async getRun(runId: string): Promise<CommandRunDetail> {
    const { data } = await http.get<CommandRunDetail>(`/ops/cli/runs/${encodeURIComponent(runId)}`)
    return data
  },

  async getRunLogs(runId: string): Promise<CommandRunLog[]> {
    const { data } = await http.get<CommandRunLog[]>(`/ops/cli/runs/${encodeURIComponent(runId)}/logs`)
    return data
  },

  async queryLogs(query?: LogQuery): Promise<LogEntry[]> {
    const { data } = await http.get<LogEntry[]>(`/logs${toQuery(query)}`)
    return data
  },

  async getCorrelationLogs(correlationId: string): Promise<LogEntry[]> {
    const { data } = await http.get<LogEntry[]>(`/logs/correlation/${encodeURIComponent(correlationId)}`)
    return data
  },
}
