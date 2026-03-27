import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: true,
  }
})

vi.mock('../../api/queueApi', () => ({
  queueApi: {
    getUserRequests: vi.fn(),
    getRequestsByStatus: vi.fn(),
    createRequest: vi.fn(),
    cancelRequest: vi.fn(),
    processNext: vi.fn(),
    getStats: vi.fn(),
  },
}))

const toastMocks = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => ({ userId: 'demo-user', requireUserId: vi.fn() }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

import { useQueueStore } from '../../store/queueStore'
import { queueApi } from '../../api/queueApi'

describe('queueStore demo mode', () => {
  let store: ReturnType<typeof useQueueStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    store = useQueueStore()
  })

  it('fetchUserRequests returns empty array without calling API', async () => {
    await store.fetchUserRequests()

    expect(store.requests).toEqual([])
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(queueApi.getUserRequests).not.toHaveBeenCalled()
  })

  it('fetchByStatus returns empty array without calling API', async () => {
    await store.fetchByStatus('Pending')

    expect(store.requests).toEqual([])
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(queueApi.getRequestsByStatus).not.toHaveBeenCalled()
  })

  it('fetchStats returns zeroed stats without calling API', async () => {
    await store.fetchStats()

    expect(store.stats).toEqual({ pendingCount: 0, processingCount: 0, completedCount: 0, failedCount: 0 })
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(queueApi.getStats).not.toHaveBeenCalled()
  })

  it('submitRequest throws DemoModeError and shows toast', async () => {
    await expect(
      store.submitRequest({ boardId: 'b1', requestType: 'Instruction', instructionText: 'test' } as never),
    ).rejects.toThrow('view-only in demo mode')
    expect(toastMocks.info).toHaveBeenCalledWith('This action is view-only in demo mode.')
    expect(queueApi.createRequest).not.toHaveBeenCalled()
  })

  it('cancelRequest throws DemoModeError and shows toast', async () => {
    await expect(store.cancelRequest('req-1')).rejects.toThrow('view-only in demo mode')
    expect(toastMocks.info).toHaveBeenCalledWith('This action is view-only in demo mode.')
    expect(queueApi.cancelRequest).not.toHaveBeenCalled()
  })

  it('processNext throws DemoModeError and shows toast', async () => {
    await expect(store.processNext()).rejects.toThrow('view-only in demo mode')
    expect(toastMocks.info).toHaveBeenCalledWith('This action is view-only in demo mode.')
    expect(queueApi.processNext).not.toHaveBeenCalled()
  })
})
