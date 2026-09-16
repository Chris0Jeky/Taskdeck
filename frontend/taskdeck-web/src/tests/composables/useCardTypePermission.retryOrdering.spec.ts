import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, reactive, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { useCardTypePermission } from '../../composables/useCardTypePermission'
import { boardsApi } from '../../api/boardsApi'
import type { Board, BoardDetail } from '../../types/board'

vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => reactive({ userId: 'user-1' }) }))
vi.mock('../../utils/demoMode', () => ({ isDemoMode: false }))

const store = reactive<{
  currentBoard: Board | null
  currentBoardRequestGeneration: number
  currentBoardPayloadGeneration: number
}>({
  currentBoard: null,
  currentBoardRequestGeneration: 0,
  currentBoardPayloadGeneration: 0,
})

vi.mock('../../store/boardStore', () => ({ useBoardStore: () => store }))

function board(canWrite = true): Board {
  return {
    id: 'board-1',
    name: 'Board',
    description: null,
    isArchived: false,
    canWrite,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  } as Board
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

type Harness = ReturnType<typeof useCardTypePermission>

function create() {
  const isOpen = ref(true)
  let api!: Harness
  const wrapper = mount(defineComponent({
    setup() {
      api = useCardTypePermission({
        getBoardId: () => 'board-1',
        getCardId: () => 'card-1',
        getIsOpen: () => isOpen.value,
        getCardIsArchived: () => false,
      })
      return () => null
    },
  }))
  return { api, wrapper }
}

async function enterDeniedRecovery(api: Harness, firstRead: ReturnType<typeof deferred<BoardDetail>>) {
  const recovery = api.recoverFromPermissionDenied()
  await flushPromises()
  expect(api.canWrite.value).toBe(false)
  firstRead.reject({ response: { status: 500 } })
  await recovery
  expect(api.permissionUnknown.value).toBe(true)
}

describe('useCardTypePermission explicit retry ordering', () => {
  beforeEach(() => {
    store.currentBoard = board(true)
    store.currentBoardRequestGeneration = 1
    store.currentBoardPayloadGeneration = 1
    vi.mocked(boardsApi.getBoard).mockReset()
  })

  it.each([403, 404])(
    'does not let an older successful board payload overrule a pending manual retry that returns %s',
    async status => {
      const firstRead = deferred<BoardDetail>()
      const manualRetry = deferred<BoardDetail>()
      vi.mocked(boardsApi.getBoard)
        .mockReturnValueOnce(firstRead.promise)
        .mockReturnValueOnce(manualRetry.promise)
      const { api, wrapper } = create()
      await enterDeniedRecovery(api, firstRead)

      // A board-store read starts after the write denial, then the user starts
      // a later explicit retry. Its response may be fresh relative to the
      // denial, but it is older than the retry and cannot cancel that owner.
      store.currentBoardRequestGeneration = 2
      const retry = api.refreshPermission()
      await flushPromises()
      const retrySignal = vi.mocked(boardsApi.getBoard).mock.calls[1]![1]!.signal!

      store.currentBoard = board(true)
      store.currentBoardPayloadGeneration = 2
      await flushPromises()

      expect(api.canWrite.value).toBe(false)
      expect(retrySignal.aborted).toBe(false)

      manualRetry.reject({ response: { status } })
      await retry

      expect(api.canWrite.value).toBe(false)
      expect(api.accessUnavailable.value).toBe(true)
      expect(api.readsBlocked.value).toBe(true)

      // Only evidence from a request newer than the definitive retry denial
      // may recover the editor.
      store.currentBoardRequestGeneration = 3
      store.currentBoard = board(true)
      store.currentBoardPayloadGeneration = 3
      await flushPromises()
      expect(api.canWrite.value).toBe(true)
      wrapper.unmount()
    },
  )

  it('uses the deferred board payload after the later manual retry fails transiently', async () => {
    const firstRead = deferred<BoardDetail>()
    const manualRetry = deferred<BoardDetail>()
    vi.mocked(boardsApi.getBoard)
      .mockReturnValueOnce(firstRead.promise)
      .mockReturnValueOnce(manualRetry.promise)
    const { api, wrapper } = create()
    await enterDeniedRecovery(api, firstRead)

    store.currentBoardRequestGeneration = 2
    const retry = api.refreshPermission()
    await flushPromises()
    const retrySignal = vi.mocked(boardsApi.getBoard).mock.calls[1]![1]!.signal!

    store.currentBoard = board(true)
    store.currentBoardPayloadGeneration = 2
    await flushPromises()
    expect(api.canWrite.value).toBe(false)
    expect(retrySignal.aborted).toBe(false)

    manualRetry.reject({ response: { status: 500 } })
    await retry

    expect(api.canWrite.value).toBe(true)
    expect(api.accessUnavailable.value).toBe(false)
    expect(api.readsBlocked.value).toBe(false)
    wrapper.unmount()
  })
})
