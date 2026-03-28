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

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (error: unknown, fallback: string) => {
    if (error instanceof Error && error.message.trim().length > 0) {
      return { message: error.message, code: null }
    }

    return { message: fallback, code: null }
  },
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

  it('fetchEntityHistory forwards entity filters and clamps the limit', async () => {
    const store = useAuditStore()
    vi.mocked(auditApi.getEntityHistory).mockResolvedValue([])

    await store.fetchEntityHistory('Card', 'card-7', 999)

    expect(auditApi.getEntityHistory).toHaveBeenCalledWith('Card', 'card-7', 100)
  })

  it('fetchUserHistory clamps pagination before calling the API', async () => {
    const store = useAuditStore()
    vi.mocked(auditApi.getUserHistory).mockResolvedValue([])

    await store.fetchUserHistory(0)

    expect(auditApi.getUserHistory).toHaveBeenCalledWith(1)
  })

  it('preserves existing entries when a fetch fails and records the error', async () => {
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
    vi.mocked(auditApi.getUserHistory).mockRejectedValue(new Error('history failed'))

    await expect(store.fetchUserHistory()).rejects.toBeInstanceOf(Error)

    expect(store.entries).toEqual([
      expect.objectContaining({
        id: 'existing-audit',
      }),
    ])
    expect(store.error).toBe('history failed')
    expect(toastMocks.error).toHaveBeenCalledWith('history failed')
  })

  it('sets loading true during fetch and false after completion', async () => {
    const store = useAuditStore()
    let resolveRequest: ((value: []) => void) | null = null

    vi.mocked(auditApi.getBoardHistory).mockImplementation(() => new Promise((resolve) => {
      resolveRequest = resolve as (value: []) => void
    }))

    const fetchPromise = store.fetchBoardHistory('board-1', 25)

    expect(store.loading).toBe(true)

    resolveRequest?.([])
    await fetchPromise

    expect(store.loading).toBe(false)
  })
})
