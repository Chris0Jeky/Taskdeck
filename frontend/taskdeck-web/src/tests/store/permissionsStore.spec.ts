import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { usePermissionsStore } from '../../store/permissionsStore'
import { useSessionStore } from '../../store/sessionStore'
import { boardAccessApi } from '../../api/boardAccessApi'
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

function makeAccess(overrides: Partial<BoardAccess> = {}): BoardAccess {
  return {
    id: 'access-1',
    boardId: 'board-1',
    userId: 'user-1',
    role: 'Editor',
    grantedBy: 'owner-1',
    grantedAt: new Date().toISOString(),
    ...overrides,
  }
}

describe('permissionsStore', () => {
  let store: ReturnType<typeof usePermissionsStore>
  let sessionStore: ReturnType<typeof useSessionStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    store = usePermissionsStore()
    sessionStore = useSessionStore()
    vi.clearAllMocks()
  })

  describe('fetchBoardAccess', () => {
    it('should store access data keyed by boardId', async () => {
      const accessList: BoardAccess[] = [
        makeAccess({ id: 'a1', userId: 'user-1', role: 'Owner' }),
        makeAccess({ id: 'a2', userId: 'user-2', role: 'Viewer' }),
      ]
      vi.mocked(boardAccessApi.getAccess).mockResolvedValue(accessList)

      await store.fetchBoardAccess('board-1')

      expect(boardAccessApi.getAccess).toHaveBeenCalledWith('board-1')
      expect(store.boardAccess.get('board-1')).toEqual(accessList)
      expect(store.loading).toBe(false)
    })

    it('sets error when fetching board access fails', async () => {
      vi.mocked(boardAccessApi.getAccess).mockRejectedValue(new Error('network down'))

      await expect(store.fetchBoardAccess('board-1')).rejects.toBeInstanceOf(Error)

      expect(store.error).toBe('network down')
      expect(store.loading).toBe(false)
    })
  })

  describe('grantAccess', () => {
    it('should add granted access to the existing list', async () => {
      const existing = makeAccess({ id: 'a1', userId: 'user-1', role: 'Owner' })
      store.boardAccess.set('board-1', [existing])
      sessionStore.userId = 'user-1'

      const newAccess = makeAccess({ id: 'a2', userId: 'user-3', role: 'Editor' })
      vi.mocked(boardAccessApi.grantAccess).mockResolvedValue(newAccess)

      await store.grantAccess('board-1', { userId: 'user-3', role: 'Editor' })

      const list = store.boardAccess.get('board-1')!
      expect(list).toHaveLength(2)
      expect(list[1]).toEqual(newAccess)
    })
  })

  describe('updateAccess', () => {
    it('should update an existing entry in the list', async () => {
      const access = makeAccess({ id: 'a1', userId: 'user-2', role: 'Viewer' })
      store.boardAccess.set('board-1', [access])
      sessionStore.userId = 'user-1'

      const updated = makeAccess({ id: 'a1', userId: 'user-2', role: 'Admin' })
      vi.mocked(boardAccessApi.updateAccess).mockResolvedValue(updated)

      await store.updateAccess('board-1', 'a1', { role: 'Admin' })

      const list = store.boardAccess.get('board-1')!
      expect(list[0].role).toBe('Admin')
    })

    it('returns updated access even when local entry is missing', async () => {
      sessionStore.userId = 'user-1'
      store.boardAccess.set('board-1', [makeAccess({ id: 'a1', userId: 'user-2', role: 'Viewer' })])
      const updated = makeAccess({ id: 'a2', userId: 'user-3', role: 'Admin' })
      vi.mocked(boardAccessApi.updateAccess).mockResolvedValue(updated)

      const result = await store.updateAccess('board-1', 'a2', { role: 'Admin' })

      expect(result).toEqual(updated)
      expect(store.boardAccess.get('board-1')).toHaveLength(1)
      expect(store.boardAccess.get('board-1')?.[0].id).toBe('a1')
    })
  })

  describe('revokeAccess', () => {
    it('should remove the entry from the list', async () => {
      const a1 = makeAccess({ id: 'a1', userId: 'user-1', role: 'Owner' })
      const a2 = makeAccess({ id: 'a2', userId: 'user-2', role: 'Viewer' })
      store.boardAccess.set('board-1', [a1, a2])
      sessionStore.userId = 'user-1'

      vi.mocked(boardAccessApi.revokeAccess).mockResolvedValue()

      await store.revokeAccess('board-1', 'a2')

      const list = store.boardAccess.get('board-1')!
      expect(list).toHaveLength(1)
      expect(list[0].id).toBe('a1')
    })
  })

  describe('canEdit', () => {
    it.each([
      ['Owner', true],
      ['Admin', true],
      ['Editor', true],
      ['Viewer', false],
    ] as const)('should return %s for role %s', (role, expected) => {
      sessionStore.userId = 'user-1'
      store.boardAccess.set('board-1', [makeAccess({ userId: 'user-1', role })])

      expect(store.canEdit('board-1')).toBe(expected)
    })

    it('normalizes numeric role values from backend', () => {
      sessionStore.userId = 'user-1'
      store.boardAccess.set('board-1', [makeAccess({ userId: 'user-1', role: 1 })])

      expect(store.canEdit('board-1')).toBe(true)
      expect(store.canAdmin('board-1')).toBe(true)
      expect(store.isOwner('board-1')).toBe(false)
    })
  })

  describe('canAdmin', () => {
    it.each([
      ['Owner', true],
      ['Admin', true],
      ['Editor', false],
      ['Viewer', false],
    ] as const)('should return %s for role %s', (role, expected) => {
      sessionStore.userId = 'user-1'
      store.boardAccess.set('board-1', [makeAccess({ userId: 'user-1', role })])

      expect(store.canAdmin('board-1')).toBe(expected)
    })
  })

  describe('isOwner', () => {
    it.each([
      ['Owner', true],
      ['Admin', false],
      ['Editor', false],
      ['Viewer', false],
    ] as const)('should return %s for role %s', (role, expected) => {
      sessionStore.userId = 'user-1'
      store.boardAccess.set('board-1', [makeAccess({ userId: 'user-1', role })])

      expect(store.isOwner('board-1')).toBe(expected)
    })
  })

  describe('guardrails', () => {
    it('throws if grantAccess is called without a session user', async () => {
      await expect(store.grantAccess('board-1', { userId: 'user-2', role: 'Viewer' }))
        .rejects
        .toThrow('You must be logged in to use board access management.')
      expect(boardAccessApi.grantAccess).not.toHaveBeenCalled()
    })

    it('returns null role checks when no session user exists', () => {
      store.boardAccess.set('board-1', [makeAccess({ userId: 'user-1', role: 'Owner' })])

      expect(store.currentUserRole('board-1')).toBeNull()
      expect(store.canEdit('board-1')).toBe(false)
      expect(store.canAdmin('board-1')).toBe(false)
      expect(store.isOwner('board-1')).toBe(false)
    })
  })
})
