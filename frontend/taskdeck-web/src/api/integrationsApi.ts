import http from './http'
import {
  normalizeConnectorType,
  normalizeConnectorDirection,
  normalizeConnectorStatus,
  normalizeConnectorEventType,
} from '../types/integration'
import type {
  IntegrationConnector,
  IntegrationConnectorDetail,
  ConnectorEvent,
  CreateIntegrationConnectorRequest,
  UpdateIntegrationConnectorRequest,
  ConnectorTypeValue,
  ConnectorDirectionValue,
  ConnectorStatusValue,
  ConnectorEventTypeValue,
} from '../types/integration'

/** Raw connector shape from backend (enums may be numeric) */
interface RawIntegrationConnector
  extends Omit<IntegrationConnector, 'connectorType' | 'direction' | 'status'> {
  connectorType: ConnectorTypeValue
  direction: ConnectorDirectionValue
  status: ConnectorStatusValue
}

/** Raw event shape from backend (enums may be numeric) */
interface RawConnectorEvent extends Omit<ConnectorEvent, 'eventType'> {
  eventType: ConnectorEventTypeValue
}

/** Raw detail shape from backend (enums may be numeric) */
interface RawIntegrationConnectorDetail
  extends Omit<IntegrationConnectorDetail, 'connectorType' | 'direction' | 'status' | 'recentEvents'> {
  connectorType: ConnectorTypeValue
  direction: ConnectorDirectionValue
  status: ConnectorStatusValue
  recentEvents: RawConnectorEvent[]
}

function normalizeConnector(raw: RawIntegrationConnector): IntegrationConnector {
  return {
    ...raw,
    connectorType: normalizeConnectorType(raw.connectorType),
    direction: normalizeConnectorDirection(raw.direction),
    status: normalizeConnectorStatus(raw.status),
  }
}

function normalizeEvent(raw: RawConnectorEvent): ConnectorEvent {
  return {
    ...raw,
    eventType: normalizeConnectorEventType(raw.eventType),
  }
}

function normalizeConnectorDetail(
  raw: RawIntegrationConnectorDetail,
): IntegrationConnectorDetail {
  return {
    ...raw,
    connectorType: normalizeConnectorType(raw.connectorType),
    direction: normalizeConnectorDirection(raw.direction),
    status: normalizeConnectorStatus(raw.status),
    recentEvents: raw.recentEvents.map(normalizeEvent),
  }
}

export const integrationsApi = {
  async listConnectors(): Promise<IntegrationConnector[]> {
    const { data } = await http.get<RawIntegrationConnector[]>('/integrations')
    return data.map(normalizeConnector)
  },

  async getConnector(id: string): Promise<IntegrationConnectorDetail> {
    const { data } = await http.get<RawIntegrationConnectorDetail>(
      `/integrations/${encodeURIComponent(id)}`,
    )
    return normalizeConnectorDetail(data)
  },

  async registerConnector(
    request: CreateIntegrationConnectorRequest,
  ): Promise<IntegrationConnector> {
    const { data } = await http.post<RawIntegrationConnector>('/integrations', request)
    return normalizeConnector(data)
  },

  async updateConnector(
    id: string,
    request: UpdateIntegrationConnectorRequest,
  ): Promise<IntegrationConnector> {
    const { data } = await http.put<RawIntegrationConnector>(
      `/integrations/${encodeURIComponent(id)}`,
      request,
    )
    return normalizeConnector(data)
  },

  async deleteConnector(id: string): Promise<void> {
    await http.delete(`/integrations/${encodeURIComponent(id)}`)
  },

  async enableConnector(id: string): Promise<IntegrationConnector> {
    const { data } = await http.post<RawIntegrationConnector>(
      `/integrations/${encodeURIComponent(id)}/enable`,
    )
    return normalizeConnector(data)
  },

  async disableConnector(id: string): Promise<IntegrationConnector> {
    const { data } = await http.post<RawIntegrationConnector>(
      `/integrations/${encodeURIComponent(id)}/disable`,
    )
    return normalizeConnector(data)
  },
}
