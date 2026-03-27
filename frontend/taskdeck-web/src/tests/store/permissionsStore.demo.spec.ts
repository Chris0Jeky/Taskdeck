import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: true,
  }
})

vi.mock('../../api/boardAccessApi', () => ({
  boardAccessApi: {
    getAccess: vi.fn(),
    grantAccess: vi.fn(),
    updateAccess: vi.fn(),
    revokeAccess: vi.fn(),
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

vi.mock('../../utils/roles', () => ({
  normalizeBoardRole: (role: unknown) => role,
}))

import { usePermissionsStore } from '../../store/permissionsStore'
import { boardAccessApi } from '../../api/boardAccessApi'

describe('permissionsStore demo mode', () => {
  let store: ReturnType<typeof usePermissionsStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    store = usePermissionsStore()
  })

  it('fetchBoardAccess returns empty list without calling API', async () => {
    await store.fetchBoardAccess('board-1')

    expect(store.boardAccess.get('board-1')).toEqual([])
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(boardAccessApi.getAccess).not.toHaveBeenCalled()
  })

  it('grantAccess throws DemoModeError and shows toast', async () => {
    await expect(
      store.grantAccess('board-1', { userId: 'u1', role: 'Editor' } as never),
    ).rejects.toThrow('view-only in demo mode')
    expect(toastMocks.info).toHaveBeenCalledWith('This action is view-only in demo mode.')
    expect(boardAccessApi.grantAccess).not.toHaveBeenCalled()
  })

  it('updateAccess throws DemoModeError and shows toast', async () => {
    await expect(
      store.updateAccess('board-1', 'access-1', { role: 'Viewer' } as never),
    ).rejects.toThrow('view-only in demo mode')
    expect(toastMocks.info).toHaveBeenCalledWith('This action is view-only in demo mode.')
    expect(boardAccessApi.updateAccess).not.toHaveBeenCalled()
  })

  it('revokeAccess throws DemoModeError and shows toast', async () => {
    await expect(store.revokeAccess('board-1', 'access-1')).rejects.toThrow('view-only in demo mode')
    expect(toastMocks.info).toHaveBeenCalledWith('This action is view-only in demo mode.')
    expect(boardAccessApi.revokeAccess).not.toHaveBeenCalled()
  })
})
