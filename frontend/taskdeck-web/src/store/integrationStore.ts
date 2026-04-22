import { defineStore } from 'pinia'
import { ref } from 'vue'
import { integrationsApi } from '../api/integrationsApi'
import { useToastStore } from './toastStore'
import { isDemoMode, DemoModeError } from '../utils/demoMode'
import { getErrorDisplay } from '../composables/useErrorMapper'
import type {
  IntegrationConnector,
  IntegrationConnectorDetail,
  CreateIntegrationConnectorRequest,
  UpdateIntegrationConnectorRequest,
} from '../types/integration'

export const useIntegrationStore = defineStore('integration', () => {
  const toast = useToastStore()

  const connectors = ref<IntegrationConnector[]>([])
  const selectedConnector = ref<IntegrationConnectorDetail | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  /** Tracks the connector ID for the in-flight detail fetch so late responses are discarded. */
  let _pendingDetailId: string | null = null

  function guardDemoMutation(): never | void {
    if (isDemoMode) {
      toast.info('This action is view-only in demo mode.')
      throw new DemoModeError()
    }
  }

  async function fetchConnectors() {
    if (isDemoMode) {
      loading.value = false
      error.value = 'Integrations are not available in demo mode.'
      return
    }
    try {
      loading.value = true
      error.value = null
      connectors.value = await integrationsApi.listConnectors()
    } catch (e: unknown) {
      connectors.value = []
      const msg = getErrorDisplay(e, 'Failed to fetch integrations').message
      error.value = msg
      toast.error(msg)
    } finally {
      loading.value = false
    }
  }

  async function fetchConnectorDetail(id: string) {
    if (isDemoMode) {
      error.value = 'Integrations are not available in demo mode.'
      return
    }
    _pendingDetailId = id
    try {
      loading.value = true
      error.value = null
      const result = await integrationsApi.getConnector(id)
      // Discard stale response if the user selected a different connector while we were loading
      if (_pendingDetailId !== id) return
      selectedConnector.value = result
    } catch (e: unknown) {
      // Only update state if this is still the active request
      if (_pendingDetailId !== id) return
      const msg = getErrorDisplay(e, 'Failed to fetch connector details').message
      error.value = msg
      selectedConnector.value = null
      toast.error(msg)
    } finally {
      if (_pendingDetailId === id) {
        loading.value = false
      }
    }
  }

  async function registerConnector(request: CreateIntegrationConnectorRequest) {
    guardDemoMutation()
    try {
      error.value = null
      const connector = await integrationsApi.registerConnector(request)
      connectors.value = [connector, ...connectors.value]
      toast.success('Connector registered successfully.')
      return connector
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to register connector').message
      error.value = msg
      toast.error(msg)
      throw e
    }
  }

  async function updateConnector(id: string, request: UpdateIntegrationConnectorRequest) {
    guardDemoMutation()
    try {
      error.value = null
      const updated = await integrationsApi.updateConnector(id, request)
      connectors.value = connectors.value.map((c) => (c.id === id ? updated : c))
      if (selectedConnector.value?.id === id) {
        selectedConnector.value = { ...selectedConnector.value, ...updated }
      }
      toast.success('Connector updated.')
      return updated
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to update connector').message
      error.value = msg
      toast.error(msg)
      throw e
    }
  }

  async function deleteConnector(id: string) {
    guardDemoMutation()
    try {
      error.value = null
      await integrationsApi.deleteConnector(id)
      connectors.value = connectors.value.filter((c) => c.id !== id)
      if (selectedConnector.value?.id === id) {
        selectedConnector.value = null
      }
      toast.success('Connector removed.')
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to remove connector').message
      error.value = msg
      toast.error(msg)
      throw e
    }
  }

  async function enableConnector(id: string) {
    guardDemoMutation()
    try {
      error.value = null
      const updated = await integrationsApi.enableConnector(id)
      connectors.value = connectors.value.map((c) => (c.id === id ? updated : c))
      if (selectedConnector.value?.id === id) {
        selectedConnector.value = { ...selectedConnector.value, ...updated }
      }
      toast.success('Connector enabled.')
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to enable connector').message
      error.value = msg
      toast.error(msg)
      throw e
    }
  }

  async function disableConnector(id: string) {
    guardDemoMutation()
    try {
      error.value = null
      const updated = await integrationsApi.disableConnector(id)
      connectors.value = connectors.value.map((c) => (c.id === id ? updated : c))
      if (selectedConnector.value?.id === id) {
        selectedConnector.value = { ...selectedConnector.value, ...updated }
      }
      toast.success('Connector disabled.')
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to disable connector').message
      error.value = msg
      toast.error(msg)
      throw e
    }
  }

  function $reset() {
    connectors.value = []
    selectedConnector.value = null
    loading.value = false
    error.value = null
    _pendingDetailId = null
  }

  return {
    connectors,
    selectedConnector,
    loading,
    error,
    fetchConnectors,
    fetchConnectorDetail,
    registerConnector,
    updateConnector,
    deleteConnector,
    enableConnector,
    disableConnector,
    $reset,
  }
})
