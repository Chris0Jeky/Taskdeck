import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { auditApi } from '../../api/auditApi'
import { useAuditStore } from '../../store/auditStore'
import { useSessionStore } from '../../store/sessionStore'
import type { AuditEntry } from '../../types/audit'

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

vi.mock('../../api/auditApi', () => ({
  auditApi: {
    getBoardHistory: vi.fn(),
    getEntityHistory: vi.fn(),
    getUserHistory: vi.fn(),
  },
}))

vi.mock('../../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    changePassword: vi.fn(),
    refreshToken: vi.fn(),
    exchangeOAuthCode: vi.fn(),
    exchangeOidcCode: vi.fn(),
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

function entry(id: string): AuditEntry {
  return {
    id,
    entityType: 'Card',
    entityId: id,
    action: 'Updated',
    userId: 'user-a',
    userName: 'Alex',
    changes: null,
    timestamp: '2026-09-21T00:00:00Z',
  }
}

describe('auditStore async ownership', () => {
  let session: ReturnType<typeof useSessionStore>
  let store: ReturnType<typeof useAuditStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'user-a'
    session.token = 'token-a'
    store = useAuditStore()
    vi.clearAllMocks()
  })

  it('keeps the newest cross-kind history query when responses settle in reverse order', async () => {
    const oldBoard = deferred<AuditEntry[]>()
    const newEntity = deferred<AuditEntry[]>()
    vi.mocked(auditApi.getBoardHistory).mockReturnValue(oldBoard.promise)
    vi.mocked(auditApi.getEntityHistory).mockReturnValue(newEntity.promise)

    const oldRequest = store.fetchBoardHistory('board-old')
    const newRequest = store.fetchEntityHistory('Card', 'card-new')

    newEntity.resolve([entry('new-entity')])
    await newRequest
    oldBoard.resolve([entry('old-board')])
    await oldRequest

    expect(store.entries.map(item => item.id)).toEqual(['new-entity'])
    expect(store.error).toBeNull()
  })

  it('suppresses stale failure UI after a newer query succeeds while preserving rejection', async () => {
    const oldBoard = deferred<AuditEntry[]>()
    const newUser = deferred<AuditEntry[]>()
    vi.mocked(auditApi.getBoardHistory).mockReturnValue(oldBoard.promise)
    vi.mocked(auditApi.getUserHistory).mockReturnValue(newUser.promise)

    const oldRequest = store.fetchBoardHistory('board-old')
    const newRequest = store.fetchUserHistory()

    newUser.resolve([entry('new-user')])
    await newRequest
    oldBoard.reject(new Error('stale board failure'))
    await expect(oldRequest).rejects.toThrow('stale board failure')

    expect(store.entries.map(item => item.id)).toEqual(['new-user'])
    expect(store.error).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('does not let an older finally clear loading owned by the current query', async () => {
    const older = deferred<AuditEntry[]>()
    const newer = deferred<AuditEntry[]>()
    vi.mocked(auditApi.getBoardHistory).mockReturnValue(older.promise)
    vi.mocked(auditApi.getUserHistory).mockReturnValue(newer.promise)

    const oldRequest = store.fetchBoardHistory('board-old')
    const newRequest = store.fetchUserHistory()

    older.resolve([entry('old')])
    await oldRequest
    expect(store.loading).toBe(true)

    newer.resolve([entry('new')])
    await newRequest
    expect(store.loading).toBe(false)
  })

  it('reconciles loaded history after a same-user token refresh', async () => {
    store.entries = [entry('existing')]
    store.error = 'existing error'
    const oldRead = deferred<AuditEntry[]>()
    const freshRead = deferred<AuditEntry[]>()
    vi.mocked(auditApi.getBoardHistory)
      .mockReturnValueOnce(oldRead.promise)
      .mockReturnValueOnce(freshRead.promise)

    const request = store.fetchBoardHistory('board-old')
    expect(store.loading).toBe(true)

    session.token = 'token-b'

    expect(auditApi.getBoardHistory).toHaveBeenCalledTimes(2)
    expect(store.entries.map(item => item.id)).toEqual(['existing'])
    expect(store.error).toBeNull()
    expect(store.loading).toBe(true)

    oldRead.resolve([entry('old-token')])
    freshRead.resolve([entry('fresh-token')])
    await request

    expect(store.entries.map(item => item.id)).toEqual(['fresh-token'])
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
  })

  it('lets a completed token-refresh successor settle a stalled retired read', async () => {
    store.entries = [entry('existing')]
    const oldRead = deferred<AuditEntry[]>()
    const freshRead = deferred<AuditEntry[]>()
    vi.mocked(auditApi.getBoardHistory)
      .mockReturnValueOnce(oldRead.promise)
      .mockReturnValueOnce(freshRead.promise)

    const request = store.fetchBoardHistory('board-stalled')
    session.token = 'token-b'

    expect(auditApi.getBoardHistory).toHaveBeenCalledTimes(2)

    freshRead.resolve([entry('fresh-token')])
    await request

    expect(store.entries.map(item => item.id)).toEqual(['fresh-token'])
    expect(store.loading).toBe(false)

    oldRead.resolve([entry('old-token')])
    await Promise.resolve()
    expect(store.entries.map(item => item.id)).toEqual(['fresh-token'])
  })

  it('surfaces a replacement history failure after a same-user token refresh', async () => {
    store.entries = [entry('existing')]
    const oldRead = deferred<AuditEntry[]>()
    const freshRead = deferred<AuditEntry[]>()
    vi.mocked(auditApi.getEntityHistory)
      .mockReturnValueOnce(oldRead.promise)
      .mockReturnValueOnce(freshRead.promise)

    const request = store.fetchEntityHistory('Card', 'card-old')
    session.token = 'token-b'
    oldRead.reject(new Error('old-token failure'))
    freshRead.reject(new Error('replacement failure'))
    await expect(request).rejects.toThrow('replacement failure')

    expect(store.entries.map(item => item.id)).toEqual(['existing'])
    expect(store.error).toBe('replacement failure')
    expect(store.loading).toBe(false)
    expect(toastMocks.error).toHaveBeenCalledTimes(1)
  })

  it('retries an empty initial history read after same-user token rotation', async () => {
    const oldRead = deferred<AuditEntry[]>()
    const freshRead = deferred<AuditEntry[]>()
    vi.mocked(auditApi.getBoardHistory)
      .mockReturnValueOnce(oldRead.promise)
      .mockReturnValueOnce(freshRead.promise)

    const request = store.fetchBoardHistory('board-a')
    session.token = 'token-b'

    expect(auditApi.getBoardHistory).toHaveBeenCalledTimes(2)
    expect(store.entries).toEqual([])
    expect(store.loading).toBe(true)

    oldRead.resolve([entry('old-token')])
    freshRead.resolve([entry('fresh-token')])
    await request

    expect(store.entries.map(item => item.id)).toEqual(['fresh-token'])
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('clears history on identity replacement and suppresses late settlement', async () => {
    store.entries = [entry('existing')]
    const pending = deferred<AuditEntry[]>()
    vi.mocked(auditApi.getUserHistory).mockReturnValue(pending.promise)

    const request = store.fetchUserHistory()
    session.userId = 'user-b'

    expect(store.entries).toEqual([])
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()

    pending.resolve([entry('old-user')])
    await request

    expect(store.entries).toEqual([])
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })
})
