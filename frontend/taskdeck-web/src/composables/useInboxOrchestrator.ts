import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useCaptureStore } from '../store/captureStore'
import { boardsApi } from '../api/boardsApi'
import { isTriageTerminalStatus } from '../types/capture'
import type { CaptureItem, CaptureItemSummary } from '../types/capture'
import type { BoardDetail } from '../types/board'
import { registerEscapeHandler } from './useEscapeStack'
import { usePerformanceMark } from './usePerformanceMark'
import { normalizeBoardIdQueryParam } from '../utils/navigation'

export function useInboxOrchestrator(options: {
  scrollToIndex: () => ((index: number) => void) | undefined
}) {
  const captureStore = useCaptureStore()
  const router = useRouter()
  const route = useRoute()
  const inboxLoadPerf = usePerformanceMark('inbox-load')
  const selectedItemId = ref<string | null>(null)
  const hashLoadFailedItemId = ref<string | null>(null)
  const activeItemIndex = ref(0)
  const showCaptureModal = ref(false)
  let stopTriagePolling: (() => void) | null = null
  let scopedBoardLoadGeneration = 0

  // Batch selection state
  const selectedIds = ref<Set<string>>(new Set())
  const isEditingSuggestion = ref(false)
  const editedText = ref('')
  const editedTitleHint = ref('')
  const scopedBoard = ref<BoardDetail | null>(null)

  const items = computed(() => captureStore.items)
  const activeDescendantId = computed(() => {
    if (items.value.length === 0) {
      return undefined
    }
    return `td-inbox-option-${activeItemIndex.value}`
  })
  const selectedItem = computed(() => {
    if (!selectedItemId.value) {
      return null
    }
    return captureStore.detailById[selectedItemId.value] ?? null
  })
  const activeBoardId = computed(() => normalizeBoardIdQueryParam(route.query.boardId))
  const activeColumnId = computed(() => normalizeBoardIdQueryParam(route.query.columnId))
  const isArchivedHistory = computed(
    () => route.query.history === 'archived' && activeBoardId.value !== null,
  )
  const activeBoardName = computed(() => {
    const boardId = activeBoardId.value
    return boardId && scopedBoard.value?.id === boardId ? scopedBoard.value.name : boardId ?? ''
  })
  const activeColumnName = computed(() => {
    const columnId = activeColumnId.value
    if (!columnId) return ''
    if (scopedBoard.value?.id !== activeBoardId.value) return columnId
    return scopedBoard.value.columns.find((column) => column.id === columnId)?.name ?? columnId
  })

  async function loadScopedBoard() {
    const requestGeneration = ++scopedBoardLoadGeneration
    const boardId = activeBoardId.value
    if (!boardId) {
      scopedBoard.value = null
      return
    }
    try {
      const board = await boardsApi.getBoard(boardId)
      if (requestGeneration !== scopedBoardLoadGeneration || activeBoardId.value !== boardId) return
      scopedBoard.value = board.id === boardId ? board : null
    } catch {
      // The scoped inbox remains usable when the board metadata is unavailable.
      if (requestGeneration === scopedBoardLoadGeneration && activeBoardId.value === boardId) {
        scopedBoard.value = null
      }
    }
  }

  // ---- Batch selection ----

  function toggleItemSelection(itemId: string) {
    if (isArchivedHistory.value) return
    const next = new Set(selectedIds.value)
    if (next.has(itemId)) {
      next.delete(itemId)
    } else {
      next.add(itemId)
    }
    selectedIds.value = next
  }

  function toggleSelectAll() {
    if (isArchivedHistory.value) return
    if (items.value.length > 0 && selectedIds.value.size === items.value.length) {
      selectedIds.value = new Set()
    } else {
      selectedIds.value = new Set(items.value.map((i) => i.id))
    }
  }

  function clearSelection() {
    selectedIds.value = new Set()
  }

  async function batchAction(action: 'triage' | 'ignore' | 'cancel') {
    if (isArchivedHistory.value) return
    if (selectedIds.value.size === 0) return
    const ids = Array.from(selectedIds.value)
    try {
      await captureStore.batchTriage(ids, action)
      clearSelection()
    } catch {
      // Store handles toast
    }
  }

  // ---- Suggestion editing ----

  function startEditSuggestion() {
    if (isArchivedHistory.value) return
    if (!selectedItem.value) return
    editedText.value = selectedItem.value.rawText
    editedTitleHint.value = ''
    isEditingSuggestion.value = true
  }

  function cancelEditSuggestion() {
    isEditingSuggestion.value = false
    editedText.value = ''
    editedTitleHint.value = ''
  }

  async function saveEditedSuggestion() {
    if (isArchivedHistory.value) return
    if (!selectedItemId.value || !editedText.value.trim()) return
    try {
      await captureStore.updateSuggestion(selectedItemId.value, {
        text: editedText.value.trim(),
        titleHint: editedTitleHint.value.trim() || null,
      })
      isEditingSuggestion.value = false
      editedText.value = ''
      editedTitleHint.value = ''
    } catch {
      // Store handles toast
    }
  }

  // ---- Capture modal ----

  function openCaptureModal() {
    if (isArchivedHistory.value) return
    showCaptureModal.value = true
  }

  function closeCaptureModal() {
    showCaptureModal.value = false
  }

  async function handleCaptureCreated() {
    if (isArchivedHistory.value) return
    closeCaptureModal()
    await loadInbox()
  }

  // ---- Hash / deep link handling ----

  function getCaptureIdFromHash(hash: string): string | null {
    if (!hash.startsWith('#capture-')) {
      return null
    }
    const rawId = hash.slice('#capture-'.length).trim()
    if (!rawId) {
      return null
    }
    try {
      return decodeURIComponent(rawId)
    } catch {
      return null
    }
  }

  function isHttpNotFound(error: unknown): boolean {
    const candidate = error as { response?: { status?: number; data?: { errorCode?: string } } } | null
    return candidate?.response?.status === 404 || candidate?.response?.data?.errorCode === 'NotFound'
  }

  async function clearCaptureHash() {
    if (!getCaptureIdFromHash(route.hash)) {
      return
    }
    hashLoadFailedItemId.value = null
    await router.replace({
      name: 'workspace-inbox',
      query: route.query,
    })
  }

  // ---- Selection & navigation ----

  function setActiveIndex(index: number) {
    if (index < 0 || index >= items.value.length) {
      return
    }
    activeItemIndex.value = index
  }

  function scrollActiveItemIntoView() {
    options.scrollToIndex()?.(activeItemIndex.value)
  }

  function primeSelection(itemId: string, preferredIndex?: number) {
    if (preferredIndex !== undefined) {
      setActiveIndex(preferredIndex)
    } else {
      const matchingIndex = items.value.findIndex((item) => item.id === itemId)
      if (matchingIndex >= 0) {
        setActiveIndex(matchingIndex)
      }
    }
    selectedItemId.value = itemId
  }

  type SelectItemOptions = {
    preferredIndex?: number
    preloadedDetail?: CaptureItem
    cacheSummary?: boolean
  }

  async function selectItemById(itemId: string, opts: SelectItemOptions = {}): Promise<boolean> {
    const { preferredIndex, preloadedDetail, cacheSummary = true } = opts
    // Archived history (#1973) inspects records the LIVE queues deliberately
    // omit, so a detail load there must never write back into the list.
    // `fetchDetail`'s success path calls `cacheDetail(detail, syncSummary)`, and
    // `upsertSummary` UNSHIFTS an absent summary to the top of
    // `captureStore.items` with no scope or request-generation guard
    // (`latestListLoadRequestId` covers only `fetchItems`). A detail GET started
    // in archived history that resolves AFTER the mode-exit watcher's unscoped
    // `loadInbox` would therefore seat the archived board's capture at the top
    // of the live Inbox, with Triage / Ignore / Cancel enabled against an
    // archived board — the exact boundary this surface exists to hold. The
    // detail still caches into `detailById`, so the read-only panel renders;
    // only the list write is suppressed. Paper loads through `peekDetail` and is
    // unaffected either way.
    const syncSummary = cacheSummary && !isArchivedHistory.value
    primeSelection(itemId, preferredIndex)
    hashLoadFailedItemId.value = null
    try {
      if (preloadedDetail) {
        captureStore.cacheDetail(preloadedDetail, syncSummary)
        return true
      }
      await captureStore.fetchDetail(itemId, { syncSummary })
      return true
    } catch {
      if (selectedItemId.value === itemId) {
        selectedItemId.value = null
      }
      return false
    }
  }

  async function openBoardScopedHashItem(captureId: string): Promise<void> {
    try {
      hashLoadFailedItemId.value = null
      const detail = await captureStore.peekDetail(captureId, {
        forceRefresh: true,
        recordError: false,
        showToast: false,
      })
      if (getCaptureIdFromHash(route.hash) !== captureId) {
        return
      }
      if (normalizeBoardIdQueryParam(detail.boardId) !== activeBoardId.value) {
        selectedItemId.value = null
        await clearCaptureHash()
        return
      }
      await selectItemById(captureId, {
        preloadedDetail: detail,
        cacheSummary: false,
      })
      return
    } catch (error) {
      if (getCaptureIdFromHash(route.hash) !== captureId) {
        return
      }
      if (isHttpNotFound(error)) {
        selectedItemId.value = null
        hashLoadFailedItemId.value = null
        await clearCaptureHash()
        return
      }
      selectedItemId.value = null
      hashLoadFailedItemId.value = captureId
    }
  }

  async function openItemFromHash() {
    const captureId = getCaptureIdFromHash(route.hash)
    if (!captureId) {
      hashLoadFailedItemId.value = null
      return
    }
    if (selectedItemId.value === captureId && selectedItem.value) {
      if (!activeBoardId.value || normalizeBoardIdQueryParam(selectedItem.value.boardId) === activeBoardId.value) {
        hashLoadFailedItemId.value = null
        return
      }
    }
    if (activeBoardId.value) {
      await openBoardScopedHashItem(captureId)
      return
    }
    const opened = await selectItemById(captureId)
    if (!opened) {
      await clearCaptureHash()
    }
  }

  // ---- Inbox loading ----

  async function loadInbox() {
    inboxLoadPerf.start()
    try {
      await captureStore.fetchItems({
        limit: 200,
        ...(activeBoardId.value ? { boardId: activeBoardId.value } : {}),
      })
    } catch {
      // Store handles toast + error state.
    }
    await openItemFromHash()
    inboxLoadPerf.end()
  }

  async function openItemFromList(item: CaptureItemSummary, index: number) {
    hashLoadFailedItemId.value = null
    await clearCaptureHash()
    await selectItemById(item.id, { preferredIndex: index })
  }

  async function closeDetail() {
    selectedItemId.value = null
    hashLoadFailedItemId.value = null
    await clearCaptureHash()
  }

  async function openActiveItem() {
    const target = items.value[activeItemIndex.value]
    if (!target) {
      return
    }
    await openItemFromList(target, activeItemIndex.value)
  }

  async function handleKeydown(event: KeyboardEvent) {
    if (items.value.length === 0) {
      return
    }
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      activeItemIndex.value = (activeItemIndex.value + 1) % items.value.length
      return
    }
    if (event.key === 'ArrowUp') {
      event.preventDefault()
      activeItemIndex.value = (activeItemIndex.value - 1 + items.value.length) % items.value.length
      return
    }
    if (event.key === 'Enter') {
      event.preventDefault()
      await openActiveItem()
    }
  }

  // ---- Detail actions ----

  async function ignoreSelected() {
    if (isArchivedHistory.value) return
    if (!selectedItemId.value) return
    try {
      await captureStore.ignoreItem(selectedItemId.value)
    } catch {
      // Store handles toast + error state.
    }
  }

  async function cancelSelected() {
    if (isArchivedHistory.value) return
    if (!selectedItemId.value) return
    try {
      await captureStore.cancelItem(selectedItemId.value)
    } catch {
      // Store handles toast + error state.
    }
  }

  async function triageSelected() {
    if (isArchivedHistory.value) return
    const itemId = selectedItemId.value
    if (!itemId) return

    if (stopTriagePolling) {
      stopTriagePolling()
      stopTriagePolling = null
    }

    try {
      await captureStore.triageItem(itemId)
      const latestStatus = captureStore.detailById[itemId]?.status
      if (latestStatus !== undefined && isTriageTerminalStatus(latestStatus)) {
        return
      }
      stopTriagePolling = captureStore.pollTriageCompletion(itemId)
    } catch {
      if (stopTriagePolling) {
        stopTriagePolling()
        stopTriagePolling = null
      }
      // Store handles toast + error state.
    }
  }

  async function refreshSelectedDetail() {
    if (!selectedItemId.value) return
    try {
      await captureStore.fetchDetail(selectedItemId.value, {
        forceRefresh: true,
        // Same list-write boundary as `selectItemById`. Refresh Detail is a READ
        // affordance and stays available in archived history — unlike the
        // Triage / Ignore / Cancel siblings, it writes nothing server-side — but
        // it must not seed the live list either, and `forceRefresh` means it
        // always takes the caching path rather than the cached early return.
        syncSummary: !isArchivedHistory.value,
      })
    } catch {
      // Store handles toast + error state.
    }
  }

  // ---- Routing helpers ----

  function reviewRoute(proposalId?: string, boardId?: string | null) {
    const effectiveBoardId = boardId ?? activeBoardId.value
    const query: Record<string, string> = effectiveBoardId ? { boardId: effectiveBoardId } : {}
    if (isArchivedHistory.value) query.history = 'archived'
    return {
      name: 'workspace-review',
      query: Object.keys(query).length > 0 ? query : undefined,
      hash: proposalId ? `#proposal-${encodeURIComponent(proposalId)}` : undefined,
    }
  }

  function openProposal(proposalId: string): void {
    void router.push(reviewRoute(proposalId, selectedItem.value?.boardId ?? null))
  }

  function openReview(): void {
    void router.push(reviewRoute())
  }

  function openRoute(path: string): void {
    void router.push(path)
  }

  async function clearScope(): Promise<void> {
    const query = { ...route.query }
    delete query.boardId
    delete query.columnId
    delete query.history
    await router.replace({ name: 'workspace-inbox', query })
  }

  // ---- Watchers & lifecycle ----

  watch(items, (nextItems) => {
    if (nextItems.length === 0) {
      activeItemIndex.value = 0
      return
    }
    if (selectedItemId.value) {
      const selectedIndex = nextItems.findIndex((item) => item.id === selectedItemId.value)
      if (selectedIndex >= 0) {
        activeItemIndex.value = selectedIndex
        return
      }
    }
    if (activeItemIndex.value >= nextItems.length) {
      activeItemIndex.value = nextItems.length - 1
    }
  })

  watch(activeItemIndex, async () => {
    await nextTick()
    scrollActiveItemIntoView()
  })

  function resetScopedState() {
    selectedItemId.value = null
    selectedIds.value = new Set()
    showCaptureModal.value = false
    activeItemIndex.value = 0
  }

  watch(activeBoardId, () => {
    resetScopedState()
    void loadScopedBoard()
    void loadInbox()
  })

  // Entering or leaving archived history (#1973) is a scope change of its own:
  // the two modes list different records, and a selection, batch set, or open
  // capture modal carried across the boundary would act on the wrong one. Kept
  // as a SEPARATE watcher rather than folded into the `activeBoardId` source
  // above, because the board watcher's identity is the contract the scoped
  // board-metadata race tests drive it through.
  watch(isArchivedHistory, () => {
    resetScopedState()
    void loadScopedBoard()
    void loadInbox()
  })

  watch(
    () => route.hash,
    () => {
      void openItemFromHash()
    },
  )

  watch(selectedItemId, (itemId, _, onCleanup) => {
    if (stopTriagePolling) {
      stopTriagePolling()
      stopTriagePolling = null
    }
    // Reset editing state when switching items
    isEditingSuggestion.value = false
    editedText.value = ''
    editedTitleHint.value = ''

    if (!itemId) {
      return
    }
    const unregister = registerEscapeHandler(closeDetail)
    onCleanup(() => {
      unregister()
    })
  })

  onMounted(() => {
    void loadScopedBoard()
    void loadInbox()
  })

  onUnmounted(() => {
    if (stopTriagePolling) {
      stopTriagePolling()
      stopTriagePolling = null
    }
  })

  return {
    // State
    captureStore,
    items,
    selectedItemId,
    hashLoadFailedItemId,
    activeItemIndex,
    activeDescendantId,
    selectedItem,
    activeBoardId,
    activeColumnId,
    isArchivedHistory,
    activeBoardName,
    activeColumnName,
    showCaptureModal,
    selectedIds,
    isEditingSuggestion,
    editedText,
    editedTitleHint,

    // Actions
    loadInbox,
    openItemFromList,
    setActiveIndex,
    handleKeydown,
    toggleItemSelection,
    toggleSelectAll,
    clearSelection,
    batchAction,
    openCaptureModal,
    closeCaptureModal,
    handleCaptureCreated,
    openRoute,
    clearScope,
    openReview,
    closeDetail,
    refreshSelectedDetail,
    triageSelected,
    ignoreSelected,
    cancelSelected,
    startEditSuggestion,
    cancelEditSuggestion,
    saveEditedSuggestion,
    openProposal,
  }
}
