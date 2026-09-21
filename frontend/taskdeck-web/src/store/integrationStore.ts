import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { integrationsApi } from '../api/integrationsApi'
import { useToastStore } from './toastStore'
import { useSessionStore } from './sessionStore'
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
  const session = useSessionStore()

  const connectors = ref<IntegrationConnector[]>([])
  const selectedConnector = ref<IntegrationConnectorDetail | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  type ReadLane = 'list' | 'detail'
  type ReadRetry = () => Promise<void>

  interface ReadOwner {
    epoch: number
    token: symbol
  }

  let lifecycleEpoch = 0
  const readOwners = new Map<ReadLane, ReadOwner>()
  const readRetries = new Map<ReadLane, ReadRetry>()
  const activeReadTokens = new Set<symbol>()

  function syncLoading() {
    loading.value = activeReadTokens.size > 0
  }

  function beginRead(lane: ReadLane, retry: ReadRetry): ReadOwner {
    const previous = readOwners.get(lane)
    if (previous?.epoch === lifecycleEpoch) activeReadTokens.delete(previous.token)

    const owner = { epoch: lifecycleEpoch, token: Symbol(lane) }
    readOwners.set(lane, owner)
    readRetries.set(lane, retry)
    activeReadTokens.add(owner.token)
    error.value = null
    syncLoading()
    return owner
  }

  function ownsRead(lane: ReadLane, owner: ReadOwner): boolean {
    const current = readOwners.get(lane)
    return owner.epoch === lifecycleEpoch && current?.token === owner.token
  }

  function finishRead(lane: ReadLane, owner: ReadOwner) {
    if (!ownsRead(lane, owner)) return
    readOwners.delete(lane)
    readRetries.delete(lane)
    activeReadTokens.delete(owner.token)
    syncLoading()
  }

  function invalidateOperations() {
    lifecycleEpoch += 1
    readOwners.clear()
    readRetries.clear()
    activeReadTokens.clear()
    loading.value = false
    error.value = null
  }

  function retryEmptyActiveReads() {
    const listRetry = readOwners.has('list') && connectors.value.length === 0
      ? readRetries.get('list')
      : undefined
    const detailRetry = readOwners.has('detail') && selectedConnector.value === null
      ? readRetries.get('detail')
      : undefined

    invalidateOperations()
    if (listRetry) {
      void listRetry().catch(() => {
        // The retried store action owns current error/toast state.
      })
    }
    if (detailRetry) {
      void detailRetry().catch(() => {
        // The retried store action owns current error/toast state.
      })
    }
  }

  function ownsLifetime(epoch: number): boolean {
    return epoch === lifecycleEpoch
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

    const owner = beginRead('list', fetchConnectors)
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

    const owner = beginRead('detail', () => fetchConnectorDetail(id))
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
    const epoch = lifecycleEpoch
    try {
      error.value = null
      const connector = await integrationsApi.registerConnector(request)
      if (!ownsLifetime(epoch)) return connector

      connectors.value = [connector, ...connectors.value]
      toast.success('Connector registered successfully.')
      return connector
    } catch (e: unknown) {
      if (ownsLifetime(epoch)) {
        const msg = getErrorDisplay(e, 'Failed to register connector').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    }
  }

  async function updateConnector(id: string, request: UpdateIntegrationConnectorRequest) {
    guardDemoMutation()
    const epoch = lifecycleEpoch
    try {
      error.value = null
      const updated = await integrationsApi.updateConnector(id, request)
      if (!ownsLifetime(epoch)) return updated

      connectors.value = connectors.value.map((connector) => connector.id === id ? updated : connector)
      if (selectedConnector.value?.id === id) {
        selectedConnector.value = { ...selectedConnector.value, ...updated }
      }
      toast.success('Connector updated.')
      return updated
    } catch (e: unknown) {
      if (ownsLifetime(epoch)) {
        const msg = getErrorDisplay(e, 'Failed to update connector').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    }
  }

  async function deleteConnector(id: string) {
    guardDemoMutation()
    const epoch = lifecycleEpoch
    try {
      error.value = null
      await integrationsApi.deleteConnector(id)
      if (!ownsLifetime(epoch)) return

      connectors.value = connectors.value.filter((connector) => connector.id !== id)
      if (selectedConnector.value?.id === id) {
        selectedConnector.value = null
      }
      toast.success('Connector removed.')
    } catch (e: unknown) {
      if (ownsLifetime(epoch)) {
        const msg = getErrorDisplay(e, 'Failed to remove connector').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    }
  }

  async function enableConnector(id: string) {
    guardDemoMutation()
    const epoch = lifecycleEpoch
    try {
      error.value = null
      const updated = await integrationsApi.enableConnector(id)
      if (!ownsLifetime(epoch)) return

      connectors.value = connectors.value.map((connector) => connector.id === id ? updated : connector)
      if (selectedConnector.value?.id === id) {
        selectedConnector.value = { ...selectedConnector.value, ...updated }
      }
      toast.success('Connector enabled.')
    } catch (e: unknown) {
      if (ownsLifetime(epoch)) {
        const msg = getErrorDisplay(e, 'Failed to enable connector').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    }
  }

  async function disableConnector(id: string) {
    guardDemoMutation()
    const epoch = lifecycleEpoch
    try {
      error.value = null
      const updated = await integrationsApi.disableConnector(id)
      if (!ownsLifetime(epoch)) return

      connectors.value = connectors.value.map((connector) => connector.id === id ? updated : connector)
      if (selectedConnector.value?.id === id) {
        selectedConnector.value = { ...selectedConnector.value, ...updated }
      }
      toast.success('Connector disabled.')
    } catch (e: unknown) {
      if (ownsLifetime(epoch)) {
        const msg = getErrorDisplay(e, 'Failed to disable connector').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    }
  }

  function $reset() {
    invalidateOperations()
    connectors.value = []
    selectedConnector.value = null
  }

  watch(
    () => [session.userId, session.isAuthenticated, session.isDemo],
    $reset,
    { flush: 'sync' },
  )

  watch(
    () => session.token,
    retryEmptyActiveReads,
    { flush: 'sync' },
  )

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
