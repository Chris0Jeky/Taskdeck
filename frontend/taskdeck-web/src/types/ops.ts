export type CommandRunStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'TimedOut' | 'Cancelled'
export type CommandRunStatusValue = CommandRunStatus | number

export interface RunCommandRequest {
  templateName: string
  parameters?: Record<string, string>
}

export interface CommandRun {
  id: string
  templateName: string
  requestedByUserId: string
  status: CommandRunStatusValue
  startedAt: string | null
  completedAt: string | null
  exitCode: number | null
  truncated: boolean
  correlationId: string
  errorMessage: string | null
  outputPreview: string | null
  createdAt: string
}

export interface CommandRunLog {
  id: string
  commandRunId: string
  timestamp: string
  level: string
  source: string
  message: string
  metadata: string | null
}

export interface CommandRunDetail extends CommandRun {
  logs: CommandRunLog[]
}

export interface CommandTemplate {
  name: string
  description: string
  riskClass: string
  timeoutSeconds: number
  requiredRole: string
  acceptedParameters: string[]
}

export interface LogEntry {
  id: string
  timestamp: string
  level: string
  source: string
  eventName: string
  message: string
  correlationId: string | null
  userId: string | null
  boardId: string | null
  metadata: string | null
}

export interface LogQuery extends Record<string, string | number | undefined> {
  level?: string
  source?: string
  userId?: string
  boardId?: string
  correlationId?: string
  from?: string
  to?: string
  limit?: number
}
