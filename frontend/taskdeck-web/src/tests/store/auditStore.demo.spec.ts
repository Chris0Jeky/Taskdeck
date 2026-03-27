import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: true,
  }
})

vi.mock('../../api/auditApi', () => ({
  auditApi: {
    getBoardHistory: vi.fn(),
    getEntityHistory: vi.fn(),
    getUserHistory: vi.fn(),
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

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

import { useAuditStore } from '../../store/auditStore'
import { auditApi } from '../../api/auditApi'

describe('auditStore demo mode', () => {
  let store: ReturnType<typeof useAuditStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    store = useAuditStore()
  })

  it('fetchBoardHistory returns empty array without calling API', async () => {
    await store.fetchBoardHistory('board-1')

    expect(store.entries).toEqual([])
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(auditApi.getBoardHistory).not.toHaveBeenCalled()
  })

  it('fetchEntityHistory returns empty array without calling API', async () => {
    await store.fetchEntityHistory('Card', 'card-1')

    expect(store.entries).toEqual([])
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(auditApi.getEntityHistory).not.toHaveBeenCalled()
  })

  it('fetchUserHistory returns empty array without calling API', async () => {
    await store.fetchUserHistory()

    expect(store.entries).toEqual([])
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(auditApi.getUserHistory).not.toHaveBeenCalled()
  })
})
