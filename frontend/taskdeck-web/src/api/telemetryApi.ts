import axios from 'axios'

const API_BASE = import.meta.env.VITE_API_BASE_URL || ''

export interface SentryClientConfig {
  enabled: boolean
  dsn: string
  environment: string
  tracesSampleRate: number
}

export interface AnalyticsClientConfig {
  enabled: boolean
  provider: string
  scriptUrl: string
  siteId: string
}

export interface TelemetryClientConfig {
  enabled: boolean
}

export interface ClientTelemetryConfig {
  sentry: SentryClientConfig
  analytics: AnalyticsClientConfig
  telemetry: TelemetryClientConfig
}

export interface TelemetryEventPayload {
  event: string
  timestamp: string
  sessionId: string
  workspaceMode: string
  appVersion: string
  platform: string
  properties?: Record<string, unknown>
}

export interface TelemetryBatchResponse {
  recorded: number
  message?: string
}

export const telemetryApi = {
  async getConfig(): Promise<ClientTelemetryConfig> {
    const response = await axios.get<ClientTelemetryConfig>(`${API_BASE}/api/telemetry/config`)
    return response.data
  },

  async sendEvents(events: TelemetryEventPayload[]): Promise<TelemetryBatchResponse> {
    const response = await axios.post<TelemetryBatchResponse>(`${API_BASE}/api/telemetry/events`, { events })
    return response.data
  },
}
