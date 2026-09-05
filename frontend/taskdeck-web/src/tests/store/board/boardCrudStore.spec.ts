import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { ref } from 'vue'
import axios from 'axios'
import { BOARD_REQUEST_TIMEOUT_MS } from '../../../api/http'
import { getErrorMessage } from '../../../utils/errorMessage'

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
import { initialCardFilters, type CardFilters } from '../../../store/board/boardState'

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
    filters: ref<CardFilters>({
      searchText: '',
      labelIds: [],
      dueDateFilter: 'all',
      showBlockedOnly: false,
    }),
  }
}

function createMockHelpers(overrides: { isDemoMode?: boolean } = {}) {
  const boardDetailMutationEpochs = new Map<string, number>()

  return {
    guardDemoMutation: vi.fn(),
    handleApiError: vi.fn(),
    isDemoMode: overrides.isDemoMode ?? false,
    toast: { success: vi.fn(), error: vi.fn() },
    getBoardDetailMutationEpoch: vi.fn(
      (boardId: string) => boardDetailMutationEpochs.get(boardId) ?? 0,
    ),
    markBoardDetailMutation: vi.fn((boardId: string) => {
      boardDetailMutationEpochs.set(boardId, (boardDetailMutationEpochs.get(boardId) ?? 0) + 1)
    }),
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

      expect(mockBoardsApi.getBoards).toHaveBeenCalledWith(undefined, false, expect.anything())
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

    // The composer and the triage table both call fetchBoards() on an empty
    // store when the inbox mounts.  The throttle stamp is only written after a
    // success, so before the share these two mounts issued parallel unfiltered
    // requests and one could fail while the other confirmed an empty account
    // (#1961).
    it('shares one in-flight unfiltered request between concurrent callers', async () => {
      vi.useFakeTimers()
      const inFlight = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoards.mockReturnValueOnce(inFlight.promise)

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      const composerFetch = fetchBoards()
      const tableFetch = fetchBoards()

      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(1)

      inFlight.resolve([{ id: 'board-1', name: 'First Board' }])
      await expect(composerFetch).resolves.toBeUndefined()
      await expect(tableFetch).resolves.toBeUndefined()
      expect(state.boards.value).toEqual([{ id: 'board-1', name: 'First Board' }])

      // The post-success throttle still applies once the shared request settles.
      await fetchBoards()
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(1)
      vi.advanceTimersByTime(5001)
      mockBoardsApi.getBoards.mockResolvedValueOnce([{ id: 'board-2', name: 'Second Board' }])
      await fetchBoards()
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(2)
    })

    it('rejects both concurrent callers once and drops the share so a retry refetches', async () => {
      const inFlight = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoards
        .mockReturnValueOnce(inFlight.promise)
        .mockResolvedValueOnce([{ id: 'board-2', name: 'Recovered Board' }])

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      const composerFetch = fetchBoards()
      const tableFetch = fetchBoards()

      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(1)

      inFlight.reject(new Error('network error'))
      await expect(composerFetch).rejects.toThrow('network error')
      await expect(tableFetch).rejects.toThrow('network error')
      // One real request means one error surface, not one per mounted caller.
      expect(helpers.handleApiError).toHaveBeenCalledTimes(1)

      await expect(fetchBoards()).resolves.toBeUndefined()
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(2)
      expect(state.boards.value).toEqual([{ id: 'board-2', name: 'Recovered Board' }])
    })

    // Every unfiltered caller joins one promise, so an unbounded read pins the
    // whole page rather than one mount.  The bound is the detail read's.
    it('bounds the shared unfiltered list read with a signal, a timeout and no retries', async () => {
      mockBoardsApi.getBoards.mockResolvedValueOnce([])

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      await fetchBoards()

      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(1)
      const readOptions = mockBoardsApi.getBoards.mock.calls[0][2]
      expect(readOptions).toMatchObject({
        timeout: BOARD_REQUEST_TIMEOUT_MS,
        skipRetry: true,
      })
      expect(readOptions.signal).toBeInstanceOf(AbortSignal)
      expect(readOptions.signal.aborted).toBe(false)
    })

    // Same endpoint, same bound: a retry policy that changed with a query
    // parameter would be a trap for the next caller of this action.
    it('bounds a filtered list read the same way as the shared one', async () => {
      mockBoardsApi.getBoards.mockResolvedValueOnce([])

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      await fetchBoards('urgent')

      expect(mockBoardsApi.getBoards).toHaveBeenCalledWith('urgent', false, expect.anything())
      const readOptions = mockBoardsApi.getBoards.mock.calls[0][2]
      expect(readOptions).toMatchObject({
        timeout: BOARD_REQUEST_TIMEOUT_MS,
        skipRetry: true,
      })
      expect(readOptions.signal).toBeInstanceOf(AbortSignal)
    })

    it('surfaces a timed-out shared read once, releases the share, and lets the next caller refetch', async () => {
      const inFlight = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoards
        .mockReturnValueOnce(inFlight.promise)
        .mockResolvedValueOnce([{ id: 'board-2', name: 'Recovered Board' }])

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      const composerFetch = fetchBoards()
      const tableFetch = fetchBoards()

      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(1)
      // The timeout is only reachable because the read carries one.
      expect(mockBoardsApi.getBoards.mock.calls[0][2]).toMatchObject({
        timeout: BOARD_REQUEST_TIMEOUT_MS,
        skipRetry: true,
      })

      // What axios raises when `timeout` elapses: a terminal error, not a cancel.
      const timedOut = Object.assign(new Error('timeout of 10000ms exceeded'), {
        code: 'ECONNABORTED',
      })
      inFlight.reject(timedOut)
      await expect(composerFetch).rejects.toThrow('timeout of 10000ms exceeded')
      await expect(tableFetch).rejects.toThrow('timeout of 10000ms exceeded')

      // One bounded attempt, one error surface, and the skeleton comes down so
      // BoardsListView can render the error instead of spinning.
      expect(helpers.handleApiError).toHaveBeenCalledTimes(1)
      expect(state.loading.value).toBe(false)

      // No throttle stamp is written on failure, so the retry is immediate.
      await expect(fetchBoards()).resolves.toBeUndefined()
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(2)
      expect(state.boards.value).toEqual([{ id: 'board-2', name: 'Recovered Board' }])
    })

    it('issues a separate request for a filtered caller during an unfiltered flight', async () => {
      const inFlight = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoards
        .mockReturnValueOnce(inFlight.promise)
        .mockResolvedValueOnce([{ id: 'board-search', name: 'Searched' }])

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      const unfilteredFetch = fetchBoards()
      await fetchBoards('search term')

      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(2)
      expect(mockBoardsApi.getBoards).toHaveBeenNthCalledWith(2, 'search term', false, expect.anything())

      inFlight.resolve([{ id: 'board-1', name: 'First Board' }])
      await expect(unfilteredFetch).resolves.toBeUndefined()
    })

    it('never lets an unfiltered caller join a filtered request in flight', async () => {
      const inFlight = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoards
        .mockReturnValueOnce(inFlight.promise)
        .mockResolvedValueOnce([{ id: 'board-1', name: 'First Board' }])

      const { fetchBoards } = createBoardCrudActions(state as any, helpers as any)
      const filteredFetch = fetchBoards(undefined, true)
      await fetchBoards()

      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(2)
      expect(mockBoardsApi.getBoards).toHaveBeenNthCalledWith(1, undefined, true, expect.anything())
      expect(mockBoardsApi.getBoards).toHaveBeenNthCalledWith(2, undefined, false, expect.anything())

      inFlight.resolve([{ id: 'board-archived', name: 'Archived' }])
      await expect(filteredFetch).resolves.toBeUndefined()
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
      expect(mockBoardsApi.getBoards).toHaveBeenCalledWith('search term', false, expect.anything())
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
      expect(mockBoardsApi.getBoards).toHaveBeenCalledWith(undefined, true, expect.anything())
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
      expect(mockBoardsApi.getBoards).toHaveBeenNthCalledWith(2, 'search term', false, expect.anything())
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

      expect(mockBoardsApi.getBoard).toHaveBeenCalledWith(
        'board-1',
        expect.objectContaining({
          signal: expect.any(AbortSignal),
          timeout: BOARD_REQUEST_TIMEOUT_MS,
          skipRetry: true,
        }),
      )
      expect(mockCardsApi.getCards).toHaveBeenCalledWith(
        'board-1',
        undefined,
        expect.objectContaining({
          signal: expect.any(AbortSignal),
          timeout: BOARD_REQUEST_TIMEOUT_MS,
          skipRetry: true,
        }),
      )
      expect(mockLabelsApi.getLabels).toHaveBeenCalledWith(
        'board-1',
        expect.objectContaining({
          signal: expect.any(AbortSignal),
          timeout: BOARD_REQUEST_TIMEOUT_MS,
          skipRetry: true,
        }),
      )
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
      state.currentBoard.value = { id: 'existing', name: 'Existing' }
      state.currentBoardCards.value = [{ id: 'existing-card' }]
      state.currentBoardLabels.value = [{ id: 'existing-label' }]
      mockBoardsApi.getBoard.mockRejectedValueOnce(new Error('not found'))

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      await expect(fetchBoard('board-1')).rejects.toThrow('not found')

      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to fetch board',
      )
      expect(state.currentBoard.value).toEqual({ id: 'existing', name: 'Existing' })
      expect(state.currentBoardCards.value).toEqual([{ id: 'existing-card' }])
      expect(state.currentBoardLabels.value).toEqual([{ id: 'existing-label' }])
      expect(state.loading.value).toBe(false)
    })

    it('aborts still-pending siblings when one board read fails', async () => {
      let cardsAborted = false
      let labelsAborted = false
      const abortable = (onAbort: () => void) =>
        (_id: string, _params: unknown, options: { signal: AbortSignal }) =>
          new Promise<never>((_resolve, reject) => {
            options.signal.addEventListener('abort', () => {
              onAbort()
              reject(new axios.CanceledError('sibling aborted'))
            })
          })

      mockBoardsApi.getBoard.mockRejectedValueOnce(new Error('board failed'))
      mockCardsApi.getCards.mockImplementationOnce(abortable(() => (cardsAborted = true)))
      mockLabelsApi.getLabels.mockImplementationOnce(
        (_id: string, options: { signal: AbortSignal }) =>
          new Promise<never>((_resolve, reject) => {
            options.signal.addEventListener('abort', () => {
              labelsAborted = true
              reject(new axios.CanceledError('sibling aborted'))
            })
          }),
      )

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      await expect(fetchBoard('board-1')).rejects.toThrow('board failed')

      expect(cardsAborted).toBe(true)
      expect(labelsAborted).toBe(true)
      expect(state.loading.value).toBe(false)
    })

    it('aborts the previous generation before starting a new board read', async () => {
      let oldCardsAborted = false
      let oldLabelsAborted = false
      let boardCall = 0
      mockBoardsApi.getBoard.mockImplementation((_id: string, options: { signal: AbortSignal }) => {
        boardCall += 1
        if (boardCall === 1) {
          return new Promise((_resolve, reject) => {
            options.signal.addEventListener('abort', () => reject(new axios.CanceledError('stale board')))
          })
        }
        return Promise.resolve({ id: 'board-2', name: 'Board 2', columns: [] })
      })
      mockCardsApi.getCards.mockImplementation((_id: string, _params: unknown, options: { signal: AbortSignal }) => {
        if (boardCall === 1) {
          return new Promise((_resolve, reject) => {
            options.signal.addEventListener('abort', () => {
              oldCardsAborted = true
              reject(new axios.CanceledError('stale cards'))
            })
          })
        }
        return Promise.resolve([])
      })
      mockLabelsApi.getLabels.mockImplementation((_id: string, options: { signal: AbortSignal }) => {
        if (boardCall === 1) {
          return new Promise((_resolve, reject) => {
            options.signal.addEventListener('abort', () => {
              oldLabelsAborted = true
              reject(new axios.CanceledError('stale labels'))
            })
          })
        }
        return Promise.resolve([])
      })

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const first = fetchBoard('board-1')
      const second = fetchBoard('board-2')

      await expect(second).resolves.toBe(true)
      await expect(first).resolves.toBe(false)
      expect(oldCardsAborted).toBe(true)
      expect(oldLabelsAborted).toBe(true)
      expect(state.currentBoard.value).toMatchObject({ id: 'board-2' })
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

    it('queues and coalesces background refreshes behind an explicit board load', async () => {
      const explicitBoard = createDeferred<{ id: string; name: string; columns: Array<{ id: string; cardCount: number }> }>()
      const explicitCards = createDeferred<Array<{ id: string; columnId: string }>>()
      const explicitLabels = createDeferred<Array<{ id: string; name: string }>>()
      const backgroundBoard = createDeferred<{ id: string; name: string; columns: Array<{ id: string; cardCount: number }> }>()
      const backgroundCards = createDeferred<Array<{ id: string; columnId: string }>>()
      const backgroundLabels = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoard
        .mockReturnValueOnce(explicitBoard.promise)
        .mockReturnValueOnce(backgroundBoard.promise)
      mockCardsApi.getCards
        .mockReturnValueOnce(explicitCards.promise)
        .mockReturnValueOnce(backgroundCards.promise)
      mockLabelsApi.getLabels
        .mockReturnValueOnce(explicitLabels.promise)
        .mockReturnValueOnce(backgroundLabels.promise)

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const explicit = fetchBoard('board-1')
      const queuedFirst = fetchBoard('board-1', { intent: 'background' })
      const queuedSecond = fetchBoard('board-1', { intent: 'background' })

      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(1)
      expect(mockCardsApi.getCards).toHaveBeenCalledTimes(1)
      expect(mockLabelsApi.getLabels).toHaveBeenCalledTimes(1)

      explicitBoard.resolve({
        id: 'board-1',
        name: 'Recovered board',
        columns: [{ id: 'column-1', cardCount: 0 }],
      })
      explicitCards.resolve([{ id: 'card-explicit', columnId: 'column-1' }])
      explicitLabels.resolve([{ id: 'label-explicit', name: 'Explicit' }])
      await expect(explicit).resolves.toBe(true)

      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)
      expect(mockCardsApi.getCards).toHaveBeenCalledTimes(2)
      expect(mockLabelsApi.getLabels).toHaveBeenCalledTimes(2)

      backgroundBoard.resolve({
        id: 'board-1',
        name: 'Realtime board',
        columns: [{ id: 'column-1', cardCount: 0 }],
      })
      backgroundCards.resolve([{ id: 'card-background', columnId: 'column-1' }])
      backgroundLabels.resolve([{ id: 'label-background', name: 'Background' }])

      await expect(queuedFirst).resolves.toBe(true)
      await expect(queuedSecond).resolves.toBe(true)
      expect(state.currentBoard.value).toMatchObject({ name: 'Realtime board' })
      expect(state.currentBoardCards.value).toEqual([
        { id: 'card-background', columnId: 'column-1' },
      ])
    })

    it('keeps a recovered board and clean error state when its queued background refresh fails', async () => {
      const explicitBoard = createDeferred<{ id: string; name: string; columns: Array<{ id: string; cardCount: number }> }>()
      const explicitCards = createDeferred<Array<{ id: string; columnId: string }>>()
      const explicitLabels = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoard
        .mockReturnValueOnce(explicitBoard.promise)
        .mockRejectedValueOnce(new Error('background unavailable'))
      mockCardsApi.getCards
        .mockReturnValueOnce(explicitCards.promise)
        .mockResolvedValueOnce([])
      mockLabelsApi.getLabels
        .mockReturnValueOnce(explicitLabels.promise)
        .mockResolvedValueOnce([])
      state.error.value = 'Previous board load failed'

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const explicit = fetchBoard('board-1')
      const queued = fetchBoard('board-1', { intent: 'background' })

      explicitBoard.resolve({ id: 'board-1', name: 'Recovered board', columns: [] })
      explicitCards.resolve([{ id: 'card-recovered', columnId: 'column-1' }])
      explicitLabels.resolve([{ id: 'label-recovered', name: 'Recovered' }])

      await expect(explicit).resolves.toBe(true)
      await expect(queued).resolves.toBe(false)
      expect(state.currentBoard.value).toMatchObject({ name: 'Recovered board' })
      expect(state.currentBoardCards.value).toEqual([
        { id: 'card-recovered', columnId: 'column-1' },
      ])
      expect(state.error.value).toBeNull()
      expect(helpers.handleApiError).not.toHaveBeenCalled()
    })

    it('surfaces a current background 403 without replacing cached board state', async () => {
      const forbidden = {
        message: 'Request failed with status code 403',
        response: { status: 403 },
      }
      state.currentBoard.value = { id: 'board-1', name: 'Cached board' }
      state.currentBoardCards.value = [{ id: 'cached-card' }]
      state.currentBoardLabels.value = [{ id: 'cached-label' }]
      state.cardCommentsByCardId.value = { 'cached-card': [{ text: 'Cached comment' }] }
      mockBoardsApi.getBoard.mockRejectedValueOnce(forbidden)
      mockCardsApi.getCards.mockResolvedValueOnce([])
      mockLabelsApi.getLabels.mockResolvedValueOnce([])
      helpers.handleApiError.mockImplementationOnce((error: unknown, fallback: string) => {
        state.error.value = getErrorMessage(error, fallback)
      })

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      await expect(fetchBoard('board-1', { intent: 'background' })).resolves.toBe(false)

      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.objectContaining({ message: 'You no longer have access to this board' }),
        'You no longer have access to this board',
      )
      expect(state.error.value).toBe('You no longer have access to this board')
      expect(state.currentBoard.value).toEqual({ id: 'board-1', name: 'Cached board' })
      expect(state.currentBoardCards.value).toEqual([{ id: 'cached-card' }])
      expect(state.currentBoardLabels.value).toEqual([{ id: 'cached-label' }])
      expect(state.cardCommentsByCardId.value).toEqual({
        'cached-card': [{ text: 'Cached comment' }],
      })
    })

    it('suppresses a background 403 after a newer generation commits', async () => {
      const staleBoard = createDeferred<{ id: string; name: string; columns: [] }>()
      const forbidden = {
        message: 'Request failed with status code 403',
        response: { status: 403 },
      }
      mockBoardsApi.getBoard
        .mockReturnValueOnce(staleBoard.promise)
        .mockResolvedValueOnce({ id: 'board-1', name: 'Current board', columns: [] })
      mockCardsApi.getCards.mockResolvedValue([])
      mockLabelsApi.getLabels.mockResolvedValue([])

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const stale = fetchBoard('board-1', { intent: 'background' })
      const current = fetchBoard('board-1')

      await expect(current).resolves.toBe(true)
      staleBoard.reject(forbidden)
      await expect(stale).resolves.toBe(false)

      expect(helpers.handleApiError).not.toHaveBeenCalled()
      expect(state.currentBoard.value).toMatchObject({ name: 'Current board' })
    })

    it('discards a queued background refresh when an explicit route load changes boards', async () => {
      const boardA = createDeferred<{ id: string; name: string; columns: [] }>()
      const cardsA = createDeferred<Array<{ id: string; columnId: string }>>()
      const labelsA = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoard
        .mockReturnValueOnce(boardA.promise)
        .mockResolvedValueOnce({ id: 'board-b', name: 'Board B', columns: [] })
      mockCardsApi.getCards
        .mockReturnValueOnce(cardsA.promise)
        .mockResolvedValueOnce([])
      mockLabelsApi.getLabels
        .mockReturnValueOnce(labelsA.promise)
        .mockResolvedValueOnce([])

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const first = fetchBoard('board-a')
      const queued = fetchBoard('board-a', { intent: 'background' })

      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(1)

      const next = fetchBoard('board-b')
      await expect(next).resolves.toBe(true)
      await expect(queued).resolves.toBe(false)

      boardA.resolve({ id: 'board-a', name: 'Board A', columns: [] })
      cardsA.resolve([])
      labelsA.resolve([])
      await expect(first).resolves.toBe(false)

      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)
      expect(state.currentBoard.value).toMatchObject({ id: 'board-b' })
    })

    it('discards queued background work when the board view unmounts', async () => {
      const explicitBoard = createDeferred<{ id: string; name: string; columns: [] }>()
      const explicitCards = createDeferred<Array<{ id: string; columnId: string }>>()
      const explicitLabels = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoard.mockReturnValueOnce(explicitBoard.promise)
      mockCardsApi.getCards.mockReturnValueOnce(explicitCards.promise)
      mockLabelsApi.getLabels.mockReturnValueOnce(explicitLabels.promise)

      const { fetchBoard, cancelBackgroundBoardFetch } = createBoardCrudActions(
        state as any,
        helpers as any,
      )
      const explicit = fetchBoard('board-1')
      const queued = fetchBoard('board-1', { intent: 'background' })

      cancelBackgroundBoardFetch('board-1')
      explicitBoard.resolve({ id: 'board-1', name: 'Recovered board', columns: [] })
      explicitCards.resolve([])
      explicitLabels.resolve([])

      await expect(explicit).resolves.toBe(true)
      await expect(queued).resolves.toBe(false)
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(1)
    })

    it('does not let an explicit retry overwrite a board write that completed mid-flight', async () => {
      const retryBoard = createDeferred<{ id: string; name: string; columns: [] }>()
      const retryCards = createDeferred<Array<{ id: string; columnId: string }>>()
      const retryLabels = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoard.mockReturnValueOnce(retryBoard.promise)
      mockCardsApi.getCards.mockReturnValueOnce(retryCards.promise)
      mockLabelsApi.getLabels.mockReturnValueOnce(retryLabels.promise)
      state.currentBoard.value = { id: 'board-1', name: 'Renamed board' }
      state.currentBoardLabels.value = [{ id: 'label-new' }]

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const retry = fetchBoard('board-1')

      // The board-settings/label save resolves while the retry fan-out is still open.
      helpers.markBoardDetailMutation('board-1')

      retryBoard.resolve({ id: 'board-1', name: 'Pre-save name', columns: [] })
      retryCards.resolve([])
      retryLabels.resolve([{ id: 'label-stale', name: 'Pre-save label' }])

      await expect(retry).resolves.toBe(false)
      expect(state.currentBoard.value).toEqual({ id: 'board-1', name: 'Renamed board' })
      expect(state.currentBoardLabels.value).toEqual([{ id: 'label-new' }])
      expect(state.loading.value).toBe(false)
      // An explicit read carries the view's own error surface, so it does not
      // queue a successor of its own.
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(1)
    })

    it('queues one successor read when a local mutation invalidates a background refresh', async () => {
      const staleBoard = createDeferred<{
        id: string
        name: string
        columns: Array<{ id: string; cardCount: number }>
      }>()
      const staleCards = createDeferred<Array<{ id: string; columnId: string; position: number }>>()
      const staleLabels = createDeferred<Array<{ id: string; name: string }>>()
      const successorBoard = createDeferred<{
        id: string
        name: string
        columns: Array<{ id: string; cardCount: number }>
      }>()
      const successorCards =
        createDeferred<Array<{ id: string; columnId: string; position: number }>>()
      const successorLabels = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoard
        .mockReturnValueOnce(staleBoard.promise)
        .mockReturnValueOnce(successorBoard.promise)
      mockCardsApi.getCards
        .mockReturnValueOnce(staleCards.promise)
        .mockReturnValueOnce(successorCards.promise)
      mockLabelsApi.getLabels
        .mockReturnValueOnce(staleLabels.promise)
        .mockReturnValueOnce(successorLabels.promise)

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const background = fetchBoard('board-1', { intent: 'background' })

      // A local card move completes while the refresh is open, so the server
      // reindexed siblings this payload cannot describe.
      helpers.markBoardDetailMutation('board-1')

      staleBoard.resolve({
        id: 'board-1',
        name: 'Stale board',
        columns: [{ id: 'column-1', cardCount: 0 }],
      })
      staleCards.resolve([{ id: 'card-1', columnId: 'column-1', position: 9 }])
      staleLabels.resolve([])

      await expect(background).resolves.toBe(false)
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)

      // The successor is the only in-flight read, so a further background
      // request joins it instead of starting a parallel fan-out.
      const successor = fetchBoard('board-1', { intent: 'background' })
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)

      successorBoard.resolve({
        id: 'board-1',
        name: 'Reordered board',
        columns: [{ id: 'column-1', cardCount: 0 }],
      })
      successorCards.resolve([{ id: 'card-1', columnId: 'column-1', position: 0 }])
      successorLabels.resolve([])
      await expect(successor).resolves.toBe(true)

      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)
      expect(state.currentBoard.value).toMatchObject({ name: 'Reordered board' })
      expect(state.currentBoardCards.value).toEqual([
        { id: 'card-1', columnId: 'column-1', position: 0 },
      ])
    })

    it('produces exactly one successor read and no parallel fan-out for repeated mutation events during one background read', async () => {
      const staleBoard = createDeferred<{ id: string; name: string; columns: [] }>()
      const staleCards = createDeferred<Array<{ id: string; columnId: string }>>()
      const staleLabels = createDeferred<Array<{ id: string; name: string }>>()
      const successorBoard = createDeferred<{ id: string; name: string; columns: [] }>()
      const successorCards = createDeferred<Array<{ id: string; columnId: string }>>()
      const successorLabels = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoard
        .mockReturnValueOnce(staleBoard.promise)
        .mockReturnValueOnce(successorBoard.promise)
      mockCardsApi.getCards
        .mockReturnValueOnce(staleCards.promise)
        .mockReturnValueOnce(successorCards.promise)
      mockLabelsApi.getLabels
        .mockReturnValueOnce(staleLabels.promise)
        .mockReturnValueOnce(successorLabels.promise)

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const background = fetchBoard('board-1', { intent: 'background' })

      helpers.markBoardDetailMutation('board-1')
      const duringWindowFirst = fetchBoard('board-1', { intent: 'background' })
      helpers.markBoardDetailMutation('board-1')
      const duringWindowSecond = fetchBoard('board-1', { intent: 'background' })
      helpers.markBoardDetailMutation('board-1')

      // Bound, part one: a background request that arrives while this read is
      // open joins its promise instead of starting a fan-out.
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(1)

      staleBoard.resolve({ id: 'board-1', name: 'Stale board', columns: [] })
      staleCards.resolve([])
      staleLabels.resolve([])

      await expect(background).resolves.toBe(false)
      await expect(duringWindowFirst).resolves.toBe(false)
      await expect(duringWindowSecond).resolves.toBe(false)

      // Bound, part two: the successor check runs once per read, so all three
      // invalidating events produced a single successor.
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)
      const successor = fetchBoard('board-1', { intent: 'background' })
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)

      successorBoard.resolve({ id: 'board-1', name: 'Repaired board', columns: [] })
      successorCards.resolve([])
      successorLabels.resolve([])
      await expect(successor).resolves.toBe(true)
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)
      expect(state.currentBoard.value).toMatchObject({ name: 'Repaired board' })
    })

    it('discards an invalidation successor read when an explicit route load changes boards', async () => {
      const staleBoard = createDeferred<{ id: string; name: string; columns: [] }>()
      mockBoardsApi.getBoard
        .mockReturnValueOnce(staleBoard.promise)
        .mockImplementationOnce(
          (_id: string, options: { signal: AbortSignal }) =>
            new Promise((_resolve, reject) => {
              options.signal.addEventListener('abort', () =>
                reject(new axios.CanceledError('successor discarded')),
              )
            }),
        )
        .mockResolvedValueOnce({ id: 'board-b', name: 'Board B', columns: [] })
      mockCardsApi.getCards.mockResolvedValue([])
      mockLabelsApi.getLabels.mockResolvedValue([])
      state.currentBoard.value = { id: 'board-1', name: 'Local board' }

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const background = fetchBoard('board-1', { intent: 'background' })
      helpers.markBoardDetailMutation('board-1')
      staleBoard.resolve({ id: 'board-1', name: 'Stale board', columns: [] })

      await expect(background).resolves.toBe(false)
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)
      const successor = fetchBoard('board-1', { intent: 'background' })
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)

      const routeLoad = fetchBoard('board-b')
      await expect(routeLoad).resolves.toBe(true)
      await expect(successor).resolves.toBe(false)

      expect(state.currentBoard.value).toMatchObject({ id: 'board-b' })
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(3)
      expect(helpers.handleApiError).not.toHaveBeenCalled()
    })

    it('discards an invalidation successor read when the board view unmounts', async () => {
      const staleBoard = createDeferred<{ id: string; name: string; columns: [] }>()
      mockBoardsApi.getBoard
        .mockReturnValueOnce(staleBoard.promise)
        .mockImplementationOnce(
          (_id: string, options: { signal: AbortSignal }) =>
            new Promise((_resolve, reject) => {
              options.signal.addEventListener('abort', () =>
                reject(new axios.CanceledError('successor discarded')),
              )
            }),
        )
      mockCardsApi.getCards.mockResolvedValue([])
      mockLabelsApi.getLabels.mockResolvedValue([])
      state.currentBoard.value = { id: 'board-1', name: 'Local board' }

      const { fetchBoard, cancelBackgroundBoardFetch } = createBoardCrudActions(
        state as any,
        helpers as any,
      )
      const background = fetchBoard('board-1', { intent: 'background' })
      helpers.markBoardDetailMutation('board-1')
      staleBoard.resolve({ id: 'board-1', name: 'Stale board', columns: [] })

      await expect(background).resolves.toBe(false)
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)
      const successor = fetchBoard('board-1', { intent: 'background' })
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)

      cancelBackgroundBoardFetch('board-1')
      await expect(successor).resolves.toBe(false)

      expect(state.currentBoard.value).toEqual({ id: 'board-1', name: 'Local board' })
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(2)
      expect(helpers.handleApiError).not.toHaveBeenCalled()
    })

    it('keeps a current background 403 authoritative when a local mutation lands first', async () => {
      const forbidden = {
        message: 'Request failed with status code 403',
        response: { status: 403 },
      }
      const revoked = createDeferred<{ id: string; name: string; columns: [] }>()
      state.currentBoard.value = { id: 'board-1', name: 'Cached board' }
      state.currentBoardCards.value = [{ id: 'cached-card' }]
      mockBoardsApi.getBoard.mockReturnValueOnce(revoked.promise)
      mockCardsApi.getCards.mockResolvedValueOnce([])
      mockLabelsApi.getLabels.mockResolvedValueOnce([])
      helpers.handleApiError.mockImplementationOnce((error: unknown, fallback: string) => {
        state.error.value = getErrorMessage(error, fallback)
      })

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const background = fetchBoard('board-1', { intent: 'background' })

      // Ordering: the local mutation completes, then the still-current read is
      // rejected because access was revoked.
      helpers.markBoardDetailMutation('board-1')
      revoked.reject(forbidden)

      await expect(background).resolves.toBe(false)

      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.objectContaining({ message: 'You no longer have access to this board' }),
        'You no longer have access to this board',
      )
      expect(state.error.value).toBe('You no longer have access to this board')
      expect(state.currentBoard.value).toEqual({ id: 'board-1', name: 'Cached board' })
      expect(state.currentBoardCards.value).toEqual([{ id: 'cached-card' }])
      // A failed read carries no payload to repair with, so it queues nothing.
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(1)
    })

    it('keeps a current background 403 authoritative when the mutation lands after the rejection', async () => {
      const forbidden = {
        message: 'Request failed with status code 403',
        response: { status: 403 },
      }
      const revoked = createDeferred<{ id: string; name: string; columns: [] }>()
      state.currentBoard.value = { id: 'board-1', name: 'Cached board' }
      mockBoardsApi.getBoard.mockReturnValueOnce(revoked.promise)
      mockCardsApi.getCards.mockResolvedValueOnce([])
      mockLabelsApi.getLabels.mockResolvedValueOnce([])
      helpers.handleApiError.mockImplementationOnce((error: unknown, fallback: string) => {
        state.error.value = getErrorMessage(error, fallback)
      })

      const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
      const background = fetchBoard('board-1', { intent: 'background' })

      // Ordering: the rejection is already queued when the local mutation
      // advances the epoch, so the catch observes the newer epoch.
      revoked.reject(forbidden)
      helpers.markBoardDetailMutation('board-1')

      await expect(background).resolves.toBe(false)

      expect(state.error.value).toBe('You no longer have access to this board')
      expect(state.currentBoard.value).toEqual({ id: 'board-1', name: 'Cached board' })
      expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(1)
    })
  })

  describe('resetForLogout', () => {
    it('clears list and detail state back to its initial values', () => {
      state.boards.value = [{ id: 'board-1', name: 'My Board' }]
      state.activeBoardId.value = 'board-1'
      state.currentBoard.value = { id: 'board-1', name: 'My Board' }
      state.currentBoardCards.value = [{ id: 'card-1' }]
      state.currentBoardLabels.value = [{ id: 'label-1' }]
      state.cardCommentsByCardId.value = { 'card-1': [{ id: 'comment-1' }] }
      state.boardPresenceMembers.value = [{ id: 'member-1' }]
      state.editingCardId.value = 'card-1'
      state.loading.value = true
      state.error.value = 'Failed to fetch boards'
      state.filters.value = {
        searchText: 'urgent',
        labelIds: ['label-1'],
        dueDateFilter: 'overdue',
        showBlockedOnly: true,
      }

      const { resetForLogout } = createBoardCrudActions(state as any, helpers as any)
      resetForLogout()

      expect(state.boards.value).toEqual([])
      expect(state.activeBoardId.value).toBeNull()
      expect(state.currentBoard.value).toBeNull()
      expect(state.currentBoardCards.value).toEqual([])
      expect(state.currentBoardLabels.value).toEqual([])
      expect(state.cardCommentsByCardId.value).toEqual({})
      expect(state.boardPresenceMembers.value).toEqual([])
      expect(state.editingCardId.value).toBeNull()
      expect(state.loading.value).toBe(false)
      expect(state.error.value).toBeNull()
      expect(state.filters.value).toEqual({
        searchText: '',
        labelIds: [],
        dueDateFilter: 'all',
        showBlockedOnly: false,
      })
    })

    it('resets filters to a fresh initialCardFilters() instance', () => {
      state.filters.value = {
        searchText: 'urgent',
        labelIds: ['label-1'],
        dueDateFilter: 'overdue',
        showBlockedOnly: true,
      }

      const { resetForLogout } = createBoardCrudActions(state as any, helpers as any)
      resetForLogout()

      expect(state.filters.value).toEqual(initialCardFilters())
      // A fresh object, not a shared default: mutating the restored value must
      // not reach what a later reset restores (the identity check that used to
      // sit here could not fail, since every call allocates).
      state.filters.value.labelIds.push('leaked')
      expect(initialCardFilters().labelIds).toEqual([])
    })

    it('discards a board-list response that settles after the reset', async () => {
      const inFlight = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoards.mockReturnValueOnce(inFlight.promise)

      const { fetchBoards, resetForLogout } = createBoardCrudActions(state as any, helpers as any)
      const pending = fetchBoards()
      resetForLogout()

      inFlight.resolve([{ id: 'previous-account-board', name: 'Private' }])
      await expect(pending).resolves.toBeUndefined()

      expect(state.boards.value).toEqual([])
      expect(state.activeBoardId.value).toBeNull()
    })

    it('drops the throttle stamp so the next session refetches immediately', async () => {
      const inFlight = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoards
        .mockReturnValueOnce(inFlight.promise)
        .mockResolvedValueOnce([{ id: 'next-session-board', name: 'Next' }])

      const { fetchBoards, resetForLogout } = createBoardCrudActions(state as any, helpers as any)
      const pending = fetchBoards()
      resetForLogout()
      inFlight.resolve([{ id: 'previous-account-board', name: 'Private' }])
      await expect(pending).resolves.toBeUndefined()

      // The discarded read wrote no throttle stamp, and the share slot it held
      // was dropped, so the next session's first caller issues a real request.
      await expect(fetchBoards()).resolves.toBeUndefined()
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(2)
      expect(state.boards.value).toEqual([{ id: 'next-session-board', name: 'Next' }])
    })

    it('aborts the in-flight board-list read and writes nothing when it settles', async () => {
      const inFlight = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoards
        .mockReturnValueOnce(inFlight.promise)
        .mockResolvedValueOnce([{ id: 'next-session-board', name: 'Next' }])

      const { fetchBoards, resetForLogout } = createBoardCrudActions(state as any, helpers as any)
      const pending = fetchBoards()
      const signal = mockBoardsApi.getBoards.mock.calls[0][2].signal as AbortSignal
      expect(signal.aborted).toBe(false)

      resetForLogout()
      // The previous account's request is cancelled on the wire, not merely
      // ignored when it lands.
      expect(signal.aborted).toBe(true)

      inFlight.reject(new axios.CanceledError('canceled'))
      await expect(pending).resolves.toBeUndefined()

      expect(helpers.handleApiError).not.toHaveBeenCalled()
      expect(state.boards.value).toEqual([])
      expect(state.error.value).toBeNull()
      expect(state.loading.value).toBe(false)

      // The next session's first caller starts a fresh request.
      await expect(fetchBoards()).resolves.toBeUndefined()
      expect(mockBoardsApi.getBoards).toHaveBeenCalledTimes(2)
      expect(state.boards.value).toEqual([{ id: 'next-session-board', name: 'Next' }])
    })

    it('aborts an in-flight filtered board-list read as well', async () => {
      const unfiltered = createDeferred<Array<{ id: string; name: string }>>()
      const filtered = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoards
        .mockReturnValueOnce(unfiltered.promise)
        .mockReturnValueOnce(filtered.promise)

      const { fetchBoards, resetForLogout } = createBoardCrudActions(state as any, helpers as any)
      const pendingUnfiltered = fetchBoards()
      const pendingFiltered = fetchBoards(undefined, true)

      const unfilteredSignal = mockBoardsApi.getBoards.mock.calls[0][2].signal as AbortSignal
      const filteredSignal = mockBoardsApi.getBoards.mock.calls[1][2].signal as AbortSignal
      expect(filteredSignal).not.toBe(unfilteredSignal)

      resetForLogout()

      // No request outlives the session that started it, share or not.
      expect(unfilteredSignal.aborted).toBe(true)
      expect(filteredSignal.aborted).toBe(true)

      unfiltered.reject(new axios.CanceledError('canceled'))
      filtered.reject(new axios.CanceledError('canceled'))
      await expect(pendingUnfiltered).resolves.toBeUndefined()
      await expect(pendingFiltered).resolves.toBeUndefined()

      expect(helpers.handleApiError).not.toHaveBeenCalled()
      expect(state.boards.value).toEqual([])
      expect(state.loading.value).toBe(false)
    })

    it('raises no error surface for a board-list request that fails after the reset', async () => {
      const inFlight = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoards.mockReturnValueOnce(inFlight.promise)

      const { fetchBoards, resetForLogout } = createBoardCrudActions(state as any, helpers as any)
      const pending = fetchBoards()
      resetForLogout()

      inFlight.reject(new Error('network error'))
      await expect(pending).resolves.toBeUndefined()

      expect(helpers.handleApiError).not.toHaveBeenCalled()
      expect(state.error.value).toBeNull()
      expect(state.boards.value).toEqual([])
    })

    // A superseded read must not clear the loading flag a newer read owns.
    // Otherwise BoardsListView drops its skeleton and shows the empty state to
    // the next signed-in user until their own read resolves.
    it('leaves a newer read loading when a superseded response resolves', async () => {
      const supersededRead = createDeferred<Array<{ id: string; name: string }>>()
      const nextSessionRead = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoards
        .mockReturnValueOnce(supersededRead.promise)
        .mockReturnValueOnce(nextSessionRead.promise)

      const { fetchBoards, resetForLogout } = createBoardCrudActions(state as any, helpers as any)
      const previousSessionFetch = fetchBoards()
      resetForLogout()
      const nextSessionFetch = fetchBoards()
      expect(state.loading.value).toBe(true)

      supersededRead.resolve([{ id: 'previous-account-board', name: 'Private' }])
      await expect(previousSessionFetch).resolves.toBeUndefined()

      expect(state.loading.value).toBe(true)
      expect(state.boards.value).toEqual([])

      nextSessionRead.resolve([{ id: 'next-session-board', name: 'Next' }])
      await expect(nextSessionFetch).resolves.toBeUndefined()

      expect(state.boards.value).toEqual([{ id: 'next-session-board', name: 'Next' }])
      expect(state.loading.value).toBe(false)
    })

    it('leaves a newer read loading when a superseded response rejects', async () => {
      const supersededRead = createDeferred<Array<{ id: string; name: string }>>()
      const nextSessionRead = createDeferred<Array<{ id: string; name: string }>>()
      mockBoardsApi.getBoards
        .mockReturnValueOnce(supersededRead.promise)
        .mockReturnValueOnce(nextSessionRead.promise)

      const { fetchBoards, resetForLogout } = createBoardCrudActions(state as any, helpers as any)
      const previousSessionFetch = fetchBoards()
      resetForLogout()
      const nextSessionFetch = fetchBoards()
      expect(state.loading.value).toBe(true)

      supersededRead.reject(new Error('network error'))
      await expect(previousSessionFetch).resolves.toBeUndefined()

      expect(state.loading.value).toBe(true)
      expect(state.boards.value).toEqual([])
      expect(state.error.value).toBeNull()
      expect(helpers.handleApiError).not.toHaveBeenCalled()

      nextSessionRead.resolve([{ id: 'next-session-board', name: 'Next' }])
      await expect(nextSessionFetch).resolves.toBeUndefined()

      expect(state.boards.value).toEqual([{ id: 'next-session-board', name: 'Next' }])
      expect(state.loading.value).toBe(false)
    })

    it('aborts an active detail fetch and discards its late response', async () => {
      const board = createDeferred<{ id: string; name: string; columns: [] }>()
      mockBoardsApi.getBoard.mockReturnValueOnce(board.promise)
      mockCardsApi.getCards.mockResolvedValueOnce([])
      mockLabelsApi.getLabels.mockResolvedValueOnce([])

      const { fetchBoard, resetForLogout } = createBoardCrudActions(state as any, helpers as any)
      const pending = fetchBoard('board-1')
      resetForLogout()

      board.resolve({ id: 'board-1', name: 'Private Board', columns: [] })
      await expect(pending).resolves.toBe(false)

      expect(state.currentBoard.value).toBeNull()
      expect(state.currentBoardCards.value).toEqual([])
      expect(state.currentBoardLabels.value).toEqual([])
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

    it('advances the board detail mutation epoch so an older fan-out cannot overwrite it', async () => {
      mockBoardsApi.updateBoard.mockResolvedValueOnce({ id: 'board-1', name: 'Renamed' })

      const { updateBoard } = createBoardCrudActions(state as any, helpers as any)
      await updateBoard('board-1', { name: 'Renamed' } as any)

      expect(helpers.markBoardDetailMutation).toHaveBeenCalledWith('board-1')
    })

    it('does not advance the board detail mutation epoch when the write fails', async () => {
      mockBoardsApi.updateBoard.mockRejectedValueOnce(new Error('update failed'))

      const { updateBoard } = createBoardCrudActions(state as any, helpers as any)
      await expect(updateBoard('board-1', { name: 'Renamed' } as any)).rejects.toThrow(
        'update failed',
      )

      expect(helpers.markBoardDetailMutation).not.toHaveBeenCalled()
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
