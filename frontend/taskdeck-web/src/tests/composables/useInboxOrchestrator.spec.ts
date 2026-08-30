import { describe, expect, it, vi, beforeEach } from 'vitest'
import type { BoardDetail } from '../../types/board'

let mountedCallback: (() => void) | null = null
let unmountedCallback: (() => void) | null = null
let watchers: Array<[unknown, (val: unknown, oldVal: unknown, onCleanup: (fn: () => void) => void) => void]> = []

vi.mock('vue', () => ({
  ref: (v: unknown) => ({ value: v }),
  computed: (fn: () => unknown) => ({ get value() { return fn() } }),
  watch: (source: unknown, cb: (val: unknown, oldVal: unknown, onCleanup: (fn: () => void) => void) => void) => {
    watchers.push([source, cb])
  },
  onMounted: (cb: () => void) => { mountedCallback = cb },
  onUnmounted: (cb: () => void) => { unmountedCallback = cb },
  nextTick: vi.fn(() => Promise.resolve()),
}))

const mockRouter = { push: vi.fn(), replace: vi.fn() }
const mockRoute = { hash: '', query: {} }

vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
  useRouter: () => mockRouter,
}))

const mockBoardsApi = vi.hoisted(() => ({
  getBoard: vi.fn<(id: string) => Promise<BoardDetail>>(),
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: mockBoardsApi,
}))

const mockCaptureStore = {
  items: [] as Array<{ id: string }>,
  detailById: {} as Record<string, { id: string; rawText: string; boardId: string | null; status: string }>,
  fetchItems: vi.fn(),
  fetchDetail: vi.fn(),
  batchTriage: vi.fn(),
  ignoreItem: vi.fn(),
  cancelItem: vi.fn(),
  triageItem: vi.fn(),
  pollTriageCompletion: vi.fn(() => vi.fn()),
  pollBatchTriageCompletion: vi.fn(() => vi.fn()),
  cacheDetail: vi.fn(),
  peekDetail: vi.fn(),
  updateSuggestion: vi.fn(),
}

vi.mock('../../store/captureStore', () => ({
  useCaptureStore: () => mockCaptureStore,
}))

vi.mock('../../types/capture', () => ({
  isTriageTerminalStatus: (s: unknown) => ['Triaged', 'ProposalCreated', 'Converted', 'Ignored', 'Failed'].includes(s as string),
}))

const mockUnregister = vi.fn()
vi.mock('../../composables/useEscapeStack', () => ({
  registerEscapeHandler: vi.fn(() => mockUnregister),
}))

vi.mock('../../composables/usePerformanceMark', () => ({
  usePerformanceMark: () => ({ start: vi.fn(), end: vi.fn() }),
}))

vi.mock('../../utils/navigation', () => ({
  normalizeBoardIdQueryParam: (v: unknown) => v ?? null,
}))

import { useInboxOrchestrator } from '../../composables/useInboxOrchestrator'

function createOrchestrator() {
  mountedCallback = null
  unmountedCallback = null
  watchers = []
  const scrollToIndex = vi.fn(() => vi.fn())
  return useInboxOrchestrator({ scrollToIndex: () => scrollToIndex })
}

function watcherForSource(source: unknown) {
  const watcher = watchers.find(([candidate]) => candidate === source)
  expect(watcher).toBeDefined()
  return watcher!
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

function makeBoard(id: string, name: string, columnId: string, columnName: string): BoardDetail {
  const timestamp = '2026-08-24T00:00:00Z'
  return {
    id,
    name,
    description: null,
    isArchived: false,
    createdAt: timestamp,
    updatedAt: timestamp,
    columns: [{
      id: columnId,
      boardId: id,
      name: columnName,
      position: 0,
      wipLimit: null,
      cardCount: 0,
      createdAt: timestamp,
      updatedAt: timestamp,
    }],
  }
}

async function flushAsyncWork() {
  await Promise.resolve()
  await Promise.resolve()
}

function summaryRow(id: string, boardId: string | null) {
  return {
    id,
    userId: 'u1',
    boardId,
    status: 'New',
    source: 'Typed',
    textExcerpt: id,
    createdAt: '2026-08-24T00:00:00Z',
    processedAt: null,
  } as never
}

describe('useInboxOrchestrator', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    watchers = []
    mockCaptureStore.items = []
    mockCaptureStore.detailById = {}
    mockRoute.hash = ''
    mockRoute.query = {}
    mockRouter.push.mockReset()
    mockRouter.replace.mockReset()
    mockBoardsApi.getBoard.mockReset()
    mockCaptureStore.batchTriage.mockReset().mockResolvedValue({
      total: 2,
      succeeded: 2,
      failed: 0,
      results: [
        { itemId: 'a', success: true },
        { itemId: 'b', success: true },
      ],
    })
    mockCaptureStore.pollBatchTriageCompletion.mockReset().mockReturnValue(vi.fn())
  })

  describe('batch selection', () => {
    it('toggleItemSelection adds and removes items', () => {
      const orch = createOrchestrator()
      orch.toggleItemSelection('a')
      expect(orch.selectedIds.value.has('a')).toBe(true)
      orch.toggleItemSelection('a')
      expect(orch.selectedIds.value.has('a')).toBe(false)
    })

    it('toggleSelectAll selects all items then clears', () => {
      mockCaptureStore.items = [{ id: '1' }, { id: '2' }, { id: '3' }]
      const orch = createOrchestrator()
      orch.toggleSelectAll()
      expect(orch.selectedIds.value.size).toBe(3)
      orch.toggleSelectAll()
      expect(orch.selectedIds.value.size).toBe(0)
    })

    it('clearSelection empties the set', () => {
      const orch = createOrchestrator()
      orch.toggleItemSelection('x')
      orch.clearSelection()
      expect(orch.selectedIds.value.size).toBe(0)
    })

    it('batchAction calls store and clears selection', async () => {
      const orch = createOrchestrator()
      orch.toggleItemSelection('a')
      orch.toggleItemSelection('b')
      await orch.batchAction('triage')
      expect(mockCaptureStore.batchTriage).toHaveBeenCalledWith(['a', 'b'], 'triage')
      expect(mockCaptureStore.pollBatchTriageCompletion).toHaveBeenCalledWith(
        ['a', 'b'],
        { limit: 200 },
      )
      expect(orch.selectedIds.value.size).toBe(0)
    })

    it('polls only successfully queued triage ids in the active board scope', async () => {
      mockRoute.query = { boardId: 'board-7' }
      mockCaptureStore.batchTriage.mockResolvedValueOnce({
        total: 2,
        succeeded: 1,
        failed: 1,
        results: [
          { itemId: 'accepted', success: true },
          { itemId: 'rejected', success: false, errorCode: 'NotFound' },
        ],
      })
      const orch = createOrchestrator()
      orch.toggleItemSelection('accepted')
      orch.toggleItemSelection('rejected')

      await orch.batchAction('triage')

      expect(mockCaptureStore.pollBatchTriageCompletion).toHaveBeenCalledWith(
        ['accepted'],
        { limit: 200, boardId: 'board-7' },
      )
    })

    it('does not poll ignore or cancel batch actions', async () => {
      const orch = createOrchestrator()
      orch.toggleItemSelection('a')

      await orch.batchAction('ignore')

      expect(mockCaptureStore.pollBatchTriageCompletion).not.toHaveBeenCalled()
    })

    it('stops an active batch poll when the Inbox scope changes', async () => {
      mockRoute.query = { boardId: 'board-a' }
      const stopBatchPoll = vi.fn()
      mockCaptureStore.pollBatchTriageCompletion.mockReturnValueOnce(stopBatchPoll)
      const orch = createOrchestrator()
      orch.toggleItemSelection('a')
      await orch.batchAction('triage')

      mockRoute.query = { boardId: 'board-b' }
      watcherForSource(orch.activeBoardId)[1]('board-b', 'board-a', () => {})

      expect(stopBatchPoll).toHaveBeenCalledTimes(1)
    })

    it('does not start a batch poll when the Inbox scope changes before the action resolves', async () => {
      mockRoute.query = { boardId: 'board-a' }
      const batch = deferred<{
        total: number
        succeeded: number
        failed: number
        results: Array<{ itemId: string; success: boolean }>
      }>()
      mockCaptureStore.batchTriage.mockReturnValueOnce(batch.promise)
      const orch = createOrchestrator()
      orch.toggleItemSelection('a')
      const action = orch.batchAction('triage')

      mockRoute.query = { boardId: 'board-b' }
      watcherForSource(orch.activeBoardId)[1]('board-b', 'board-a', () => {})
      batch.resolve({
        total: 1,
        succeeded: 1,
        failed: 0,
        results: [{ itemId: 'a', success: true }],
      })
      await action

      expect(mockCaptureStore.pollBatchTriageCompletion).not.toHaveBeenCalled()
    })

    it('keeps batch polling when the open Legacy detail changes', async () => {
      const stopBatchPoll = vi.fn()
      mockCaptureStore.pollBatchTriageCompletion.mockReturnValueOnce(stopBatchPoll)
      const orch = createOrchestrator()
      orch.toggleItemSelection('a')
      await orch.batchAction('triage')

      const selectionWatcher = watcherForSource(orch.selectedItemId)
      orch.selectedItemId.value = 'other'
      selectionWatcher[1]('other', null, () => {})

      expect(stopBatchPoll).not.toHaveBeenCalled()
    })

    it('batchAction does nothing when selection is empty', async () => {
      const orch = createOrchestrator()
      await orch.batchAction('ignore')
      expect(mockCaptureStore.batchTriage).not.toHaveBeenCalled()
    })

    it('batchAction swallows store errors', async () => {
      mockCaptureStore.batchTriage.mockRejectedValueOnce(new Error('fail'))
      const orch = createOrchestrator()
      orch.toggleItemSelection('a')
      await expect(orch.batchAction('cancel')).resolves.toBeUndefined()
    })
  })

  describe('route scope', () => {
    it('reads board and column context, then clears both without a page reload', async () => {
      mockRoute.query = { boardId: 'board-1', columnId: 'column-1', source: 'capture' }
      const orch = createOrchestrator()

      expect(orch.activeBoardId.value).toBe('board-1')
      expect(orch.activeColumnId.value).toBe('column-1')

      await orch.clearScope()
      expect(mockRouter.replace).toHaveBeenCalledWith({
        name: 'workspace-inbox',
        query: { source: 'capture' },
      })
    })

    // The orchestrator half of the archived-history list-write boundary (#1973).
    // The store half — that `syncSummary: false` actually holds when the detail
    // GET resolves after the exit's list load — is pinned in
    // `store/captureStore.spec.ts`. Legacy loads detail through `fetchDetail`
    // (Paper uses the non-caching `peekDetail`), and `fetchDetail`'s success
    // path unshifts an absent summary straight into the live `items`, so the
    // flag is the only thing standing between archived inspection and a
    // mutation-enabled row at the top of the live Inbox.
    it('opts out of summary sync for detail loads inside archived history', async () => {
      mockRoute.query = { boardId: 'archived-board', history: 'archived' }
      const orch = createOrchestrator()
      mockCaptureStore.fetchDetail.mockResolvedValue(undefined)

      expect(orch.isArchivedHistory.value).toBe(true)

      await orch.openItemFromList(summaryRow('archived-capture', 'archived-board'), 0)
      expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('archived-capture', {
        syncSummary: false,
      })

      // Refresh Detail is a READ affordance and stays available in history mode,
      // but it takes the same boundary — and `forceRefresh` means it always
      // reaches the caching path rather than the cached early return.
      mockCaptureStore.fetchDetail.mockClear()
      await orch.refreshSelectedDetail()
      expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('archived-capture', {
        forceRefresh: true,
        syncSummary: false,
      })
    })

    it('keeps syncing summaries for detail loads in the live Inbox', async () => {
      mockRoute.query = {}
      const orch = createOrchestrator()
      mockCaptureStore.fetchDetail.mockResolvedValue(undefined)

      expect(orch.isArchivedHistory.value).toBe(false)

      await orch.openItemFromList(summaryRow('live-capture', 'live-board'), 0)
      expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('live-capture', {
        syncSummary: true,
      })

      mockCaptureStore.fetchDetail.mockClear()
      await orch.refreshSelectedDetail()
      expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('live-capture', {
        forceRefresh: true,
        syncSummary: true,
      })
    })

    it('keeps B names after an obsolete A metadata success resolves last', async () => {
      const boardA = deferred<BoardDetail>()
      const boardB = deferred<BoardDetail>()
      mockBoardsApi.getBoard
        .mockReturnValueOnce(boardA.promise)
        .mockReturnValueOnce(boardB.promise)
      mockRoute.query = { boardId: 'board-a', columnId: 'column-a' }
      const orch = createOrchestrator()
      mountedCallback!()

      mockRoute.query = { boardId: 'board-b', columnId: 'column-b' }
      watcherForSource(orch.activeBoardId)[1]('board-b', 'board-a', () => {})
      boardB.resolve(makeBoard('board-b', 'Board B', 'column-b', 'Column B'))
      await flushAsyncWork()
      expect(orch.activeBoardName.value).toBe('Board B')
      expect(orch.activeColumnName.value).toBe('Column B')

      boardA.resolve(makeBoard('board-a', 'Stale Board A', 'column-a', 'Stale Column A'))
      await flushAsyncWork()
      expect(orch.activeBoardName.value).toBe('Board B')
      expect(orch.activeColumnName.value).toBe('Column B')
    })

    it('keeps B names after an obsolete A metadata failure resolves last', async () => {
      const boardA = deferred<BoardDetail>()
      const boardB = deferred<BoardDetail>()
      mockBoardsApi.getBoard
        .mockReturnValueOnce(boardA.promise)
        .mockReturnValueOnce(boardB.promise)
      mockRoute.query = { boardId: 'board-a', columnId: 'column-a' }
      const orch = createOrchestrator()
      mountedCallback!()

      mockRoute.query = { boardId: 'board-b', columnId: 'column-b' }
      watcherForSource(orch.activeBoardId)[1]('board-b', 'board-a', () => {})
      boardB.resolve(makeBoard('board-b', 'Board B', 'column-b', 'Column B'))
      await flushAsyncWork()

      boardA.reject(new Error('obsolete A failed'))
      await flushAsyncWork()
      expect(orch.activeBoardName.value).toBe('Board B')
      expect(orch.activeColumnName.value).toBe('Column B')
    })

    it('falls back to B ids while loaded A metadata is being replaced', async () => {
      mockBoardsApi.getBoard.mockResolvedValueOnce(
        makeBoard('board-a', 'Board A', 'column-a', 'Column A'),
      )
      const boardB = deferred<BoardDetail>()
      mockBoardsApi.getBoard.mockReturnValueOnce(boardB.promise)
      mockRoute.query = { boardId: 'board-a', columnId: 'column-a' }
      const orch = createOrchestrator()
      mountedCallback!()
      await flushAsyncWork()
      expect(orch.activeBoardName.value).toBe('Board A')
      expect(orch.activeColumnName.value).toBe('Column A')

      mockRoute.query = { boardId: 'board-b', columnId: 'column-b' }
      watcherForSource(orch.activeBoardId)[1]('board-b', 'board-a', () => {})
      expect(orch.activeBoardName.value).toBe('board-b')
      expect(orch.activeColumnName.value).toBe('column-b')

      boardB.resolve(makeBoard('board-b', 'Board B', 'column-b', 'Column B'))
      await flushAsyncWork()
      expect(orch.activeBoardName.value).toBe('Board B')
      expect(orch.activeColumnName.value).toBe('Column B')
    })
  })

  describe('suggestion editing', () => {
    it('startEditSuggestion returns early when no selectedItem', () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = null
      orch.startEditSuggestion()
      expect(orch.isEditingSuggestion.value).toBe(false)
    })

    it('startEditSuggestion copies rawText and enables editing', () => {
      mockCaptureStore.detailById = { 'item-1': { id: 'item-1', rawText: 'hello world', boardId: null, status: 'New' } }
      const orch = createOrchestrator()
      orch.selectedItemId.value = 'item-1'
      orch.startEditSuggestion()
      expect(orch.isEditingSuggestion.value).toBe(true)
      expect(orch.editedText.value).toBe('hello world')
      expect(orch.editedTitleHint.value).toBe('')
    })

    it('cancelEditSuggestion resets editing state', () => {
      const orch = createOrchestrator()
      orch.isEditingSuggestion.value = true
      orch.editedText.value = 'something'
      orch.editedTitleHint.value = 'hint'
      orch.cancelEditSuggestion()
      expect(orch.isEditingSuggestion.value).toBe(false)
      expect(orch.editedText.value).toBe('')
      expect(orch.editedTitleHint.value).toBe('')
    })

    it('saveEditedSuggestion calls store and resets state', async () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = 'item-1'
      orch.editedText.value = '  updated text  '
      orch.editedTitleHint.value = ' title '
      orch.isEditingSuggestion.value = true
      await orch.saveEditedSuggestion()
      expect(mockCaptureStore.updateSuggestion).toHaveBeenCalledWith('item-1', {
        text: 'updated text',
        titleHint: 'title',
      })
      expect(orch.isEditingSuggestion.value).toBe(false)
    })

    it('saveEditedSuggestion returns early if text is empty', async () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = 'item-1'
      orch.editedText.value = '   '
      await orch.saveEditedSuggestion()
      expect(mockCaptureStore.updateSuggestion).not.toHaveBeenCalled()
    })

    it('saveEditedSuggestion returns early if no selectedItemId', async () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = null
      orch.editedText.value = 'text'
      await orch.saveEditedSuggestion()
      expect(mockCaptureStore.updateSuggestion).not.toHaveBeenCalled()
    })

    it('saveEditedSuggestion passes null titleHint when empty', async () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = 'item-1'
      orch.editedText.value = 'updated'
      orch.editedTitleHint.value = ''
      await orch.saveEditedSuggestion()
      expect(mockCaptureStore.updateSuggestion).toHaveBeenCalledWith('item-1', {
        text: 'updated',
        titleHint: null,
      })
    })
  })

  describe('capture modal', () => {
    it('openCaptureModal sets flag to true', () => {
      const orch = createOrchestrator()
      orch.openCaptureModal()
      expect(orch.showCaptureModal.value).toBe(true)
    })

    it('closeCaptureModal sets flag to false', () => {
      const orch = createOrchestrator()
      orch.showCaptureModal.value = true
      orch.closeCaptureModal()
      expect(orch.showCaptureModal.value).toBe(false)
    })
  })

  describe('keyboard navigation', () => {
    it('ArrowDown wraps to beginning', async () => {
      mockCaptureStore.items = [{ id: '1' }, { id: '2' }, { id: '3' }]
      const orch = createOrchestrator()
      orch.activeItemIndex.value = 2
      const event = { key: 'ArrowDown', preventDefault: vi.fn() } as unknown as KeyboardEvent
      await orch.handleKeydown(event)
      expect(event.preventDefault).toHaveBeenCalled()
      expect(orch.activeItemIndex.value).toBe(0)
    })

    it('ArrowUp wraps to end', async () => {
      mockCaptureStore.items = [{ id: '1' }, { id: '2' }, { id: '3' }]
      const orch = createOrchestrator()
      orch.activeItemIndex.value = 0
      const event = { key: 'ArrowUp', preventDefault: vi.fn() } as unknown as KeyboardEvent
      await orch.handleKeydown(event)
      expect(event.preventDefault).toHaveBeenCalled()
      expect(orch.activeItemIndex.value).toBe(2)
    })

    it('does nothing when items is empty', async () => {
      mockCaptureStore.items = []
      const orch = createOrchestrator()
      const event = { key: 'ArrowDown', preventDefault: vi.fn() } as unknown as KeyboardEvent
      await orch.handleKeydown(event)
      expect(event.preventDefault).not.toHaveBeenCalled()
    })

    it('Enter opens the active item', async () => {
      mockCaptureStore.items = [{ id: 'abc' }]
      const orch = createOrchestrator()
      orch.activeItemIndex.value = 0
      const event = { key: 'Enter', preventDefault: vi.fn() } as unknown as KeyboardEvent
      await orch.handleKeydown(event)
      expect(event.preventDefault).toHaveBeenCalled()
      expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('abc', { syncSummary: true })
    })
  })

  describe('hash deep-link', () => {
    it('loadInbox triggers openItemFromHash when hash is present', async () => {
      mockRoute.hash = '#capture-deep-id'
      const orch = createOrchestrator()
      await orch.loadInbox()
      expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('deep-id', { syncSummary: true })
    })

    it('loadInbox does not fetch detail when hash is absent', async () => {
      mockRoute.hash = ''
      const orch = createOrchestrator()
      await orch.loadInbox()
      expect(mockCaptureStore.fetchDetail).not.toHaveBeenCalled()
    })

    it('loadInbox does not fetch when hash has invalid format', async () => {
      mockRoute.hash = '#other-thing'
      const orch = createOrchestrator()
      await orch.loadInbox()
      expect(mockCaptureStore.fetchDetail).not.toHaveBeenCalled()
    })
  })

  describe('detail actions', () => {
    it('ignoreSelected calls captureStore.ignoreItem', async () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = 'item-x'
      await orch.ignoreSelected()
      expect(mockCaptureStore.ignoreItem).toHaveBeenCalledWith('item-x')
    })

    it('ignoreSelected does nothing without selection', async () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = null
      await orch.ignoreSelected()
      expect(mockCaptureStore.ignoreItem).not.toHaveBeenCalled()
    })

    it('cancelSelected calls captureStore.cancelItem', async () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = 'item-y'
      await orch.cancelSelected()
      expect(mockCaptureStore.cancelItem).toHaveBeenCalledWith('item-y')
    })

    it('cancelSelected does nothing without selection', async () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = null
      await orch.cancelSelected()
      expect(mockCaptureStore.cancelItem).not.toHaveBeenCalled()
    })

    it('triageSelected calls store and starts polling when not terminal', async () => {
      mockCaptureStore.detailById = { 'item-t': { id: 'item-t', rawText: '', boardId: null, status: 'Triaging' } }
      const stopPoll = vi.fn()
      mockCaptureStore.pollTriageCompletion.mockReturnValue(stopPoll)
      const orch = createOrchestrator()
      orch.selectedItemId.value = 'item-t'
      await orch.triageSelected()
      expect(mockCaptureStore.triageItem).toHaveBeenCalledWith('item-t')
      expect(mockCaptureStore.pollTriageCompletion).toHaveBeenCalledWith('item-t')
    })

    it('triageSelected does nothing without selection', async () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = null
      await orch.triageSelected()
      expect(mockCaptureStore.triageItem).not.toHaveBeenCalled()
    })

    it('triageSelected stops existing polling before starting new', async () => {
      const stopPoll1 = vi.fn()
      const stopPoll2 = vi.fn()
      mockCaptureStore.pollTriageCompletion
        .mockReturnValueOnce(stopPoll1)
        .mockReturnValueOnce(stopPoll2)
      mockCaptureStore.detailById = { 'a': { id: 'a', rawText: '', boardId: null, status: 'Triaging' } }
      const orch = createOrchestrator()
      orch.selectedItemId.value = 'a'
      await orch.triageSelected()
      await orch.triageSelected()
      expect(stopPoll1).toHaveBeenCalled()
    })
  })

  describe('lifecycle', () => {
    it('onMounted triggers loadInbox', async () => {
      createOrchestrator()
      expect(mountedCallback).not.toBeNull()
      await mountedCallback!()
      expect(mockCaptureStore.fetchItems).toHaveBeenCalled()
    })

    it('onUnmounted stops active triage polling', async () => {
      const stopPoll = vi.fn()
      mockCaptureStore.pollTriageCompletion.mockReturnValueOnce(stopPoll)
      mockCaptureStore.detailById = { 'a': { id: 'a', rawText: '', boardId: null, status: 'Triaging' } }
      const orch = createOrchestrator()
      orch.selectedItemId.value = 'a'
      await orch.triageSelected()
      expect(stopPoll).not.toHaveBeenCalled()
      unmountedCallback!()
      expect(stopPoll).toHaveBeenCalled()
    })

    it('onUnmounted stops active batch triage polling independently', async () => {
      const stopBatchPoll = vi.fn()
      mockCaptureStore.pollBatchTriageCompletion.mockReturnValueOnce(stopBatchPoll)
      const orch = createOrchestrator()
      orch.toggleItemSelection('a')
      await orch.batchAction('triage')

      unmountedCallback!()

      expect(stopBatchPoll).toHaveBeenCalledTimes(1)
    })
  })

  describe('watchers', () => {
    it('items watcher resets activeItemIndex when items become empty', () => {
      mockCaptureStore.items = [{ id: '1' }]
      const orch = createOrchestrator()
      orch.activeItemIndex.value = 5
      const itemsWatcher = watcherForSource(orch.items)
      mockCaptureStore.items = []
      itemsWatcher[1]([], undefined, () => {})
      expect(orch.activeItemIndex.value).toBe(0)
    })

    it('items watcher clamps activeItemIndex when items shrink', () => {
      mockCaptureStore.items = [{ id: '1' }, { id: '2' }, { id: '3' }]
      const orch = createOrchestrator()
      orch.activeItemIndex.value = 4
      const itemsWatcher = watcherForSource(orch.items)
      itemsWatcher[1]([{ id: '1' }, { id: '2' }], undefined, () => {})
      expect(orch.activeItemIndex.value).toBe(1)
    })

    it('selectedItemId watcher resets editing state', () => {
      const orch = createOrchestrator()
      orch.isEditingSuggestion.value = true
      orch.editedText.value = 'x'
      const selectedWatcher = watcherForSource(orch.selectedItemId)
      selectedWatcher[1]('new-id', undefined, () => {})
      expect(orch.isEditingSuggestion.value).toBe(false)
      expect(orch.editedText.value).toBe('')
    })
  })

  describe('routing helpers', () => {
    it('openReview navigates to workspace-review', () => {
      const orch = createOrchestrator()
      orch.openReview()
      expect(mockRouter.push).toHaveBeenCalledWith(
        expect.objectContaining({ name: 'workspace-review' }),
      )
    })

    it('openProposal navigates with hash', () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = null
      orch.openProposal('prop-123')
      expect(mockRouter.push).toHaveBeenCalledWith(
        expect.objectContaining({
          name: 'workspace-review',
          hash: '#proposal-prop-123',
        }),
      )
    })

    it('openRoute pushes arbitrary path', () => {
      const orch = createOrchestrator()
      orch.openRoute('/settings')
      expect(mockRouter.push).toHaveBeenCalledWith('/settings')
    })
  })

  describe('closeDetail', () => {
    it('resets selectedItemId and hashLoadFailedItemId', async () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = 'item-1'
      orch.hashLoadFailedItemId.value = 'item-1'
      await orch.closeDetail()
      expect(orch.selectedItemId.value).toBeNull()
      expect(orch.hashLoadFailedItemId.value).toBeNull()
    })
  })

  describe('loadInbox', () => {
    it('calls fetchItems with boardId when active', async () => {
      mockRoute.query = { boardId: 'board-1' }
      const orch = createOrchestrator()
      await orch.loadInbox()
      expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith(
        expect.objectContaining({ boardId: 'board-1', limit: 200 }),
      )
    })

    it('calls fetchItems without boardId when none active', async () => {
      mockRoute.query = {}
      const orch = createOrchestrator()
      await orch.loadInbox()
      expect(mockCaptureStore.fetchItems).toHaveBeenCalledWith({ limit: 200 })
    })
  })

  describe('setActiveIndex', () => {
    it('does nothing for out-of-bounds index', () => {
      mockCaptureStore.items = [{ id: '1' }]
      const orch = createOrchestrator()
      orch.activeItemIndex.value = 0
      orch.setActiveIndex(-1)
      expect(orch.activeItemIndex.value).toBe(0)
      orch.setActiveIndex(5)
      expect(orch.activeItemIndex.value).toBe(0)
    })
  })

  describe('refreshSelectedDetail', () => {
    it('calls fetchDetail with forceRefresh', async () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = 'item-r'
      await orch.refreshSelectedDetail()
      expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith('item-r', {
        forceRefresh: true,
        syncSummary: true,
      })
    })

    it('does nothing without selection', async () => {
      const orch = createOrchestrator()
      orch.selectedItemId.value = null
      await orch.refreshSelectedDetail()
      expect(mockCaptureStore.fetchDetail).not.toHaveBeenCalled()
    })
  })
})
