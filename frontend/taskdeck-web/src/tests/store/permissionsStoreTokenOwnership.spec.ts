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

function access(id: string): BoardAccess {
  return {
    id,
    boardId: 'board-1',
    userId: 'viewer-1',
    role: 'Viewer',
    grantedBy: 'owner-1',
    grantedAt: '2026-09-21T00:00:00Z',
  }
}

describe('permissionsStore token ownership', () => {
  let session: ReturnType<typeof useSessionStore>
  let store: ReturnType<typeof usePermissionsStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'owner-1'
    session.token = token('old')
    store = usePermissionsStore()
    vi.clearAllMocks()
  })

  it('preserves loaded access while invalidating an old read after same-user token rotation', async () => {
    store.boardAccess.set('board-1', [access('existing')])
    const pending = deferred<BoardAccess[]>()
    vi.mocked(boardAccessApi.getAccess).mockReturnValue(pending.promise)
    const request = store.fetchBoardAccess('board-1')

    session.token = token('new')

    expect(store.boardAccess.get('board-1')?.map(item => item.id)).toEqual(['existing'])
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()

    pending.resolve([access('old-token-read')])
    await request

    expect(store.boardAccess.get('board-1')?.map(item => item.id)).toEqual(['existing'])
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('preserves loaded access while suppressing an old-token mutation failure', async () => {
    store.boardAccess.set('board-1', [access('existing')])
    const pending = deferred<BoardAccess>()
    vi.mocked(boardAccessApi.grantAccess).mockReturnValue(pending.promise)
    const request = store.grantAccess('board-1', { userId: 'viewer-1', role: 'Viewer' })

    session.token = token('new')
    pending.reject(new Error('old credential failure'))
    await expect(request).rejects.toThrow('old credential failure')

    expect(store.boardAccess.get('board-1')?.map(item => item.id)).toEqual(['existing'])
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('reconciles a successful old-token grant after same-user token rotation', async () => {
    const existing = access('existing')
    const granted = access('fresh-grant')
    store.boardAccess.set('board-1', [existing])
    const pendingGrant = deferred<BoardAccess>()
    vi.mocked(boardAccessApi.grantAccess).mockReturnValue(pendingGrant.promise)
    vi.mocked(boardAccessApi.getAccess).mockResolvedValue([existing, granted])

    const request = store.grantAccess('board-1', { userId: 'viewer-1', role: 'Viewer' })
    session.token = token('new')
    pendingGrant.resolve(granted)

    await expect(request).resolves.toEqual(granted)
    expect(boardAccessApi.getAccess).toHaveBeenCalledWith('board-1')
    expect(store.boardAccess.get('board-1')?.map(item => item.id)).toEqual(['existing', 'fresh-grant'])
  })

  it('supersedes an active replacement read after a stale mutation settles', async () => {
    const oldRead = deferred<BoardAccess[]>()
    const replacementRead = deferred<BoardAccess[]>()
    const reconciledRead = deferred<BoardAccess[]>()
    const pendingGrant = deferred<BoardAccess>()
    const granted = access('fresh-grant')
    vi.mocked(boardAccessApi.getAccess)
      .mockReturnValueOnce(oldRead.promise)
      .mockReturnValueOnce(replacementRead.promise)
      .mockReturnValueOnce(reconciledRead.promise)
    vi.mocked(boardAccessApi.grantAccess).mockReturnValue(pendingGrant.promise)

    const readRequest = store.fetchBoardAccess('board-1')
    const mutationRequest = store.grantAccess('board-1', { userId: 'viewer-1', role: 'Viewer' })
    session.token = token('new')

    expect(boardAccessApi.getAccess).toHaveBeenCalledTimes(2)

    pendingGrant.resolve(granted)
    await vi.waitFor(() => {
      expect(boardAccessApi.getAccess).toHaveBeenCalledTimes(3)
    })

    reconciledRead.resolve([granted])
    await expect(mutationRequest).resolves.toEqual(granted)

    replacementRead.resolve([access('stale-replacement-read')])
    oldRead.resolve([access('stale-old-read')])
    await readRequest

    expect(store.boardAccess.get('board-1')?.map(item => item.id)).toEqual(['fresh-grant'])
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('retries an unresolved board-access read after same-user token rotation', async () => {
    const oldRead = deferred<BoardAccess[]>()
    const freshRead = deferred<BoardAccess[]>()
    vi.mocked(boardAccessApi.getAccess)
      .mockReturnValueOnce(oldRead.promise)
      .mockReturnValueOnce(freshRead.promise)

    const request = store.fetchBoardAccess('board-1')
    session.token = token('new')

    expect(boardAccessApi.getAccess).toHaveBeenCalledTimes(2)
    expect(store.boardAccess.has('board-1')).toBe(false)
    expect(store.loading).toBe(true)

    oldRead.resolve([access('old-token-read')])
    await request

    expect(store.boardAccess.has('board-1')).toBe(false)
    expect(store.loading).toBe(true)

    freshRead.resolve([access('fresh-token-read')])
    await vi.waitFor(() => {
      expect(store.boardAccess.get('board-1')?.map(item => item.id)).toEqual(['fresh-token-read'])
      expect(store.loading).toBe(false)
      expect(store.error).toBeNull()
    })
  })
})
