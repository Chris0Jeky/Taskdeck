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
    userId: 'viewer-1',
    role: 'Viewer',
    grantedBy: 'owner-1',
    grantedAt: '2026-09-21T00:00:00Z',
    ...overrides,
  }
}

async function flushQueue() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('permissionsStore mutation ordering', () => {
  let session: ReturnType<typeof useSessionStore>
  let store: ReturnType<typeof usePermissionsStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'owner-1'
    store = usePermissionsStore()
    store.boardAccess.set('board-1', [access()])
    vi.clearAllMocks()
  })

  it('serializes two role updates for one access row in intent order', async () => {
    const first = deferred<BoardAccess>()
    const second = deferred<BoardAccess>()
    vi.mocked(boardAccessApi.updateAccess)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const firstRequest = store.updateAccess('board-1', 'access-1', { role: 'Editor' })
    const secondRequest = store.updateAccess('board-1', 'access-1', { role: 'Admin' })

    expect(boardAccessApi.updateAccess).toHaveBeenCalledTimes(1)
    first.resolve(access({ role: 'Editor' }))
    await firstRequest
    await flushQueue()

    expect(boardAccessApi.updateAccess).toHaveBeenCalledTimes(2)
    second.resolve(access({ role: 'Admin' }))
    await secondRequest

    expect(store.boardAccess.get('board-1')?.[0].role).toBe('Admin')
    expect(store.error).toBeNull()
  })

  it('continues with the queued update after its predecessor fails', async () => {
    const first = deferred<BoardAccess>()
    const second = deferred<BoardAccess>()
    vi.mocked(boardAccessApi.updateAccess)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const firstRequest = store.updateAccess('board-1', 'access-1', { role: 'Editor' })
    const secondRequest = store.updateAccess('board-1', 'access-1', { role: 'Admin' })

    first.reject(new Error('first failed'))
    await expect(firstRequest).rejects.toThrow('first failed')
    await flushQueue()
    expect(boardAccessApi.updateAccess).toHaveBeenCalledTimes(2)

    second.resolve(access({ role: 'Admin' }))
    await secondRequest
    expect(store.boardAccess.get('board-1')?.[0].role).toBe('Admin')
    expect(store.error).toBeNull()
  })

  it('orders update before revoke and leaves the row removed', async () => {
    const update = deferred<BoardAccess>()
    const revoke = deferred<void>()
    vi.mocked(boardAccessApi.updateAccess).mockReturnValue(update.promise)
    vi.mocked(boardAccessApi.revokeAccess).mockReturnValue(revoke.promise)

    const updateRequest = store.updateAccess('board-1', 'access-1', { role: 'Admin' })
    const revokeRequest = store.revokeAccess('board-1', 'access-1')
    expect(boardAccessApi.revokeAccess).not.toHaveBeenCalled()

    update.resolve(access({ role: 'Admin' }))
    await updateRequest
    await flushQueue()
    expect(boardAccessApi.revokeAccess).toHaveBeenCalledTimes(1)

    revoke.resolve()
    await revokeRequest
    expect(store.boardAccess.get('board-1')).toEqual([])
  })

  it('does not start queued old-session transport after logout', async () => {
    const first = deferred<BoardAccess>()
    vi.mocked(boardAccessApi.updateAccess).mockReturnValue(first.promise)

    const firstRequest = store.updateAccess('board-1', 'access-1', { role: 'Editor' })
    const queuedRequest = store.updateAccess('board-1', 'access-1', { role: 'Admin' })
    session.userId = null
    session.userId = 'owner-1'

    first.resolve(access({ role: 'Editor' }))
    await firstRequest
    await queuedRequest

    expect(boardAccessApi.updateAccess).toHaveBeenCalledTimes(1)
    expect(store.boardAccess.size).toBe(0)
    expect(store.loading).toBe(false)
  })

  it('keeps different access rows concurrent', async () => {
    store.boardAccess.set('board-1', [
      access({ id: 'access-1', userId: 'viewer-1' }),
      access({ id: 'access-2', userId: 'viewer-2' }),
    ])
    const first = deferred<BoardAccess>()
    const second = deferred<BoardAccess>()
    vi.mocked(boardAccessApi.updateAccess)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const firstRequest = store.updateAccess('board-1', 'access-1', { role: 'Editor' })
    const secondRequest = store.updateAccess('board-1', 'access-2', { role: 'Admin' })

    expect(boardAccessApi.updateAccess).toHaveBeenCalledTimes(2)
    first.resolve(access({ id: 'access-1', userId: 'viewer-1', role: 'Editor' }))
    second.resolve(access({ id: 'access-2', userId: 'viewer-2', role: 'Admin' }))
    await Promise.all([firstRequest, secondRequest])

    expect(store.boardAccess.get('board-1')?.map(item => item.role)).toEqual(['Editor', 'Admin'])
  })
})
