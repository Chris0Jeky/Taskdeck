import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { integrationsApi } from '../../api/integrationsApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

function makeRawConnector(overrides = {}) {
  return {
    id: 'c1',
    name: 'Slack',
    connectorType: 0,
    direction: 0,
    status: 0,
    ...overrides,
  }
}

describe('integrationsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('listConnectors', () => {
    it('fetches connectors and normalizes enums', async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: [
          makeRawConnector({ connectorType: 'WebhookInbound', direction: 'Inbound', status: 'Active' }),
          makeRawConnector({ id: 'c2', connectorType: 1, direction: 1, status: 1 }),
        ],
      })

      const result = await integrationsApi.listConnectors()

      expect(http.get).toHaveBeenCalledWith('/integrations')
      expect(result).toHaveLength(2)
      expect(result[0].connectorType).toBe('WebhookInbound')
      expect(result[0].direction).toBe('Inbound')
      expect(result[0].status).toBe('Active')
      expect(result[1].connectorType).toBe('MarkdownImport')
      expect(result[1].direction).toBe('Context')
      expect(result[1].status).toBe('Disabled')
    })
  })

  describe('getConnector', () => {
    it('fetches detail and normalizes events', async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: {
          ...makeRawConnector(),
          recentEvents: [
            { id: 'ev1', eventType: 0 },
            { id: 'ev2', eventType: 2 },
          ],
        },
      })

      const result = await integrationsApi.getConnector('c1')

      expect(http.get).toHaveBeenCalledWith('/integrations/c1')
      expect(result.recentEvents).toHaveLength(2)
      expect(result.recentEvents[0].eventType).toBe('Connected')
      expect(result.recentEvents[1].eventType).toBe('DataReceived')
    })

    it('encodes special characters in id', async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: { ...makeRawConnector({ id: 'c/1' }), recentEvents: [] },
      })

      await integrationsApi.getConnector('c/1')

      expect(http.get).toHaveBeenCalledWith('/integrations/c%2F1')
    })
  })

  describe('registerConnector', () => {
    it('posts and normalizes response', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: makeRawConnector() })

      const result = await integrationsApi.registerConnector({ name: 'Slack' } as any)

      expect(http.post).toHaveBeenCalledWith('/integrations', { name: 'Slack' })
      expect(result.id).toBe('c1')
    })
  })

  describe('updateConnector', () => {
    it('puts and normalizes response', async () => {
      vi.mocked(http.put).mockResolvedValue({
        data: makeRawConnector({ name: 'Updated' }),
      })

      const result = await integrationsApi.updateConnector('c1', { name: 'Updated' } as any)

      expect(http.put).toHaveBeenCalledWith('/integrations/c1', { name: 'Updated' })
      expect(result.name).toBe('Updated')
    })
  })

  describe('deleteConnector', () => {
    it('sends delete request', async () => {
      vi.mocked(http.delete).mockResolvedValue({})

      await integrationsApi.deleteConnector('c1')

      expect(http.delete).toHaveBeenCalledWith('/integrations/c1')
    })
  })

  describe('enableConnector', () => {
    it('posts to enable endpoint', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: makeRawConnector({ status: 'Active' }) })

      const result = await integrationsApi.enableConnector('c1')

      expect(http.post).toHaveBeenCalledWith('/integrations/c1/enable')
      expect(result.status).toBe('Active')
    })
  })

  describe('disableConnector', () => {
    it('posts to disable endpoint', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: makeRawConnector({ status: 'Disabled' }) })

      const result = await integrationsApi.disableConnector('c1')

      expect(http.post).toHaveBeenCalledWith('/integrations/c1/disable')
      expect(result.status).toBe('Disabled')
    })
  })
})
