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

function connector(id: string, name: string, status: IntegrationConnector['status'] = 'Active'): IntegrationConnector {
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

function detail(id: string, name: string, status: IntegrationConnector['status'] = 'Active'): IntegrationConnectorDetail {
  return { ...connector(id, name, status), recentEvents: [] }
}

describe('integrationStore session and mutation ownership', () => {
  let session: ReturnType<typeof useSessionStore>
  let store: ReturnType<typeof useIntegrationStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'account-a'
    store = useIntegrationStore()
    vi.clearAllMocks()
  })

  it('clears cached connectors when the session identity is removed', () => {
    store.connectors = [connector('shared-id', 'Account A')]
    store.selectedConnector = detail('shared-id', 'Account A')

    session.userId = null

    expect(store.connectors).toEqual([])
    expect(store.selectedConnector).toBeNull()
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
  })

  it('does not append a registration that settles after logout and same-user login', async () => {
    const pending = deferred<IntegrationConnector>()
    vi.mocked(integrationsApi.registerConnector).mockReturnValue(pending.promise)

    const request = store.registerConnector({
      name: 'Account A connector',
      connectorType: 0,
      direction: 0,
    })
    session.userId = null
    session.userId = 'account-a'
    pending.resolve(connector('registered-a', 'Account A connector'))
    await request

    expect(store.connectors).toEqual([])
    expect(store.error).toBeNull()
    expect(toastMocks.success).not.toHaveBeenCalled()
  })

  it('does not let an old update overwrite same-id state loaded for the replacement session', async () => {
    const pending = deferred<IntegrationConnector>()
    vi.mocked(integrationsApi.updateConnector).mockReturnValue(pending.promise)
    store.connectors = [connector('shared-id', 'Account A')]
    store.selectedConnector = detail('shared-id', 'Account A')

    const request = store.updateConnector('shared-id', { name: 'Account A updated' })
    session.userId = null
    session.userId = 'account-b'
    store.connectors = [connector('shared-id', 'Account B')]
    store.selectedConnector = detail('shared-id', 'Account B')
    pending.resolve(connector('shared-id', 'Account A updated'))
    await request

    expect(store.connectors[0]?.name).toBe('Account B')
    expect(store.selectedConnector?.name).toBe('Account B')
    expect(store.error).toBeNull()
    expect(toastMocks.success).not.toHaveBeenCalled()
  })

  it('does not let an old delete remove same-id state loaded for the replacement session', async () => {
    const pending = deferred<void>()
    vi.mocked(integrationsApi.deleteConnector).mockReturnValue(pending.promise)
    store.connectors = [connector('shared-id', 'Account A')]
    store.selectedConnector = detail('shared-id', 'Account A')

    const request = store.deleteConnector('shared-id')
    session.userId = null
    session.userId = 'account-b'
    store.connectors = [connector('shared-id', 'Account B')]
    store.selectedConnector = detail('shared-id', 'Account B')
    pending.resolve()
    await request

    expect(store.connectors[0]?.name).toBe('Account B')
    expect(store.selectedConnector?.name).toBe('Account B')
    expect(store.error).toBeNull()
    expect(toastMocks.success).not.toHaveBeenCalled()
  })

  it('does not publish a stale mutation failure into the replacement session', async () => {
    const pending = deferred<IntegrationConnector>()
    vi.mocked(integrationsApi.disableConnector).mockReturnValue(pending.promise)

    const request = store.disableConnector('shared-id')
    session.userId = null
    session.userId = 'account-b'
    pending.reject(new Error('old account failure'))
    await expect(request).rejects.toThrow('old account failure')

    expect(store.error).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })
})
