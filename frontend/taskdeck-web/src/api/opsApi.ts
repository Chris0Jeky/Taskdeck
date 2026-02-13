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
import { buildQueryString } from '../utils/queryBuilder'

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
    const { data } = await http.get<LogEntry[]>(`/logs${buildQueryString(query)}`)
    return data
  },

  async getCorrelationLogs(correlationId: string): Promise<LogEntry[]> {
    const { data } = await http.get<LogEntry[]>(`/logs/correlation/${encodeURIComponent(correlationId)}`)
    return data
  },
}
