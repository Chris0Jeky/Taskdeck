import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, nextTick, reactive, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { useCardTypePermission } from '../../composables/useCardTypePermission'
import { boardsApi } from '../../api/boardsApi'
import type { Board, BoardDetail } from '../../types/board'

vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))

const demo = vi.hoisted(() => ({ enabled: false }))
vi.mock('../../utils/demoMode', () => ({ get isDemoMode() { return demo.enabled } }))

const mockBoardStore = reactive<{ currentBoard: Board | null }>({ currentBoard: null })

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

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

function detail(overrides: Partial<BoardDetail> = {}): BoardDetail {
  return { ...board(), columns: [], ...overrides } as BoardDetail
}

type Harness = ReturnType<typeof useCardTypePermission>

function create(options: { boardId?: string; isOpen?: boolean; cardIsArchived?: boolean } = {}) {
  const boardId = ref(options.boardId ?? 'board-1')
  const isOpen = ref(options.isOpen ?? true)
  const cardIsArchived = ref(options.cardIsArchived ?? false)
  let api!: Harness
  const wrapper = mount(defineComponent({
    setup() {
      api = useCardTypePermission({
        getBoardId: () => boardId.value,
        getIsOpen: () => isOpen.value,
        getCardIsArchived: () => cardIsArchived.value,
      })
      return () => null
    },
  }))
  return { api, wrapper, boardId, isOpen, cardIsArchived }
}

describe('useCardTypePermission', () => {
  beforeEach(() => {
    demo.enabled = false
    mockBoardStore.currentBoard = null
    vi.mocked(boardsApi.getBoard).mockReset()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('gates on a stated permission without asking the server', async () => {
    mockBoardStore.currentBoard = board({ canWrite: true })
    const { api, wrapper } = create()
    await flushPromises()

    expect(api.canEditType.value).toBe(true)
    expect(api.permissionUnknown.value).toBe(false)
    expect(boardsApi.getBoard).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('keeps a viewer read-only without asking the server', async () => {
    mockBoardStore.currentBoard = board({ canWrite: false })
    const { api, wrapper } = create()
    await flushPromises()

    expect(api.canEditType.value).toBe(false)
    expect(api.permissionUnknown.value).toBe(false)
    expect(boardsApi.getBoard).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('reads the board back when the loaded payload omits canWrite, and grants on the server answer', async () => {
    mockBoardStore.currentBoard = board()
    vi.mocked(boardsApi.getBoard).mockResolvedValue(detail({ canWrite: true }))
    const { api, wrapper } = create()

    expect(api.permissionChecking.value).toBe(true)
    expect(api.permissionUnknown.value).toBe(false)
    expect(api.canEditType.value).toBe(false)

    await flushPromises()

    expect(boardsApi.getBoard).toHaveBeenCalledTimes(1)
    // The same bounded read discipline as every other board read: no shared-interceptor
    // retry holding the control in "checking", and a timeout rather than an open request.
    expect(boardsApi.getBoard).toHaveBeenCalledWith('board-1', expect.objectContaining({
      skipRetry: true,
      timeout: 10_000,
      signal: expect.any(AbortSignal),
    }))
    expect(api.canEditType.value).toBe(true)
    expect(api.permissionChecking.value).toBe(false)
    wrapper.unmount()
  })

  it('denies on a server answer of false', async () => {
    mockBoardStore.currentBoard = board()
    vi.mocked(boardsApi.getBoard).mockResolvedValue(detail({ canWrite: false }))
    const { api, wrapper } = create()
    await flushPromises()

    expect(api.canEditType.value).toBe(false)
    expect(api.permissionUnknown.value).toBe(false)
    wrapper.unmount()
  })

  it('applies the legacy only-false convention to a fresh payload that still omits the field', async () => {
    mockBoardStore.currentBoard = board()
    vi.mocked(boardsApi.getBoard).mockResolvedValue(detail())
    const { api, wrapper } = create()
    await flushPromises()

    expect(api.canEditType.value).toBe(true)
    wrapper.unmount()
  })

  it('stays read-only when the server answer says the board is archived', async () => {
    mockBoardStore.currentBoard = board()
    vi.mocked(boardsApi.getBoard).mockResolvedValue(detail({ canWrite: true, isArchived: true }))
    const { api, wrapper } = create()
    await flushPromises()

    expect(api.canEditType.value).toBe(false)
    expect(api.permissionUnknown.value).toBe(false)
    wrapper.unmount()
  })

  it('offers explicit recovery when the read fails, and grants only on a later successful read', async () => {
    mockBoardStore.currentBoard = board()
    vi.mocked(boardsApi.getBoard).mockRejectedValueOnce(new Error('offline'))
    const { api, wrapper } = create()
    await flushPromises()

    expect(api.canEditType.value).toBe(false)
    expect(api.permissionUnknown.value).toBe(true)
    expect(api.permissionChecking.value).toBe(false)
    expect(boardsApi.getBoard).toHaveBeenCalledTimes(1)

    vi.mocked(boardsApi.getBoard).mockResolvedValueOnce(detail({ canWrite: true }))
    await api.refreshPermission()
    await nextTick()

    expect(boardsApi.getBoard).toHaveBeenCalledTimes(2)
    expect(api.canEditType.value).toBe(true)
    expect(api.permissionUnknown.value).toBe(false)
    wrapper.unmount()
  })

  it('does not retry automatically after a failed read', async () => {
    mockBoardStore.currentBoard = board()
    vi.mocked(boardsApi.getBoard).mockRejectedValue(new Error('offline'))
    const { api, wrapper } = create()
    await flushPromises()

    mockBoardStore.currentBoard = board({ name: 'Renamed' })
    await flushPromises()

    expect(boardsApi.getBoard).toHaveBeenCalledTimes(1)
    expect(api.permissionUnknown.value).toBe(true)
    wrapper.unmount()
  })

  it('asks nothing and shows no recovery for an archived card or a board that is not loaded', async () => {
    mockBoardStore.currentBoard = board({ canWrite: true })
    const archived = create({ cardIsArchived: true })
    await flushPromises()

    expect(archived.api.canEditType.value).toBe(false)
    expect(archived.api.permissionUnknown.value).toBe(false)
    archived.wrapper.unmount()

    mockBoardStore.currentBoard = board({ id: 'other-board', canWrite: true })
    const mismatched = create()
    await flushPromises()

    expect(mismatched.api.canEditType.value).toBe(false)
    expect(mismatched.api.permissionUnknown.value).toBe(false)
    expect(boardsApi.getBoard).not.toHaveBeenCalled()
    mismatched.wrapper.unmount()
  })

  it('asks nothing while the editor is closed', async () => {
    mockBoardStore.currentBoard = board()
    const { wrapper, isOpen } = create({ isOpen: false })
    await flushPromises()

    expect(boardsApi.getBoard).not.toHaveBeenCalled()

    vi.mocked(boardsApi.getBoard).mockResolvedValue(detail({ canWrite: true }))
    isOpen.value = true
    await flushPromises()

    expect(boardsApi.getBoard).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('asks nothing in demo mode, which has no server to ask', async () => {
    demo.enabled = true
    mockBoardStore.currentBoard = board()
    const { api, wrapper } = create()
    await flushPromises()

    expect(boardsApi.getBoard).not.toHaveBeenCalled()
    expect(api.canEditType.value).toBe(false)
    expect(api.permissionUnknown.value).toBe(false)
    expect(api.permissionChecking.value).toBe(false)
    wrapper.unmount()
  })

  it('never answers a board with an older read for the SAME board', async () => {
    mockBoardStore.currentBoard = board()
    let resolveFirst!: (value: BoardDetail) => void
    vi.mocked(boardsApi.getBoard).mockImplementationOnce(() => new Promise<BoardDetail>((resolve) => { resolveFirst = resolve }))
    const { api, wrapper, boardId } = create()
    await nextTick()

    // Away to a board that also needs a read, then back: the third read owns board-1's
    // answer, and the first one - whose board id matches it - must not be able to speak.
    mockBoardStore.currentBoard = board({ id: 'board-2' })
    vi.mocked(boardsApi.getBoard).mockResolvedValueOnce(detail({ id: 'board-2', canWrite: false }))
    boardId.value = 'board-2'
    await flushPromises()

    mockBoardStore.currentBoard = board()
    vi.mocked(boardsApi.getBoard).mockResolvedValueOnce(detail({ canWrite: false }))
    boardId.value = 'board-1'
    await flushPromises()

    expect(boardsApi.getBoard).toHaveBeenCalledTimes(3)
    expect(api.canEditType.value).toBe(false)

    // The first read predates a withdrawal of access; answering with it would re-grant it.
    resolveFirst(detail({ canWrite: true }))
    await flushPromises()

    expect(api.canEditType.value).toBe(false)
    expect(api.permissionUnknown.value).toBe(false)
    wrapper.unmount()
  })

  it('abandons a read the editor is no longer waiting for', async () => {
    mockBoardStore.currentBoard = board()
    const abortReasons: unknown[] = []
    vi.mocked(boardsApi.getBoard).mockImplementationOnce((_id, options) => new Promise<BoardDetail>(() => {
      options?.signal?.addEventListener('abort', () => abortReasons.push(true))
    }))
    const { wrapper } = create()
    await nextTick()

    wrapper.unmount()

    expect(abortReasons).toHaveLength(1)
  })

  it('never answers one board with the permission read for another', async () => {
    mockBoardStore.currentBoard = board()
    let resolveFirst!: (value: BoardDetail) => void
    vi.mocked(boardsApi.getBoard).mockImplementationOnce(() => new Promise<BoardDetail>((resolve) => { resolveFirst = resolve }))
    const { api, wrapper, boardId } = create()
    await nextTick()

    mockBoardStore.currentBoard = board({ id: 'board-2' })
    vi.mocked(boardsApi.getBoard).mockResolvedValueOnce(detail({ id: 'board-2', canWrite: false }))
    boardId.value = 'board-2'
    await flushPromises()

    resolveFirst(detail({ canWrite: true }))
    await flushPromises()

    expect(api.canEditType.value).toBe(false)
    // The board-2 answer still stands: a stale response neither grants nor unsettles it.
    expect(api.permissionUnknown.value).toBe(false)
    wrapper.unmount()
  })

  // #3028. `canWrite` is the same resolved answer the whole card editor gates on, so the
  // parent selector, the archive control and the assignment field stop reading an omitted
  // optional field as "no" while the type selector reads the server's actual answer.
  describe('canWrite, the answer every editor gate shares', () => {
    it('resolves once and grants the whole editor on the server answer', async () => {
      mockBoardStore.currentBoard = board()
      vi.mocked(boardsApi.getBoard).mockResolvedValue(detail({ canWrite: true }))
      const { api, wrapper } = create()

      expect(api.canWrite.value).toBe(false)
      await flushPromises()

      expect(api.canWrite.value).toBe(true)
      expect(api.canEditType.value).toBe(true)
      expect(boardsApi.getBoard).toHaveBeenCalledTimes(1)
      wrapper.unmount()
    })

    it('denies the whole editor for a viewer and for an archived board', async () => {
      mockBoardStore.currentBoard = board({ canWrite: false })
      const viewer = create()
      await flushPromises()
      expect(viewer.api.canWrite.value).toBe(false)
      viewer.wrapper.unmount()

      mockBoardStore.currentBoard = board({ canWrite: true, isArchived: true })
      const archivedBoard = create()
      await flushPromises()
      expect(archivedBoard.api.canWrite.value).toBe(false)
      expect(boardsApi.getBoard).not.toHaveBeenCalled()
      archivedBoard.wrapper.unmount()
    })

    it('grants nothing while the read is in flight or after it failed', async () => {
      mockBoardStore.currentBoard = board()
      vi.mocked(boardsApi.getBoard).mockRejectedValueOnce(new Error('offline'))
      const { api, wrapper } = create()

      expect(api.canWrite.value).toBe(false)
      await flushPromises()

      expect(api.canWrite.value).toBe(false)
      expect(api.permissionUnknown.value).toBe(true)

      vi.mocked(boardsApi.getBoard).mockResolvedValueOnce(detail({ canWrite: true }))
      await api.refreshPermission()
      await flushPromises()

      expect(api.canWrite.value).toBe(true)
      wrapper.unmount()
    })

    /*
     * An archived CARD is not a read-only board: Restore is a write the archive control
     * offers on exactly that card. A permission the payload already states must therefore
     * still reach it, while the type selector stays read-only and nothing is asked of the
     * server — the #2952 exclusion that keeps an archived card from spending a request.
     */
    it('keeps a stated permission for an archived card without asking the server', async () => {
      mockBoardStore.currentBoard = board({ canWrite: true })
      const { api, wrapper } = create({ cardIsArchived: true })
      await flushPromises()

      expect(api.canWrite.value).toBe(true)
      expect(api.canEditType.value).toBe(false)
      expect(api.permissionChecking.value).toBe(false)
      expect(api.permissionUnknown.value).toBe(false)
      expect(boardsApi.getBoard).not.toHaveBeenCalled()
      wrapper.unmount()
    })

    it('grants nothing in demo mode, which has no server to ask', async () => {
      demo.enabled = true
      mockBoardStore.currentBoard = board({ canWrite: true })
      const { api, wrapper } = create()
      await flushPromises()

      expect(api.canWrite.value).toBe(false)
      expect(boardsApi.getBoard).not.toHaveBeenCalled()
      wrapper.unmount()
    })
  })
})
