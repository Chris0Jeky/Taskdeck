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

  type ReadLane = 'list' | 'detail'
  interface ReadOwner {
    epoch: number
    token: symbol
  }

  let readEpoch = 0
  const readOwners = new Map<ReadLane, ReadOwner>()
  const activeReadTokens = new Set<symbol>()

  function syncLoading() {
    loading.value = activeReadTokens.size > 0
  }

  function beginRead(lane: ReadLane): ReadOwner {
    const previous = readOwners.get(lane)
    if (previous?.epoch === readEpoch) activeReadTokens.delete(previous.token)

    const owner = { epoch: readEpoch, token: Symbol(lane) }
    readOwners.set(lane, owner)
    activeReadTokens.add(owner.token)
    error.value = null
    syncLoading()
    return owner
  }

  function ownsRead(lane: ReadLane, owner: ReadOwner): boolean {
    const current = readOwners.get(lane)
    return owner.epoch === readEpoch && current?.token === owner.token
  }

  function finishRead(lane: ReadLane, owner: ReadOwner) {
    if (!ownsRead(lane, owner)) return
    readOwners.delete(lane)
    activeReadTokens.delete(owner.token)
    syncLoading()
  }

  function invalidateReads() {
    readEpoch += 1
    readOwners.clear()
    activeReadTokens.clear()
    loading.value = false
  }

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

    const owner = beginRead('list')
    try {
      const result = await integrationsApi.listConnectors()
      if (!ownsRead('list', owner)) return
      connectors.value = result
    } catch (e: unknown) {
      if (!ownsRead('list', owner)) return
      connectors.value = []
      const msg = getErrorDisplay(e, 'Failed to fetch integrations').message
      error.value = msg
      toast.error(msg)
    } finally {
      finishRead('list', owner)
    }
  }

  async function fetchConnectorDetail(id: string) {
    if (isDemoMode) {
      error.value = 'Integrations are not available in demo mode.'
      return
    }

    const owner = beginRead('detail')
    try {
      const result = await integrationsApi.getConnector(id)
      if (!ownsRead('detail', owner)) return
      selectedConnector.value = result
    } catch (e: unknown) {
      if (!ownsRead('detail', owner)) return
      const msg = getErrorDisplay(e, 'Failed to fetch connector details').message
      error.value = msg
      selectedConnector.value = null
      toast.error(msg)
    } finally {
      finishRead('detail', owner)
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
    invalidateReads()
    connectors.value = []
    selectedConnector.value = null
    error.value = null
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
