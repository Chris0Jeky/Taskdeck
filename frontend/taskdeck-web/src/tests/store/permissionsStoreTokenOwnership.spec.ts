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

  it('invalidates an old read when the credential rotates for the same user', async () => {
    const pending = deferred<BoardAccess[]>()
    vi.mocked(boardAccessApi.getAccess).mockReturnValue(pending.promise)
    const request = store.fetchBoardAccess('board-1')

    session.token = token('new')
    pending.resolve([access('old-token-read')])
    await request

    expect(store.boardAccess.size).toBe(0)
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('suppresses a stale mutation failure after credential rotation', async () => {
    const pending = deferred<BoardAccess>()
    vi.mocked(boardAccessApi.grantAccess).mockReturnValue(pending.promise)
    const request = store.grantAccess('board-1', { userId: 'viewer-1', role: 'Viewer' })

    session.token = token('new')
    pending.reject(new Error('old credential failure'))
    await expect(request).rejects.toThrow('old credential failure')

    expect(store.boardAccess.size).toBe(0)
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })
})
