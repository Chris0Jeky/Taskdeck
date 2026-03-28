import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { auditApi } from '../../api/auditApi'
import { useAuditStore } from '../../store/auditStore'

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: false,
  }
})

vi.mock('../../api/auditApi', () => ({
  auditApi: {
    getBoardHistory: vi.fn(),
    getEntityHistory: vi.fn(),
    getUserHistory: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

describe('auditStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('starts with empty default state', () => {
    const store = useAuditStore()

    expect(store.entries).toEqual([])
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('fetchBoardHistory populates state from the board history endpoint', async () => {
    const store = useAuditStore()
    const payload = [
      {
        id: 'audit-1',
        entityType: 'Board',
        entityId: 'board-1',
        action: 'Updated',
        userId: 'user-1',
        userName: 'Alex',
        changes: '{"name":"Updated"}',
        timestamp: '2026-03-28T12:00:00Z',
      },
    ]
    vi.mocked(auditApi.getBoardHistory).mockResolvedValue(payload)

    await store.fetchBoardHistory('board-1')

    expect(auditApi.getBoardHistory).toHaveBeenCalledWith('board-1', 50)
    expect(store.entries).toEqual(payload)
    expect(store.error).toBeNull()
  })

  it.each([
    { method: 'fetchBoardHistory' as const, call: (s: ReturnType<typeof useAuditStore>) => s.fetchBoardHistory('b', 0), api: 'getBoardHistory' as const, expectedLimit: 1 },
    { method: 'fetchBoardHistory' as const, call: (s: ReturnType<typeof useAuditStore>) => s.fetchBoardHistory('b', 999), api: 'getBoardHistory' as const, expectedLimit: 100 },
    { method: 'fetchEntityHistory' as const, call: (s: ReturnType<typeof useAuditStore>) => s.fetchEntityHistory('Card', 'c', 0), api: 'getEntityHistory' as const, expectedLimit: 1 },
    { method: 'fetchEntityHistory' as const, call: (s: ReturnType<typeof useAuditStore>) => s.fetchEntityHistory('Card', 'c', 999), api: 'getEntityHistory' as const, expectedLimit: 100 },
    { method: 'fetchUserHistory' as const, call: (s: ReturnType<typeof useAuditStore>) => s.fetchUserHistory(0), api: 'getUserHistory' as const, expectedLimit: 1 },
    { method: 'fetchUserHistory' as const, call: (s: ReturnType<typeof useAuditStore>) => s.fetchUserHistory(999), api: 'getUserHistory' as const, expectedLimit: 100 },
  ])('$method clamps limit to $expectedLimit', async ({ call, api, expectedLimit }) => {
    const store = useAuditStore()
    vi.mocked(auditApi[api]).mockResolvedValue([])

    await call(store)

    const calls = vi.mocked(auditApi[api]).mock.calls[0]
    expect(calls[calls.length - 1]).toBe(expectedLimit)
  })

  it.each([
    {
      method: 'fetchBoardHistory' as const,
      call: (s: ReturnType<typeof useAuditStore>) => s.fetchBoardHistory('board-1'),
      api: 'getBoardHistory' as const,
      fallback: 'Failed to fetch board history',
    },
    {
      method: 'fetchEntityHistory' as const,
      call: (s: ReturnType<typeof useAuditStore>) => s.fetchEntityHistory('Card', 'c'),
      api: 'getEntityHistory' as const,
      fallback: 'Failed to fetch entity history',
    },
    {
      method: 'fetchUserHistory' as const,
      call: (s: ReturnType<typeof useAuditStore>) => s.fetchUserHistory(),
      api: 'getUserHistory' as const,
      fallback: 'Failed to fetch user history',
    },
  ])('$method preserves entries on error, records error, and shows toast', async ({ call, api }) => {
    const store = useAuditStore()
    store.entries = [
      {
        id: 'existing-audit',
        entityType: 'Board',
        entityId: 'board-1',
        action: 'Created',
        userId: 'user-1',
        userName: 'Alex',
        changes: null,
        timestamp: '2026-03-28T11:00:00Z',
      },
    ]
    vi.mocked(auditApi[api]).mockRejectedValue(new Error('fetch failed'))

    await expect(call(store)).rejects.toBeInstanceOf(Error)

    expect(store.entries).toHaveLength(1)
    expect(store.entries[0].id).toBe('existing-audit')
    expect(store.error).toBe('fetch failed')
    expect(toastMocks.error).toHaveBeenCalledWith('fetch failed')
    expect(store.loading).toBe(false)
  })

  it.each([
    {
      method: 'fetchBoardHistory' as const,
      api: 'getBoardHistory' as const,
      call: (s: ReturnType<typeof useAuditStore>) => s.fetchBoardHistory('board-1'),
    },
    {
      method: 'fetchEntityHistory' as const,
      api: 'getEntityHistory' as const,
      call: (s: ReturnType<typeof useAuditStore>) => s.fetchEntityHistory('Card', 'c'),
    },
    {
      method: 'fetchUserHistory' as const,
      api: 'getUserHistory' as const,
      call: (s: ReturnType<typeof useAuditStore>) => s.fetchUserHistory(),
    },
  ])('$method sets loading true during fetch and false after', async ({ call, api }) => {
    const store = useAuditStore()
    let resolveRequest: ((value: never[]) => void) | null = null

    vi.mocked(auditApi[api]).mockImplementation(() => new Promise((resolve) => {
      resolveRequest = resolve as (value: never[]) => void
    }))

    const fetchPromise = call(store)

    expect(store.loading).toBe(true)

    resolveRequest?.([])
    await fetchPromise

    expect(store.loading).toBe(false)
  })
})

describe('auditStore (demo mode)', () => {
  beforeEach(async () => {
    vi.resetModules()
    vi.doMock('../../utils/demoMode', () => ({ isDemoMode: true }))
    vi.doMock('../../api/auditApi', () => ({
      auditApi: {
        getBoardHistory: vi.fn(),
        getEntityHistory: vi.fn(),
        getUserHistory: vi.fn(),
      },
    }))
    vi.doMock('../../store/toastStore', () => ({
      useToastStore: () => ({ error: vi.fn(), success: vi.fn(), info: vi.fn(), warning: vi.fn() }),
    }))
    setActivePinia(createPinia())
  })

  it('fetchBoardHistory returns empty entries without calling API in demo mode', async () => {
    const { useAuditStore: useDemoStore } = await import('../../store/auditStore')
    const { auditApi: demoApi } = await import('../../api/auditApi')
    const store = useDemoStore()

    await store.fetchBoardHistory('board-1')

    expect(demoApi.getBoardHistory).not.toHaveBeenCalled()
    expect(store.entries).toEqual([])
    expect(store.loading).toBe(false)
  })
})
