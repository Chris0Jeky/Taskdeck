import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { integrationsApi } from '../../api/integrationsApi'
import { useIntegrationStore } from '../../store/integrationStore'
import { useSessionStore } from '../../store/sessionStore'
import type { IntegrationConnector } from '../../types/integration'

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
  useToastStore: () => ({
    error: vi.fn(),
    success: vi.fn(),
    info: vi.fn(),
    warning: vi.fn(),
  }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (error: unknown, fallback: string) => ({
    message: error instanceof Error ? error.message : fallback,
  }),
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

async function flushQueue() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('integrationStore mutation error ownership', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    const session = useSessionStore()
    session.userId = 'account-a'
    vi.clearAllMocks()
  })

  it('does not erase an independent failure when queued same-connector work starts', async () => {
    const store = useIntegrationStore()
    store.connectors = [connector('connector-1', 'One'), connector('connector-2', 'Two')]

    const first = deferred<IntegrationConnector>()
    const independent = deferred<IntegrationConnector>()
    const queued = deferred<IntegrationConnector>()
    vi.mocked(integrationsApi.updateConnector)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(independent.promise)
      .mockReturnValueOnce(queued.promise)

    const firstRequest = store.updateConnector('connector-1', { name: 'First' })
    const queuedRequest = store.updateConnector('connector-1', { name: 'Queued' })
    const independentRequest = store.updateConnector('connector-2', { name: 'Independent' })

    independent.reject(new Error('independent connector failed'))
    await expect(independentRequest).rejects.toThrow('independent connector failed')
    expect(store.error).toBe('independent connector failed')

    first.resolve(connector('connector-1', 'First'))
    await firstRequest
    await flushQueue()

    expect(integrationsApi.updateConnector).toHaveBeenCalledTimes(3)
    expect(store.error).toBe('independent connector failed')

    queued.resolve(connector('connector-1', 'Queued'))
    await queuedRequest
    expect(store.error).toBe('independent connector failed')
  })
})
