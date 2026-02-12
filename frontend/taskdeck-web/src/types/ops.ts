export type LogLevel = 'info' | 'warning' | 'error'
export type LogSource = 'frontend' | 'api' | 'cli-bridge' | 'queue' | 'automation'
export type CommandRunStatus = 'pending' | 'running' | 'completed' | 'failed'

export interface CommandRun {
  runId: string
  commandKey: string
  args: string[]
  requestedBy: string
  startedAt: string
  endedAt: string | null
  status: CommandRunStatus
  stdout: string | null
  stderr: string | null
  exitCode: number | null
  correlationId: string
}

export interface CommandTemplate {
  key: string
  label: string
  domain: string
  description: string
  parameters: CommandParameter[]
}

export interface CommandParameter {
  name: string
  type: 'string' | 'number' | 'boolean'
  required: boolean
  description: string
}

export interface LogEntry {
  id: string
  timestamp: string
  level: LogLevel
  source: LogSource
  correlationId: string | null
  actorId: string | null
  entityType: string | null
  entityId: string | null
  message: string
  payload: Record<string, unknown> | null
}

export interface LogQuery {
  level?: LogLevel
  source?: LogSource
  correlationId?: string
  startTime?: string
  endTime?: string
  limit?: number
  offset?: number
}
