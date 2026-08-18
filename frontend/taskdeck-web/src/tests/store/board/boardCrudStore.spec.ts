import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { ref } from 'vue'

const { mockBoardsApi } = vi.hoisted(() => ({
  mockBoardsApi: {
    getBoards: vi.fn(),
    getBoard: vi.fn(),
    createBoard: vi.fn(),
    updateBoard: vi.fn(),
    deleteBoard: vi.fn(),
  },
}))

const { mockCardsApi } = vi.hoisted(() => ({
  mockCardsApi: {
    getCards: vi.fn(),
  },
}))

const { mockLabelsApi } = vi.hoisted(() => ({
  mockLabelsApi: {
    getLabels: vi.fn(),
  },
}))

const { mockDemoData } = vi.hoisted(() => ({
  mockDemoData: {
    buildDemoBoardList: vi.fn(),
    buildDemoBoardDetail: vi.fn(),
  },
}))

vi.mock('../../../api/boardsApi', () => ({
  boardsApi: mockBoardsApi,
}))

vi.mock('../../../api/cardsApi', () => ({
  cardsApi: mockCardsApi,
}))

vi.mock('../../../api/labelsApi', () => ({
  labelsApi: mockLabelsApi,
}))

vi.mock('../../../utils/demoData', () => ({
  buildDemoBoardList: mockDemoData.buildDemoBoardList,
  buildDemoBoardDetail: mockDemoData.buildDemoBoardDetail,
}))

import { createBoardCrudActions } from '../../../store/board/boardCrudStore'

function createMockState() {
  return {
    boards: ref([
      { id: 'board-1', name: 'My Board' },
      { id: 'board-2', name: 'Other' },
    ]),
    currentBoard: ref<{ id: string; name: string } | null>(null),
    currentBoardCards: ref<Array<{ id: string }>>([]),
    currentBoardLabels: ref<Array<{ id: string }>>([]),
    cardCommentsByCardId: ref<Record<string, unknown>>({}),
    boardPresenceMembers: ref<Array<{ id: string }>>([]),
    editingCardId: ref<string | null>(null),
    activeBoardId: ref<string | null>('board-1'),
    loading: ref(false),
    error: ref<string | null>(null),
  }
}

function createMockHelpers(overrides: { isDemoMode?: boolean } = {}) {
  return {
    guardDemoMutation: vi.fn(),
    handleApiError: vi.fn(),
    isDemoMode: overrides.isDemoMode ?? false,
    toast: { success: vi.fn(), error: vi.fn() },
  }
}

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((innerResolve, innerReject) => {
    resolve = innerResolve
    reject = innerReject
  })
  return { promise, resolve, reject }
}

describe('boardCrudStore', () => {
  let state: ReturnType<typeof createMockState>
  let helpers: ReturnType<typeof createMockHelpers>

  beforeEach(() => {
    vi.clearAllMocks()
    state = createMockState()
    helpers = createMockHelpers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  describe('fetchBoards', () => {
    it('fetches and sets boards array', async () => {
      const freshBoards = [
        { id: 'board-a', name: 'Alpha' },
        { id: 'board-b', name: 'Beta' },
      ]
      mockBoardsApi.getBoards.mockResolvedValueOnce(freshBoards)

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      await fetchBoards()

      expect(mockBoardsApi.getBoards).toHaveBeenCalledWith(undefined, false)
      expect(state.boards.value).toEqual(freshBoards)
      expect(state.loading.value).toBe(false)
      expect(state.error.value).toBeNull()
    })

    it('updates activeBoardId to first board if current selection no longer exists', async () => {
      state.activeBoardId.value = 'deleted-board'
      const freshBoards = [
        { id: 'board-x', name: 'X' },
        { id: 'board-y', name: 'Y' },
      ]
      mockBoardsApi.getBoards.mockResolvedValueOnce(freshBoards)

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      await fetchBoards()

      expect(state.activeBoardId.value).toBe('board-x')
    })

    it('preserves activeBoardId if it still exists in the list', async () => {
      state.activeBoardId.value = 'board-2'
      const freshBoards = [
        { id: 'board-1', name: 'My Board' },
        { id: 'board-2', name: 'Other' },
      ]
      mockBoardsApi.getBoards.mockResolvedValueOnce(freshBoards)

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      await fetchBoards()

      expect(state.activeBoardId.value).toBe('board-2')
    })

    it('throttles repeated calls within 5 seconds', async () => {
      vi.useFakeTimers()
      const freshBoards = [{ id: 'board-1', name: 'My Board' }]
      mockBoardsApi.getBoards.mockResolvedValue(freshBoards)

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)

      await fetchBoards()
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(1)

      // Second call within throttle window should be skipped
      await fetchBoards()
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(1)

      // Advance past throttle window
      vi.advanceTimersByTime(5001)
      await fetchBoards()
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(2)
    })

    it('bypasses throttle for filtered requests with search', async () => {
      vi.useFakeTimers()
      const freshBoards = [{ id: 'board-1', name: 'My Board' }]
      mockBoardsApi.getBoards.mockResolvedValue(freshBoards)

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)

      await fetchBoards()
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(1)

      // Filtered request should bypass throttle
      await fetchBoards('search term')
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(2)
      expect(mockBoardsApi.getBoards).toHaveBeenCalledWith('search term', false)
    })

    it('bypasses throttle for includeArchived requests', async () => {
      vi.useFakeTimers()
      const freshBoards = [{ id: 'board-1', name: 'My Board' }]
      mockBoardsApi.getBoards.mockResolvedValue(freshBoards)

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)

      await fetchBoards()
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(1)

      // includeArchived should bypass throttle
      await fetchBoards(undefined, true)
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(2)
      expect(mockBoardsApi.getBoards).toHaveBeenCalledWith(undefined, true)
    })

    it('uses filtered fetches to reset the throttle window', async () => {
      vi.useFakeTimers()
      const freshBoards = [{ id: 'board-1', name: 'My Board' }]
      mockBoardsApi.getBoards.mockResolvedValue(freshBoards)

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)

      await fetchBoards()
      vi.advanceTimersByTime(5001)
      await fetchBoards('search term')
      vi.advanceTimersByTime(1)
      await fetchBoards()

      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(2)
      expect(mockBoardsApi.getBoards).toHaveBeenNthCalledWith(2, 'search term', false)
    })

    it('uses demo data in demo mode', async () => {
      helpers = createMockHelpers({ isDemoMode: true })
      const demoBoards = [{ id: 'demo-1', name: 'Demo Board' }]
      mockDemoData.buildDemoBoardList.mockReturnValue(demoBoards)

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      await fetchBoards()

      expect(mockDemoData.buildDemoBoardList).toHaveBeenCalled()
      expect(state.boards.value).toEqual(demoBoards)
      expect(mockBoardsApi.getBoards).not.toHaveBeenCalled()
      expect(state.loading.value).toBe(false)
    })

    it('handles error and rethrows', async () => {
      mockBoardsApi.getBoards.mockRejectedValueOnce(new Error('network error'))

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      await expect(fetchBoards()).rejects.toThrow('network error')

      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to fetch boards',
      )
      expect(state.loading.value).toBe(false)
    })

    it('sets activeBoardId to null when fetch returns empty list and current is gone', async () => {
      state.activeBoardId.value = 'board-1'
      mockBoardsApi.getBoards.mockResolvedValueOnce([])

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      await fetchBoards()

      expect(state.activeBoardId.value).toBeNull()
    })
  })

  describe('fetchBoard', () => {
    it('sets currentBoard, cards, and labels together', async () => {
      const boardDetail = { id: 'board-1', name: 'My Board', columns: [] }
      const cards = [{ id: 'card-1', columnId: 'column-1' }]
      const labels = [{ id: 'label-1', name: 'Bug' }]
      mockBoardsApi.getBoard.mockResolvedValueOnce(boardDetail)
      mockCardsApi.getCards.mockResolvedValueOnce(cards)
      mockLabelsApi.getLabels.mockResolvedValueOnce(labels)

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const committed = await fetchBoard('board-1')

      expect(mockBoardsApi.getBoard).toHaveBeenCalledWith('board-1')
      expect(state.currentBoard.value).toEqual(boardDetail)
      expect(state.currentBoardCards.value).toEqual(cards)
      expect(state.currentBoardLabels.value).toEqual(labels)
      expect(committed).toBe(true)
      expect(state.loading.value).toBe(false)
    })

    it('clears cardCommentsByCardId', async () => {
      state.cardCommentsByCardId.value = { 'card-1': [{ text: 'hello' }] }
      mockBoardsApi.getBoard.mockResolvedValueOnce({ id: 'board-1', name: 'Test', columns: [] })
      mockCardsApi.getCards.mockResolvedValueOnce([])
      mockLabelsApi.getLabels.mockResolvedValueOnce([])

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      await fetchBoard('board-1')

      expect(state.cardCommentsByCardId.value).toEqual({})
    })

    it('uses demo data in demo mode', async () => {
      helpers = createMockHelpers({ isDemoMode: true })
      const demoDetail = {
        board: { id: 'demo-1', name: 'Demo' },
        cards: [{ id: 'card-1', title: 'Task' }],
      }
      mockDemoData.buildDemoBoardDetail.mockReturnValue(demoDetail)

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const committed = await fetchBoard('demo-1')

      expect(mockDemoData.buildDemoBoardDetail).toHaveBeenCalledWith('demo-1')
      expect(state.currentBoard.value).toEqual(demoDetail.board)
      expect(state.currentBoardCards.value).toEqual(demoDetail.cards)
      expect(state.currentBoardLabels.value).toEqual([])
      expect(state.cardCommentsByCardId.value).toEqual({})
      expect(mockBoardsApi.getBoard).not.toHaveBeenCalled()
      expect(mockCardsApi.getCards).not.toHaveBeenCalled()
      expect(mockLabelsApi.getLabels).not.toHaveBeenCalled()
      expect(committed).toBe(true)
      expect(state.loading.value).toBe(false)
    })

    it('handles error and rethrows', async () => {
      mockBoardsApi.getBoard.mockRejectedValueOnce(new Error('not found'))

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      await expect(fetchBoard('board-1')).rejects.toThrow('not found')

      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to fetch board',
      )
      expect(state.loading.value).toBe(false)
    })

    it('commits only the latest board detail request when an older request resolves last', async () => {
      const boardA = createDeferred<{ id: string; name: string; columns: Array<{ id: string; cardCount: number }> }>()
      const cardsA = createDeferred<Array<{ id: string; columnId: string }>>()
      const labelsA = createDeferred<Array<{ id: string; name: string }>>()
      const boardB = createDeferred<{ id: string; name: string; columns: Array<{ id: string; cardCount: number }> }>()
      const cardsB = createDeferred<Array<{ id: string; columnId: string }>>()
      const labelsB = createDeferred<Array<{ id: string; name: string }>>()
      state.currentBoard.value = { id: 'existing', name: 'Existing' }
      state.currentBoardCards.value = [{ id: 'existing-card' }]
      state.currentBoardLabels.value = [{ id: 'existing-label' }]
      state.cardCommentsByCardId.value = { 'existing-card': [{ text: 'keep until commit' }] }
      mockBoardsApi.getBoard.mockReturnValueOnce(boardA.promise).mockReturnValueOnce(boardB.promise)
      mockCardsApi.getCards.mockReturnValueOnce(cardsA.promise).mockReturnValueOnce(cardsB.promise)
      mockLabelsApi.getLabels.mockReturnValueOnce(labelsA.promise).mockReturnValueOnce(labelsB.promise)

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const first = fetchBoard('board-a')
      const second = fetchBoard('board-b')

      boardB.resolve({ id: 'board-b', name: 'Board B', columns: [{ id: 'column-b', cardCount: 0 }] })
      cardsB.resolve([{ id: 'card-b', columnId: 'column-b' }])
      labelsB.resolve([{ id: 'label-b', name: 'Bug' }])
      await expect(second).resolves.toBe(true)

      boardA.resolve({ id: 'board-a', name: 'Board A', columns: [{ id: 'column-a', cardCount: 0 }] })
      cardsA.resolve([{ id: 'card-a', columnId: 'column-a' }])
      labelsA.resolve([{ id: 'label-a', name: 'Feature' }])
      await expect(first).resolves.toBe(false)

      expect(state.currentBoard.value).toMatchObject({ id: 'board-b' })
      expect(state.currentBoardCards.value).toEqual([{ id: 'card-b', columnId: 'column-b' }])
      expect(state.currentBoardLabels.value).toEqual([{ id: 'label-b', name: 'Bug' }])
      expect(state.cardCommentsByCardId.value).toEqual({})
      expect(state.loading.value).toBe(false)
    })

    it('does not surface a stale board-load error after a newer request commits', async () => {
      const staleBoard = createDeferred<{ id: string; name: string; columns: [] }>()
      mockBoardsApi.getBoard.mockReturnValueOnce(staleBoard.promise).mockResolvedValueOnce({ id: 'board-b', name: 'Board B', columns: [] })
      mockCardsApi.getCards.mockResolvedValue([])
      mockLabelsApi.getLabels.mockResolvedValue([])

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const first = fetchBoard('board-a')
      const second = fetchBoard('board-b')
      await expect(second).resolves.toBe(true)

      staleBoard.reject(new Error('stale failure'))
      await expect(first).resolves.toBe(false)

      expect(helpers.handleApiError).not.toHaveBeenCalled()
      expect(state.error.value).toBeNull()
      expect(state.loading.value).toBe(false)
    })
  })

  describe('createBoard', () => {
    it('pushes new board to boards array and shows toast', async () => {
      const newBoard = { id: 'board-3', name: 'New Board' }
      mockBoardsApi.createBoard.mockResolvedValueOnce(newBoard)

      const { createBoard } = createBoardCrudActions(state as any, helpers as any)
      const result = await createBoard({ name: 'New Board' } as any)

      expect(result).toEqual(newBoard)
      expect(state.boards.value).toHaveLength(3)
      expect(state.boards.value[2]).toEqual(newBoard)
      expect(helpers.toast.success).toHaveBeenCalledWith(
        'Board "New Board" created successfully',
      )
      expect(state.loading.value).toBe(false)
    })

    it('calls guardDemoMutation', async () => {
      mockBoardsApi.createBoard.mockResolvedValueOnce({ id: 'board-3', name: 'X' })

      const { createBoard } = createBoardCrudActions(state as any, helpers as any)
      await createBoard({ name: 'X' } as any)

      expect(helpers.guardDemoMutation).toHaveBeenCalled()
    })

    it('handles error and rethrows', async () => {
      mockBoardsApi.createBoard.mockRejectedValueOnce(new Error('create failed'))

      const { createBoard } = createBoardCrudActions(state as any, helpers as any)
      await expect(createBoard({ name: 'X' } as any)).rejects.toThrow('create failed')

      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to create board',
      )
      expect(state.loading.value).toBe(false)
    })
  })

  describe('updateBoard', () => {
    it('updates board in list by index', async () => {
      const updatedBoard = { id: 'board-1', name: 'Renamed Board' }
      mockBoardsApi.updateBoard.mockResolvedValueOnce(updatedBoard)

      const { updateBoard } = createBoardCrudActions(state as any, helpers as any)
      const result = await updateBoard('board-1', { name: 'Renamed Board' } as any)

      expect(result).toEqual(updatedBoard)
      expect(state.boards.value[0]).toEqual(updatedBoard)
      expect(helpers.toast.success).toHaveBeenCalledWith('Board updated successfully')
      expect(state.loading.value).toBe(false)
    })

    it('also updates currentBoard if it matches', async () => {
      state.currentBoard.value = { id: 'board-1', name: 'My Board' }
      const updatedBoard = { id: 'board-1', name: 'Renamed' }
      mockBoardsApi.updateBoard.mockResolvedValueOnce(updatedBoard)

      const { updateBoard } = createBoardCrudActions(state as any, helpers as any)
      await updateBoard('board-1', { name: 'Renamed' } as any)

      expect(state.currentBoard.value).toEqual(
        expect.objectContaining({ id: 'board-1', name: 'Renamed' }),
      )
    })

    it('does not update currentBoard if it does not match', async () => {
      state.currentBoard.value = { id: 'board-2', name: 'Other' }
      const updatedBoard = { id: 'board-1', name: 'Renamed' }
      mockBoardsApi.updateBoard.mockResolvedValueOnce(updatedBoard)

      const { updateBoard } = createBoardCrudActions(state as any, helpers as any)
      await updateBoard('board-1', { name: 'Renamed' } as any)

      expect(state.currentBoard.value).toEqual({ id: 'board-2', name: 'Other' })
    })

    it('calls guardDemoMutation', async () => {
      mockBoardsApi.updateBoard.mockResolvedValueOnce({ id: 'board-1', name: 'X' })

      const { updateBoard } = createBoardCrudActions(state as any, helpers as any)
      await updateBoard('board-1', { name: 'X' } as any)

      expect(helpers.guardDemoMutation).toHaveBeenCalled()
    })

    it('handles error and rethrows', async () => {
      mockBoardsApi.updateBoard.mockRejectedValueOnce(new Error('update failed'))

      const { updateBoard } = createBoardCrudActions(state as any, helpers as any)
      await expect(updateBoard('board-1', { name: 'X' } as any)).rejects.toThrow('update failed')

      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to update board',
      )
      expect(state.loading.value).toBe(false)
    })
  })

  describe('deleteBoard', () => {
    it('removes board from list', async () => {
      mockBoardsApi.deleteBoard.mockResolvedValueOnce(undefined)

      const { deleteBoard } = createBoardCrudActions(state as any, helpers as any)
      await deleteBoard('board-2')

      expect(state.boards.value).toHaveLength(1)
      expect(state.boards.value[0].id).toBe('board-1')
      expect(helpers.toast.success).toHaveBeenCalledWith('Board archived successfully')
      expect(state.loading.value).toBe(false)
    })

    it('clears detail state if deleted board was current', async () => {
      state.currentBoard.value = { id: 'board-1', name: 'My Board' }
      state.currentBoardCards.value = [{ id: 'card-1' }]
      state.currentBoardLabels.value = [{ id: 'label-1' }]
      state.cardCommentsByCardId.value = { 'card-1': ['comment'] }
      state.boardPresenceMembers.value = [{ id: 'user-1' }]
      state.editingCardId.value = 'card-1'
      mockBoardsApi.deleteBoard.mockResolvedValueOnce(undefined)

      const { deleteBoard } = createBoardCrudActions(state as any, helpers as any)
      await deleteBoard('board-1')

      expect(state.currentBoard.value).toBeNull()
      expect(state.currentBoardCards.value).toEqual([])
      expect(state.currentBoardLabels.value).toEqual([])
      expect(state.cardCommentsByCardId.value).toEqual({})
      expect(state.boardPresenceMembers.value).toEqual([])
      expect(state.editingCardId.value).toBeNull()
    })

    it('does not clear detail state if deleted board was not current', async () => {
      state.currentBoard.value = { id: 'board-1', name: 'My Board' }
      state.currentBoardCards.value = [{ id: 'card-1' }]
      mockBoardsApi.deleteBoard.mockResolvedValueOnce(undefined)

      const { deleteBoard } = createBoardCrudActions(state as any, helpers as any)
      await deleteBoard('board-2')

      expect(state.currentBoard.value).toEqual({ id: 'board-1', name: 'My Board' })
      expect(state.currentBoardCards.value).toEqual([{ id: 'card-1' }])
    })

    it('updates activeBoardId if deleted board was the active selection', async () => {
      state.activeBoardId.value = 'board-1'
      mockBoardsApi.deleteBoard.mockResolvedValueOnce(undefined)

      const { deleteBoard } = createBoardCrudActions(state as any, helpers as any)
      await deleteBoard('board-1')

      // board-1 removed, board-2 remains as first
      expect(state.activeBoardId.value).toBe('board-2')
    })

    it('sets activeBoardId to null if no boards remain after deletion', async () => {
      state.boards.value = [{ id: 'board-1', name: 'Only Board' }]
      state.activeBoardId.value = 'board-1'
      mockBoardsApi.deleteBoard.mockResolvedValueOnce(undefined)

      const { deleteBoard } = createBoardCrudActions(state as any, helpers as any)
      await deleteBoard('board-1')

      expect(state.activeBoardId.value).toBeNull()
      expect(state.boards.value).toHaveLength(0)
    })

    it('calls guardDemoMutation', async () => {
      mockBoardsApi.deleteBoard.mockResolvedValueOnce(undefined)

      const { deleteBoard } = createBoardCrudActions(state as any, helpers as any)
      await deleteBoard('board-2')

      expect(helpers.guardDemoMutation).toHaveBeenCalled()
    })

    it('handles error and rethrows', async () => {
      mockBoardsApi.deleteBoard.mockRejectedValueOnce(new Error('delete failed'))

      const { deleteBoard } = createBoardCrudActions(state as any, helpers as any)
      await expect(deleteBoard('board-1')).rejects.toThrow('delete failed')

      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to archive board',
      )
      expect(state.loading.value).toBe(false)
    })
  })
})
