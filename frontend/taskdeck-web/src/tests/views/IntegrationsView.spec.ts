import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { reactive } from 'vue'
import IntegrationsView from '../../views/IntegrationsView.vue'

const mockIntegrationStore = reactive({
  connectors: [],
  selectedConnector: null,
  loading: false,
  error: null,
  fetchConnectors: vi.fn().mockResolvedValue(undefined),
  fetchConnectorDetail: vi.fn(),
  registerConnector: vi.fn(),
  enableConnector: vi.fn(),
  disableConnector: vi.fn(),
  deleteConnector: vi.fn(),
})

vi.mock('../../store/integrationStore', () => ({
  useIntegrationStore: () => mockIntegrationStore,
}))

describe('IntegrationsView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockIntegrationStore.connectors = []
    mockIntegrationStore.selectedConnector = null
    mockIntegrationStore.loading = false
    mockIntegrationStore.error = null
  })

  it('describes the page as registry management rather than connector ingestion', async () => {
    const wrapper = mount(IntegrationsView)
    await flushPromises()

    expect(wrapper.text()).toContain('Register and manage connector definitions for future integrations.')
    expect(wrapper.text()).toContain('Registration, enablement, and configuration do not yet ingest external content.')
    expect(mockIntegrationStore.fetchConnectors).toHaveBeenCalledOnce()
  })

  it('keeps standalone note import and web clip capture distinct from connector registration', async () => {
    const wrapper = mount(IntegrationsView)
    await flushPromises()

    expect(wrapper.text()).toContain('Connector runtime ingestion is not available yet; use the note import or web clip capture routes for content today.')
  })
})
