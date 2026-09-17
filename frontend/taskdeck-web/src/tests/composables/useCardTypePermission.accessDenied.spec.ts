import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { useCardTypePermission } from '../../composables/useCardTypePermission'
import type { Board, BoardDetail } from '../../types/board'

const mocks = vi.hoisted(() => ({
  getBoard: vi.fn(),
  boardStore: {
    currentBoard: null as Board | null,
    currentBoardRequestGeneration: 0,
    currentBoardPayloadGeneration: 0,
  },
  session: { userId: 'user-1' },
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: { getBoard: mocks.getBoard },
}))
vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mocks.boardStore,
}))
vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => mocks.session,
}))
vi.mock('../../utils/demoMode', () => ({ isDemoMode: false }))

function board(overrides: Partial<Board> = {}): Board {
  return {
    id: 'board-1',
    name: 'Board',
    description: null,
    isArchived: false,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  } as Board
}

function mountPermission() {
  let permission!: ReturnType<typeof useCardTypePermission>
  const wrapper = mount(defineComponent({
    setup() {
      permission = useCardTypePermission({
        getBoardId: () => 'board-1',
        getCardId: () => 'card-1',
        getIsOpen: () => true,
        getCardIsArchived: () => false,
      })
      return () => null
    },
  }))

  return { permission, wrapper }
}

describe('useCardTypePermission access-denied classification (GH-3030)', () => {
  beforeEach(() => {
    mocks.getBoard.mockReset()
    mocks.boardStore.currentBoard = board()
    mocks.boardStore.currentBoardRequestGeneration = 0
    mocks.boardStore.currentBoardPayloadGeneration = 0
    mocks.session.userId = 'user-1'
  })

  it('presents a 403 probe as a confirmed permission loss rather than retryable unknown state', async () => {
    mocks.getBoard.mockRejectedValue({ response: { status: 403 } })
    const { permission, wrapper } = mountPermission()

    expect(permission.permissionChecking.value).toBe(true)
    await flushPromises()

    expect(permission.canWrite.value).toBe(false)
    expect(permission.canEditType.value).toBe(false)
    expect(permission.permissionRecovery.value).toBe(true)
    expect(permission.permissionUnknown.value).toBe(false)
    expect(permission.accessUnavailable.value).toBe(true)
    expect(permission.readsBlocked.value).toBe(true)
    expect(mocks.getBoard).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('keeps a transient probe failure in the explicit retry state', async () => {
    mocks.getBoard.mockRejectedValue({ response: { status: 500 } })
    const { permission, wrapper } = mountPermission()
    await flushPromises()

    expect(permission.canWrite.value).toBe(false)
    expect(permission.permissionRecovery.value).toBe(false)
    expect(permission.permissionUnknown.value).toBe(true)
    expect(permission.accessUnavailable.value).toBe(false)
    expect(permission.readsBlocked.value).toBe(false)
    wrapper.unmount()
  })
})
