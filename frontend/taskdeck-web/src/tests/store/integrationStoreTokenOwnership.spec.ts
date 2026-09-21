import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { integrationsApi } from '../../api/integrationsApi'
import { useIntegrationStore } from '../../store/integrationStore'
import { useSessionStore } from '../../store/sessionStore'
import type { IntegrationConnector } from '../../types/integration'

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
    refreshToken: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
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

function token(suffix: string): string {
  const body = btoa(JSON.stringify({ exp: 1893456000 }))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/g, '')
  return `header.${body}.${suffix}`
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

describe('integrationStore token ownership', () => {
  let session: ReturnType<typeof useSessionStore>
  let store: ReturnType<typeof useIntegrationStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'account-a'
    session.token = token('old')
    store = useIntegrationStore()
    vi.clearAllMocks()
  })

  it('invalidates an old list read when the credential rotates for the same user', async () => {
    const pending = deferred<IntegrationConnector[]>()
    vi.mocked(integrationsApi.listConnectors).mockReturnValue(pending.promise)
    const request = store.fetchConnectors()

    session.token = token('new')
    pending.resolve([connector('old-token', 'Old token connector')])
    await request

    expect(store.connectors).toEqual([])
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('suppresses an old mutation failure after credential rotation', async () => {
    const pending = deferred<IntegrationConnector>()
    vi.mocked(integrationsApi.updateConnector).mockReturnValue(pending.promise)
    const request = store.updateConnector('connector-1', { name: 'Changed' })

    session.token = token('new')
    pending.reject(new Error('old credential failure'))
    await expect(request).rejects.toThrow('old credential failure')

    expect(store.connectors).toEqual([])
    expect(store.error).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })
})
