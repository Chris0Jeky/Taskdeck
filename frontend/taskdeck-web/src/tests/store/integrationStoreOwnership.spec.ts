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

function connector(id: string, name: string): IntegrationConnector {
  return {
    id,
    name,
    connectorType: 'BrowserClipper',
    direction: 'Inbound',
    status: 'Active',
    configuration: null,
    createdAt: '2026-09-21T00:00:00Z',
    updatedAt: '2026-09-21T00:00:00Z',
  }
}

function detail(id: string, name: string): IntegrationConnectorDetail {
  return { ...connector(id, name), recentEvents: [] }
}

describe('integrationStore request ownership', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('does not let an old A detail replace the newer A visit after A to B to A', async () => {
    const oldA = deferred<IntegrationConnectorDetail>()
    const boardB = deferred<IntegrationConnectorDetail>()
    const newA = deferred<IntegrationConnectorDetail>()
    vi.mocked(integrationsApi.getConnector)
      .mockReturnValueOnce(oldA.promise)
      .mockReturnValueOnce(boardB.promise)
      .mockReturnValueOnce(newA.promise)
    const store = useIntegrationStore()

    const oldRequest = store.fetchConnectorDetail('connector-a')
    const bRequest = store.fetchConnectorDetail('connector-b')
    boardB.resolve(detail('connector-b', 'B'))
    await bRequest

    const newRequest = store.fetchConnectorDetail('connector-a')
    newA.resolve(detail('connector-a', 'A new'))
    await newRequest
    expect(store.selectedConnector?.name).toBe('A new')

    oldA.resolve(detail('connector-a', 'A old'))
    await oldRequest

    expect(store.selectedConnector?.name).toBe('A new')
    expect(store.error).toBeNull()
  })

  it('invalidates an old same-id detail success across reset', async () => {
    const oldA = deferred<IntegrationConnectorDetail>()
    const newA = deferred<IntegrationConnectorDetail>()
    vi.mocked(integrationsApi.getConnector)
      .mockReturnValueOnce(oldA.promise)
      .mockReturnValueOnce(newA.promise)
    const store = useIntegrationStore()

    const oldRequest = store.fetchConnectorDetail('connector-a')
    store.$reset()
    const newRequest = store.fetchConnectorDetail('connector-a')
    newA.resolve(detail('connector-a', 'A new'))
    await newRequest

    oldA.resolve(detail('connector-a', 'A old'))
    await oldRequest

    expect(store.selectedConnector?.name).toBe('A new')
    expect(store.error).toBeNull()
  })

  it('invalidates an old same-id detail across reset without letting its failure clear the new result', async () => {
    const oldA = deferred<IntegrationConnectorDetail>()
    const newA = deferred<IntegrationConnectorDetail>()
    vi.mocked(integrationsApi.getConnector)
      .mockReturnValueOnce(oldA.promise)
      .mockReturnValueOnce(newA.promise)
    const store = useIntegrationStore()

    const oldRequest = store.fetchConnectorDetail('connector-a')
    store.$reset()
    const newRequest = store.fetchConnectorDetail('connector-a')
    newA.resolve(detail('connector-a', 'A new'))
    await newRequest

    oldA.reject(new Error('old request failed'))
    await oldRequest

    expect(store.selectedConnector?.name).toBe('A new')
    expect(store.error).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('keeps a reset list empty when the pre-reset request settles', async () => {
    const pending = deferred<IntegrationConnector[]>()
    vi.mocked(integrationsApi.listConnectors).mockReturnValue(pending.promise)
    const store = useIntegrationStore()

    const request = store.fetchConnectors()
    store.$reset()
    pending.resolve([connector('old', 'Old session')])
    await request

    expect(store.connectors).toEqual([])
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('keeps the newest list when two reads settle in reverse order', async () => {
    const older = deferred<IntegrationConnector[]>()
    const newer = deferred<IntegrationConnector[]>()
    vi.mocked(integrationsApi.listConnectors)
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise)
    const store = useIntegrationStore()

    const oldRequest = store.fetchConnectors()
    const newRequest = store.fetchConnectors()
    newer.resolve([connector('new', 'New')])
    await newRequest
    older.resolve([connector('old', 'Old')])
    await oldRequest

    expect(store.connectors.map(item => item.id)).toEqual(['new'])
  })

  it('keeps loading true until the current list and detail owners both settle', async () => {
    const list = deferred<IntegrationConnector[]>()
    const selected = deferred<IntegrationConnectorDetail>()
    vi.mocked(integrationsApi.listConnectors).mockReturnValue(list.promise)
    vi.mocked(integrationsApi.getConnector).mockReturnValue(selected.promise)
    const store = useIntegrationStore()

    const listRequest = store.fetchConnectors()
    const detailRequest = store.fetchConnectorDetail('connector-a')
    expect(store.loading).toBe(true)

    list.resolve([connector('connector-a', 'A')])
    await listRequest
    expect(store.loading).toBe(true)

    selected.resolve(detail('connector-a', 'A'))
    await detailRequest
    expect(store.loading).toBe(false)
  })
})
