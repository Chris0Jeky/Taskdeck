import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { integrationsApi } from '../../api/integrationsApi'
import { useIntegrationStore } from '../../store/integrationStore'
import type { IntegrationConnector, IntegrationConnectorDetail } from '../../types/integration'

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: false,
  }
})

vi.mock('../../api/integrationsApi', () => ({
  integrationsApi: {
    listConnectors: vi.fn(),
    getConnector: vi.fn(),
    registerConnector: vi.fn(),
    updateConnector: vi.fn(),
    deleteConnector: vi.fn(),
    enableConnector: vi.fn(),
    disableConnector: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

const MOCK_CONNECTOR: IntegrationConnector = {
  id: 'conn-1',
  name: 'Test Connector',
  connectorType: 'BrowserClipper',
  direction: 'Inbound',
  status: 'Active',
  configuration: null,
  createdAt: '2026-04-15T00:00:00Z',
  updatedAt: '2026-04-15T00:00:00Z',
}

const MOCK_DETAIL: IntegrationConnectorDetail = {
  ...MOCK_CONNECTOR,
  recentEvents: [
    {
      id: 'evt-1',
      eventType: 'Connected',
      payload: 'Connector registered.',
      createdAt: '2026-04-15T00:00:00Z',
    },
  ],
}

describe('integrationStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('starts with empty default state', () => {
    const store = useIntegrationStore()

    expect(store.connectors).toEqual([])
    expect(store.selectedConnector).toBeNull()
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('fetchConnectors populates connectors on success', async () => {
    vi.mocked(integrationsApi.listConnectors).mockResolvedValue([MOCK_CONNECTOR])
    const store = useIntegrationStore()

    await store.fetchConnectors()

    expect(store.connectors).toEqual([MOCK_CONNECTOR])
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('fetchConnectors sets error on failure', async () => {
    vi.mocked(integrationsApi.listConnectors).mockRejectedValue(new Error('Network error'))
    const store = useIntegrationStore()

    await store.fetchConnectors()

    expect(store.error).toBe('Failed to fetch integrations')
    expect(store.loading).toBe(false)
    expect(toastMocks.error).toHaveBeenCalledWith('Failed to fetch integrations')
  })

  it('fetchConnectorDetail populates selectedConnector', async () => {
    vi.mocked(integrationsApi.getConnector).mockResolvedValue(MOCK_DETAIL)
    const store = useIntegrationStore()

    await store.fetchConnectorDetail('conn-1')

    expect(store.selectedConnector).toEqual(MOCK_DETAIL)
    expect(store.loading).toBe(false)
  })

  it('registerConnector adds to list and shows toast', async () => {
    vi.mocked(integrationsApi.registerConnector).mockResolvedValue(MOCK_CONNECTOR)
    const store = useIntegrationStore()

    const result = await store.registerConnector({
      name: 'Test',
      connectorType: 0,
      direction: 0,
    })

    expect(result).toEqual(MOCK_CONNECTOR)
    expect(store.connectors).toContainEqual(MOCK_CONNECTOR)
    expect(toastMocks.success).toHaveBeenCalledWith('Connector registered successfully.')
  })

  it('deleteConnector removes from list', async () => {
    vi.mocked(integrationsApi.listConnectors).mockResolvedValue([MOCK_CONNECTOR])
    vi.mocked(integrationsApi.deleteConnector).mockResolvedValue(undefined)
    const store = useIntegrationStore()

    await store.fetchConnectors()
    expect(store.connectors).toHaveLength(1)

    await store.deleteConnector('conn-1')

    expect(store.connectors).toHaveLength(0)
    expect(toastMocks.success).toHaveBeenCalledWith('Connector removed.')
  })

  it('enableConnector updates status in list', async () => {
    const disabledConnector = { ...MOCK_CONNECTOR, status: 'Disabled' as const }
    const enabledConnector = { ...MOCK_CONNECTOR, status: 'Active' as const }
    vi.mocked(integrationsApi.listConnectors).mockResolvedValue([disabledConnector])
    vi.mocked(integrationsApi.enableConnector).mockResolvedValue(enabledConnector)
    const store = useIntegrationStore()

    await store.fetchConnectors()
    await store.enableConnector('conn-1')

    expect(store.connectors[0].status).toBe('Active')
    expect(toastMocks.success).toHaveBeenCalledWith('Connector enabled.')
  })

  it('disableConnector updates status in list', async () => {
    const disabledConnector = { ...MOCK_CONNECTOR, status: 'Disabled' as const }
    vi.mocked(integrationsApi.listConnectors).mockResolvedValue([MOCK_CONNECTOR])
    vi.mocked(integrationsApi.disableConnector).mockResolvedValue(disabledConnector)
    const store = useIntegrationStore()

    await store.fetchConnectors()
    await store.disableConnector('conn-1')

    expect(store.connectors[0].status).toBe('Disabled')
    expect(toastMocks.success).toHaveBeenCalledWith('Connector disabled.')
  })

  it('$reset restores initial state', async () => {
    vi.mocked(integrationsApi.listConnectors).mockResolvedValue([MOCK_CONNECTOR])
    const store = useIntegrationStore()

    await store.fetchConnectors()
    expect(store.connectors).not.toEqual([])

    store.$reset()
    expect(store.connectors).toEqual([])
    expect(store.selectedConnector).toBeNull()
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })
})
