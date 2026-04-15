export type ConnectorType =
  | 'BrowserClipper'
  | 'MarkdownImport'
  | 'WebClip'
  | 'GitHubIssueIntake'
  | 'WebhookInbound'
  | 'Custom'

export type ConnectorDirection = 'Inbound' | 'Context' | 'Outbound'

export type ConnectorStatus = 'Active' | 'Disabled' | 'Error'

export type ConnectorEventType = 'Connected' | 'Disconnected' | 'DataReceived' | 'Error'

export interface IntegrationConnector {
  id: string
  name: string
  connectorType: ConnectorType
  direction: ConnectorDirection
  status: ConnectorStatus
  configuration: string | null
  createdAt: string
  updatedAt: string
}

export interface ConnectorEvent {
  id: string
  eventType: ConnectorEventType
  payload: string | null
  createdAt: string
}

export interface IntegrationConnectorDetail extends IntegrationConnector {
  recentEvents: ConnectorEvent[]
}

export interface CreateIntegrationConnectorRequest {
  name: string
  connectorType: number
  direction: number
  configuration?: string | null
}

export interface UpdateIntegrationConnectorRequest {
  name?: string | null
  configuration?: string | null
}

// Enum value maps for API communication (backend uses int enums)
export const ConnectorTypeValues: Record<ConnectorType, number> = {
  BrowserClipper: 0,
  MarkdownImport: 1,
  WebClip: 2,
  GitHubIssueIntake: 3,
  WebhookInbound: 4,
  Custom: 5,
}

export const ConnectorDirectionValues: Record<ConnectorDirection, number> = {
  Inbound: 0,
  Context: 1,
  Outbound: 2,
}

export const ConnectorTypeLabels: Record<ConnectorType, string> = {
  BrowserClipper: 'Browser Clipper',
  MarkdownImport: 'Markdown Import',
  WebClip: 'Web Clip',
  GitHubIssueIntake: 'GitHub Issue Intake',
  WebhookInbound: 'Webhook Inbound',
  Custom: 'Custom',
}

export const ConnectorDirectionLabels: Record<ConnectorDirection, string> = {
  Inbound: 'Inbound (Captures)',
  Context: 'Context (Knowledge)',
  Outbound: 'Outbound (Events)',
}
