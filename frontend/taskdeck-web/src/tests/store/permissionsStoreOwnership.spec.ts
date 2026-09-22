import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { boardAccessApi } from '../../api/boardAccessApi'
import { usePermissionsStore } from '../../store/permissionsStore'
import { useSessionStore } from '../../store/sessionStore'
import type { BoardAccess } from '../../types/access'

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../api/boardAccessApi', () => ({
  boardAccessApi: {
    getAccess: vi.fn(),
    grantAccess: vi.fn(),
    updateAccess: vi.fn(),
    revokeAccess: vi.fn(),
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

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}

function access(overrides: Partial<BoardAccess> = {}): BoardAccess {
  return {
    id: 'access-1',
    boardId: 'board-1',
    userId: 'user-1',
    role: 'Owner',
    grantedBy: 'owner-1',
    grantedAt: '2026-09-21T00:00:00Z',
    ...overrides,
  }
}

describe('permissionsStore async ownership', () => {
  let session: ReturnType<typeof useSessionStore>
  let store: ReturnType<typeof usePermissionsStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'owner-1'
    store = usePermissionsStore()
    vi.clearAllMocks()
  })

  it('does not let a read started before revoke reintroduce the revoked entry', async () => {
    const owner = access({ id: 'owner', userId: 'owner-1', role: 'Owner' })
    const viewer = access({ id: 'viewer', userId: 'viewer-1', role: 'Viewer' })
    store.boardAccess.set('board-1', [owner, viewer])
    const read = deferred<BoardAccess[]>()
    vi.mocked(boardAccessApi.getAccess).mockReturnValue(read.promise)
    vi.mocked(boardAccessApi.revokeAccess).mockResolvedValue()

    const readRequest = store.fetchBoardAccess('board-1')
    await store.revokeAccess('board-1', 'viewer')
    expect(store.boardAccess.get('board-1')?.map(item => item.id)).toEqual(['owner'])

    read.resolve([owner, viewer])
    await readRequest

    expect(store.boardAccess.get('board-1')?.map(item => item.id)).toEqual(['owner'])
  })

  it('does not let a read started before grant erase the granted entry', async () => {
    const owner = access({ id: 'owner', userId: 'owner-1', role: 'Owner' })
    const granted = access({ id: 'viewer', userId: 'viewer-1', role: 'Viewer' })
    store.boardAccess.set('board-1', [owner])
    const read = deferred<BoardAccess[]>()
    vi.mocked(boardAccessApi.getAccess).mockReturnValue(read.promise)
    vi.mocked(boardAccessApi.grantAccess).mockResolvedValue(granted)

    const readRequest = store.fetchBoardAccess('board-1')
    await store.grantAccess('board-1', { userId: 'viewer-1', role: 'Viewer' })
    read.resolve([owner])
    await readRequest

    expect(store.boardAccess.get('board-1')?.map(item => item.id)).toEqual(['owner', 'viewer'])
  })

  it('does not duplicate a grant already observed by a newer authoritative read', async () => {
    const owner = access({ id: 'owner', userId: 'owner-1', role: 'Owner' })
    const granted = access({ id: 'viewer', userId: 'viewer-1', role: 'Viewer' })
    store.boardAccess.set('board-1', [owner])
    const grant = deferred<BoardAccess>()
    vi.mocked(boardAccessApi.grantAccess).mockReturnValue(grant.promise)
    vi.mocked(boardAccessApi.getAccess).mockResolvedValue([owner, granted])

    const grantRequest = store.grantAccess('board-1', { userId: 'viewer-1', role: 'Viewer' })
    await store.fetchBoardAccess('board-1')
    grant.resolve(granted)
    await grantRequest

    expect(store.boardAccess.get('board-1')?.map(item => item.id)).toEqual(['owner', 'viewer'])
  })

  it('does not let a read started before a role update restore the old role', async () => {
    const oldEntry = access({ id: 'viewer', userId: 'viewer-1', role: 'Viewer' })
    const updatedEntry = access({ id: 'viewer', userId: 'viewer-1', role: 'Admin' })
    store.boardAccess.set('board-1', [oldEntry])
    const read = deferred<BoardAccess[]>()
    vi.mocked(boardAccessApi.getAccess).mockReturnValue(read.promise)
    vi.mocked(boardAccessApi.updateAccess).mockResolvedValue(updatedEntry)

    const readRequest = store.fetchBoardAccess('board-1')
    await store.updateAccess('board-1', 'viewer', { role: 'Admin' })
    read.resolve([oldEntry])
    await readRequest

    expect(store.boardAccess.get('board-1')?.[0].role).toBe('Admin')
  })

  it('keeps the newest same-board read when responses settle in reverse order', async () => {
    const older = deferred<BoardAccess[]>()
    const newer = deferred<BoardAccess[]>()
    vi.mocked(boardAccessApi.getAccess)
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise)

    const oldRequest = store.fetchBoardAccess('board-1')
    const newRequest = store.fetchBoardAccess('board-1')
    newer.resolve([access({ id: 'new', role: 'Admin' })])
    await newRequest
    older.resolve([access({ id: 'old', role: 'Viewer' })])
    await oldRequest

    expect(store.boardAccess.get('board-1')?.map(item => item.id)).toEqual(['new'])
  })

  it('keeps loading true while independent board reads remain active', async () => {
    const boardOne = deferred<BoardAccess[]>()
    const boardTwo = deferred<BoardAccess[]>()
    vi.mocked(boardAccessApi.getAccess)
      .mockReturnValueOnce(boardOne.promise)
      .mockReturnValueOnce(boardTwo.promise)

    const first = store.fetchBoardAccess('board-1')
    const second = store.fetchBoardAccess('board-2')
    expect(store.loading).toBe(true)

    boardOne.resolve([access({ boardId: 'board-1' })])
    await first
    expect(store.loading).toBe(true)

    boardTwo.resolve([access({ id: 'board-2-owner', boardId: 'board-2' })])
    await second
    expect(store.loading).toBe(false)
  })

  it('invalidates an old read across logout and login as the same user id', async () => {
    const read = deferred<BoardAccess[]>()
    vi.mocked(boardAccessApi.getAccess).mockReturnValue(read.promise)
    const request = store.fetchBoardAccess('board-1')

    session.userId = null
    session.userId = 'owner-1'
    read.resolve([access({ id: 'old-session' })])
    await request

    expect(store.boardAccess.size).toBe(0)
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('does not publish a mutation that settles after the session changes', async () => {
    const pendingGrant = deferred<BoardAccess>()
    vi.mocked(boardAccessApi.grantAccess).mockReturnValue(pendingGrant.promise)
    const request = store.grantAccess('board-1', { userId: 'viewer-1', role: 'Viewer' })

    session.userId = null
    session.userId = 'other-user'
    pendingGrant.resolve(access({ id: 'old-session-grant', userId: 'viewer-1', role: 'Viewer' }))
    await request

    expect(store.boardAccess.size).toBe(0)
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
    expect(toastMocks.success).not.toHaveBeenCalled()
  })

  it('suppresses a stale read failure after a confirmed mutation', async () => {
    const viewer = access({ id: 'viewer', userId: 'viewer-1', role: 'Viewer' })
    store.boardAccess.set('board-1', [viewer])
    const read = deferred<BoardAccess[]>()
    vi.mocked(boardAccessApi.getAccess).mockReturnValue(read.promise)
    vi.mocked(boardAccessApi.revokeAccess).mockResolvedValue()

    const readRequest = store.fetchBoardAccess('board-1')
    await store.revokeAccess('board-1', 'viewer')
    read.reject(new Error('stale failure'))
    await expect(readRequest).rejects.toThrow('stale failure')

    expect(store.error).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })
})
