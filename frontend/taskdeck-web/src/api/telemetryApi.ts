import http from './http'

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

// Keep background requests shorter than the 30-second flush interval. The store
// owns consent-aware retries; interceptor retries must not extend this deadline.
const REQUEST_OPTIONS = { timeout: 10_000, skipRetry: true }

export const telemetryApi = {
  async getConfig(): Promise<ClientTelemetryConfig> {
    const { data } = await http.get<ClientTelemetryConfig>('/telemetry/config', REQUEST_OPTIONS)
    return data
  },

  async sendEvents(events: TelemetryEventPayload[]): Promise<TelemetryBatchResponse> {
    const { data } = await http.post<TelemetryBatchResponse>('/telemetry/events', { events }, REQUEST_OPTIONS)
    return data
  },
}
