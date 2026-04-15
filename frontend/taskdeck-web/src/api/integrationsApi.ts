import http from './http'
import type {
  IntegrationConnector,
  IntegrationConnectorDetail,
  CreateIntegrationConnectorRequest,
  UpdateIntegrationConnectorRequest,
} from '../types/integration'

export const integrationsApi = {
  async listConnectors(): Promise<IntegrationConnector[]> {
    const { data } = await http.get<IntegrationConnector[]>('/integrations')
    return data
  },

  async getConnector(id: string): Promise<IntegrationConnectorDetail> {
    const { data } = await http.get<IntegrationConnectorDetail>(
      `/integrations/${encodeURIComponent(id)}`,
    )
    return data
  },

  async registerConnector(
    request: CreateIntegrationConnectorRequest,
  ): Promise<IntegrationConnector> {
    const { data } = await http.post<IntegrationConnector>('/integrations', request)
    return data
  },

  async updateConnector(
    id: string,
    request: UpdateIntegrationConnectorRequest,
  ): Promise<IntegrationConnector> {
    const { data } = await http.put<IntegrationConnector>(
      `/integrations/${encodeURIComponent(id)}`,
      request,
    )
    return data
  },

  async deleteConnector(id: string): Promise<void> {
    await http.delete(`/integrations/${encodeURIComponent(id)}`)
  },

  async enableConnector(id: string): Promise<IntegrationConnector> {
    const { data } = await http.post<IntegrationConnector>(
      `/integrations/${encodeURIComponent(id)}/enable`,
    )
    return data
  },

  async disableConnector(id: string): Promise<IntegrationConnector> {
    const { data } = await http.post<IntegrationConnector>(
      `/integrations/${encodeURIComponent(id)}/disable`,
    )
    return data
  },
}
