import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { integrationsApi } from '../../api/integrationsApi'
import { useIntegrationStore } from '../../store/integrationStore'
import { useSessionStore } from '../../store/sessionStore'
import type { IntegrationConnector, IntegrationConnectorDetail } from '../../types/integration'

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: false }
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

vi.mock('../../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    changePassword: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}

function connector(
  id: string,
  name: string,
  status: IntegrationConnector['status'] = 'Active',
): IntegrationConnector {
  return {
    id,
    name,
    connectorType: 'BrowserClipper',
    direction: 'Inbound',
    status,
    configuration: null,
    createdAt: '2026-09-21T00:00:00Z',
    updatedAt: '2026-09-21T00:00:00Z',
  }
}

function detail(
  id: string,
  name: string,
  status: IntegrationConnector['status'] = 'Active',
): IntegrationConnectorDetail {
  return { ...connector(id, name, status), recentEvents: [] }
}

async function flushQueue() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('integrationStore mutation ordering', () => {
  let session: ReturnType<typeof useSessionStore>
  let store: ReturnType<typeof useIntegrationStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'account-a'
    store = useIntegrationStore()
    store.connectors = [connector('connector-1', 'Connector')]
    store.selectedConnector = detail('connector-1', 'Connector')
    vi.clearAllMocks()
  })

  it('serializes enable then disable for one connector', async () => {
    const enable = deferred<IntegrationConnector>()
    const disable = deferred<IntegrationConnector>()
    vi.mocked(integrationsApi.enableConnector).mockReturnValue(enable.promise)
    vi.mocked(integrationsApi.disableConnector).mockReturnValue(disable.promise)

    const enableRequest = store.enableConnector('connector-1')
    const disableRequest = store.disableConnector('connector-1')
    expect(integrationsApi.disableConnector).not.toHaveBeenCalled()

    enable.resolve(connector('connector-1', 'Connector', 'Active'))
    await enableRequest
    await flushQueue()
    expect(integrationsApi.disableConnector).toHaveBeenCalledTimes(1)

    disable.resolve(connector('connector-1', 'Connector', 'Disabled'))
    await disableRequest
    expect(store.connectors[0]?.status).toBe('Disabled')
    expect(store.selectedConnector?.status).toBe('Disabled')
  })

  it('serializes two updates and keeps the final intent', async () => {
    const first = deferred<IntegrationConnector>()
    const second = deferred<IntegrationConnector>()
    vi.mocked(integrationsApi.updateConnector)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const firstRequest = store.updateConnector('connector-1', { name: 'First' })
    const secondRequest = store.updateConnector('connector-1', { name: 'Second' })
    expect(integrationsApi.updateConnector).toHaveBeenCalledTimes(1)

    first.resolve(connector('connector-1', 'First'))
    await firstRequest
    await flushQueue()
    expect(integrationsApi.updateConnector).toHaveBeenCalledTimes(2)

    second.resolve(connector('connector-1', 'Second'))
    await secondRequest
    expect(store.connectors[0]?.name).toBe('Second')
    expect(store.selectedConnector?.name).toBe('Second')
  })

  it('continues with queued intent after a failed predecessor', async () => {
    const first = deferred<IntegrationConnector>()
    const second = deferred<IntegrationConnector>()
    vi.mocked(integrationsApi.updateConnector)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const firstRequest = store.updateConnector('connector-1', { name: 'First' })
    const secondRequest = store.updateConnector('connector-1', { name: 'Second' })
    first.reject(new Error('first failed'))
    await expect(firstRequest).rejects.toThrow('first failed')
    await flushQueue()
    expect(integrationsApi.updateConnector).toHaveBeenCalledTimes(2)

    second.resolve(connector('connector-1', 'Second'))
    await secondRequest
    expect(store.connectors[0]?.name).toBe('Second')
    expect(store.error).toBeNull()
  })

  it('orders update before delete and leaves the connector removed', async () => {
    const update = deferred<IntegrationConnector>()
    const remove = deferred<void>()
    vi.mocked(integrationsApi.updateConnector).mockReturnValue(update.promise)
    vi.mocked(integrationsApi.deleteConnector).mockReturnValue(remove.promise)

    const updateRequest = store.updateConnector('connector-1', { name: 'Updated' })
    const deleteRequest = store.deleteConnector('connector-1')
    expect(integrationsApi.deleteConnector).not.toHaveBeenCalled()

    update.resolve(connector('connector-1', 'Updated'))
    await updateRequest
    await flushQueue()
    expect(integrationsApi.deleteConnector).toHaveBeenCalledTimes(1)

    remove.resolve()
    await deleteRequest
    expect(store.connectors).toEqual([])
    expect(store.selectedConnector).toBeNull()
  })

  it('does not start queued old-session transport after logout', async () => {
    const first = deferred<IntegrationConnector>()
    vi.mocked(integrationsApi.updateConnector).mockReturnValue(first.promise)

    const firstRequest = store.updateConnector('connector-1', { name: 'First' })
    const queuedRequest = store.updateConnector('connector-1', { name: 'Second' })
    session.userId = null
    session.userId = 'account-a'

    first.resolve(connector('connector-1', 'First'))
    await firstRequest
    await queuedRequest

    expect(integrationsApi.updateConnector).toHaveBeenCalledTimes(1)
    expect(store.connectors).toEqual([])
    expect(store.error).toBeNull()
  })

  it('keeps different connectors concurrent', async () => {
    store.connectors = [
      connector('connector-1', 'One'),
      connector('connector-2', 'Two'),
    ]
    const first = deferred<IntegrationConnector>()
    const second = deferred<IntegrationConnector>()
    vi.mocked(integrationsApi.updateConnector)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const firstRequest = store.updateConnector('connector-1', { name: 'One updated' })
    const secondRequest = store.updateConnector('connector-2', { name: 'Two updated' })
    expect(integrationsApi.updateConnector).toHaveBeenCalledTimes(2)

    first.resolve(connector('connector-1', 'One updated'))
    second.resolve(connector('connector-2', 'Two updated'))
    await Promise.all([firstRequest, secondRequest])
    expect(store.connectors.map(item => item.name)).toEqual(['One updated', 'Two updated'])
  })
})
