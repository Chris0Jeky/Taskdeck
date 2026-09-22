import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { boardAccessApi } from '../../api/boardAccessApi'
import { usePermissionsStore } from '../../store/permissionsStore'
import { useSessionStore } from '../../store/sessionStore'
import type { BoardAccess } from '../../types/access'

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
  useToastStore: () => ({
    error: vi.fn(),
    success: vi.fn(),
    info: vi.fn(),
    warning: vi.fn(),
  }),
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

function access(id: string, role: BoardAccess['role'] = 'Viewer'): BoardAccess {
  return {
    id,
    boardId: 'board-1',
    userId: `${id}-user`,
    role,
    grantedBy: 'owner-1',
    grantedAt: '2026-09-21T00:00:00Z',
  }
}

async function flushQueue() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('permissionsStore mutation error ownership', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    const session = useSessionStore()
    session.userId = 'owner-1'
    vi.clearAllMocks()
  })

  it('does not erase an independent failure when queued same-entry work starts', async () => {
    const store = usePermissionsStore()
    store.boardAccess.set('board-1', [access('access-1'), access('access-2')])

    const first = deferred<BoardAccess>()
    const independent = deferred<BoardAccess>()
    const queued = deferred<BoardAccess>()
    vi.mocked(boardAccessApi.updateAccess)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(independent.promise)
      .mockReturnValueOnce(queued.promise)

    const firstRequest = store.updateAccess('board-1', 'access-1', { role: 'Editor' })
    const queuedRequest = store.updateAccess('board-1', 'access-1', { role: 'Admin' })
    const independentRequest = store.updateAccess('board-1', 'access-2', { role: 'Admin' })

    independent.reject(new Error('independent access failed'))
    await expect(independentRequest).rejects.toThrow('independent access failed')
    expect(store.error).toBe('independent access failed')

    first.resolve(access('access-1', 'Editor'))
    await firstRequest
    await flushQueue()

    expect(boardAccessApi.updateAccess).toHaveBeenCalledTimes(3)
    expect(store.error).toBe('independent access failed')

    queued.resolve(access('access-1', 'Admin'))
    await queuedRequest
    expect(store.error).toBe('independent access failed')
  })
})
