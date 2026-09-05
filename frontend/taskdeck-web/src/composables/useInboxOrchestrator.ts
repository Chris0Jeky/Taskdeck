import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useCaptureStore } from '../store/captureStore'
import type { DetailCacheOutcome } from '../store/captureStore'
import { boardsApi } from '../api/boardsApi'
import { isTriageTerminalStatus } from '../types/capture'
import type { CaptureItem, CaptureItemSummary, CaptureListQuery } from '../types/capture'
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
  const activeBatchTriagePollStops = new Set<() => void>()
  let batchActionGeneration = 0
  let scopedBoardLoadGeneration = 0
  let latestInboxLoadRequestId = 0
  const isScopeReplacement = ref(false)
  /**
   * Which scope a raised `isScopeReplacement` belongs to, and whether that
   * replacement has already spent its one repair load (#2591).
   *
   * Orchestrator bookkeeping rather than a store observable on purpose. The
   * store keeps its list request id private and publishes only `items`,
   * `loadingList` and `listError` — and `items` also moves on writes that
   * prove nothing about the current scope (optimistic summary writes, the
   * batch poll's reconciliation reader), so watching it would clear the flag
   * without the new scope's rows ever having landed: the #2501 harm. The
   * `applied` boolean from `fetchItems` stays the only evidence that a list
   * response was written, and this record is what tells a LATER load that the
   * response it is about to report on is the one an outstanding replacement
   * was waiting for.
   */
  let scopeReplacementLatch: { scopeKey: string; repairIssued: boolean } | null = null

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

  /**
   * The list query for the Inbox's CURRENT scope — the one every list read has
   * to use, not just the explicit load (#2570). Read it late (see the thunk in
   * `batchAction`); the scope can change between a read being planned and
   * issued.
   *
   * Archived history carries no key of its own because `CaptureListQuery` has
   * none (`types/capture.ts`): it is board id and limit only. That is sound
   * today because archived history is always board-scoped, so `boardId` already
   * selects the archived board's captures. Adding a `history` key to the query
   * type has to revisit this helper — `loadInboxInternal` calls it in archived
   * mode too, so the omission is not covered by `batchAction`'s early return.
   */
  function currentListQuery(): CaptureListQuery {
    return {
      limit: 200,
      ...(activeBoardId.value ? { boardId: activeBoardId.value } : {}),
    }
  }

  /**
   * The Inbox's current scope as a comparable key. Two list loads answer the
   * same question only when their keys match; a load whose key no longer
   * matches the current one must not touch scope-replacement state.
   */
  function currentScopeKey(): string {
    return JSON.stringify({
      boardId: activeBoardId.value,
      archived: isArchivedHistory.value,
    })
  }

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

  function cancelBatchTriagePolling() {
    batchActionGeneration += 1
    for (const stop of activeBatchTriagePollStops) {
      stop()
    }
    activeBatchTriagePollStops.clear()
  }

  async function batchAction(action: 'triage' | 'ignore' | 'cancel') {
    if (isArchivedHistory.value) return
    if (selectedIds.value.size === 0) return
    const ids = Array.from(selectedIds.value)
    const actionGeneration = ++batchActionGeneration
    try {
      // The store re-reads the list after the batch. Hand it this Inbox's
      // scope so a board-scoped list is not replaced by the unscoped one
      // (#2570) — for ignore and cancel no poll follows to repair it.
      //
      // A THUNK, not a value: the scope can move while the POST is in flight,
      // and the store resolves this immediately before it issues the read, so
      // the read follows the user to the new board instead of racing the new
      // board's own load and winning it with the old board's rows.
      const result = await captureStore.batchTriage(ids, action, currentListQuery)
      // A board/history scope change invalidates both the selection and the
      // question this response answers. Never start an old scope's poll late.
      if (actionGeneration !== batchActionGeneration) return
      clearSelection()
      if (action !== 'triage') return
      const queuedIds = result.results
        .filter((item) => item.success)
        .map((item) => item.itemId)
      if (queuedIds.length === 0) return
      activeBatchTriagePollStops.add(
        captureStore.pollBatchTriageCompletion(queuedIds, currentListQuery()),
      )
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
    // Both halves of the selection. `selectedItemId` drives `aria-selected` and
    // the detail panel; `activeItemIndex` drives the active row and
    // `aria-activedescendant`. Restoring one without the other would leave the
    // list pointing at a row the panel is not showing.
    const previousSelectedItemId = selectedItemId.value
    const previousActiveItemIndex = activeItemIndex.value
    primeSelection(itemId, preferredIndex)
    hashLoadFailedItemId.value = null
    try {
      if (preloadedDetail) {
        captureStore.cacheDetail(preloadedDetail, syncSummary)
        return true
      }
      // `onCacheOutcome` is the store reporting what it did with THIS read
      // (#2640). `fetchDetail` resolves with the body it fetched on three paths
      // that write nothing, so resolution alone is not evidence the panel has a
      // detail to render: returning `true` regardless left `selectedItemId` on
      // an id `detailById` holds nothing for, which `InboxDetailPanel` renders
      // as "Unable to load capture detail." for a read that succeeded. A store
      // that reports nothing is read as `cached`, the behaviour before #2640.
      // A holder, not a bare `let`: TypeScript's control-flow analysis does not
      // track an assignment made inside the callback and would narrow a plain
      // variable to its initializer for every compare below.
      const detailRead: { outcome: DetailCacheOutcome } = { outcome: 'cached' }
      const report = (reported: DetailCacheOutcome) => { detailRead.outcome = reported }
      await captureStore.fetchDetail(itemId, { syncSummary, onCacheOutcome: report })

      // A `generation` drop is a LIVE-session case, not a post-logout one: a
      // first open observes generation 0 for an uncached item, and a batch
      // triage that includes it moves that generation while the read is in
      // flight. `refreshTerminalDetails` skips such an item — it has a list row
      // and no cached detail — so unlike a detail-path write, the batch write
      // has cached no body of its own and there is nothing here to render. Not
      // re-reading turned the click into a silent no-op and stripped a live
      // deep link. Re-read ONCE, against the generation that caused the drop;
      // a second collision in that window is left to the user's next click
      // rather than looped over.
      if (detailRead.outcome === 'generation' && !captureStore.detailById[itemId]) {
        detailRead.outcome = 'cached'
        await captureStore.fetchDetail(itemId, { syncSummary, onCacheOutcome: report })
      }

      // `superseded` means a newer read for the same id is already the
      // authority, so the selection stands. Only the logout case is terminal:
      // there is no newer read coming and no body to show. It is not a failure
      // either, so restore the selection the user had — both halves of it —
      // rather than clearing it, and report the read as not opened.
      if (detailRead.outcome === 'epoch' && !captureStore.detailById[itemId]) {
        if (selectedItemId.value === itemId) {
          selectedItemId.value = previousSelectedItemId
          activeItemIndex.value = previousActiveItemIndex
        }
        return false
      }
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

  async function loadInboxInternal(scopeReplacement = false) {
    const requestId = ++latestInboxLoadRequestId
    const requestScopeKey = currentScopeKey()
    if (scopeReplacement) {
      isScopeReplacement.value = true
      // A fresh replacement, and a fresh repair budget with it. The repair
      // below is a PLAIN load, so it can never refill its own budget.
      scopeReplacementLatch = { scopeKey: requestScopeKey, repairIssued: false }
    }
    inboxLoadPerf.start()
    let repairDroppedReplacement = false
    try {
      // `applied` is the store reporting that THIS call's response was written
      // into `items` (#2501). `fetchItems` resolves without writing anything
      // when its own request id has been superseded, so resolution alone is not
      // evidence the new scope's rows arrived. Clearing the flag on resolution
      // alone un-hid the retained OLD-scope rows under the NEW scope's label —
      // the exact state this flag exists to prevent. The request-id and
      // scope-key checks below stay: they guard against a stale caller, while
      // `applied` guards against a dropped response.
      const applied = await captureStore.fetchItems(currentListQuery())
      // Still the latest load, still answering the scope the user is looking
      // at. A stale load reports on nothing.
      const isLatestForThisScope =
        requestId === latestInboxLoadRequestId && requestScopeKey === currentScopeKey()
      if (applied && isLatestForThisScope) {
        isScopeReplacement.value = false
        scopeReplacementLatch = null
      } else if (
        !applied &&
        isLatestForThisScope &&
        scopeReplacementLatch?.scopeKey === requestScopeKey
      ) {
        // A dropped response under an outstanding replacement for THIS scope
        // (#2591). Nothing else will clear the flag: the read that superseded
        // this one is the store's own post-batch refresh, whose `applied`
        // result the store discards, and no later orchestrator load is coming
        // (the id check above proved that). Left alone the flag latched
        // forever, and `PaperTriageTable` then hid the rows AND their count
        // with no Retry — an empty body under a count-free eyebrow.
        //
        // So: repair the scope ONCE with the current scope's query. The flag
        // stays raised while that read is in flight, so the table shows its
        // loading state rather than the wrong scope's rows.
        if (scopeReplacementLatch.repairIssued) {
          // The repair was dropped too. Do not loop. Clear the flag instead and
          // let the table state the truth it has: the store's rows with their
          // count, its empty state, or its error surface with Retry. Every
          // read that can supersede this one resolves the SAME
          // `currentListQuery` thunk at the moment it is issued (see
          // `batchAction` and `captureStore.batchTriage`), so the rows that won
          // are this scope's rows, not the retained previous scope's.
          isScopeReplacement.value = false
          scopeReplacementLatch = null
        } else {
          scopeReplacementLatch.repairIssued = true
          repairDroppedReplacement = true
        }
      }
    } catch {
      // Store handles toast + error state. The flag stays raised deliberately:
      // a failed load leaves the previous scope's rows in the store, and the
      // table's error surface already carries a Retry.
    }
    await openItemFromHash()
    inboxLoadPerf.end()
    if (repairDroppedReplacement) {
      // A plain load: it must not restart the replacement, only finish it.
      await loadInboxInternal()
    }
  }

  function loadInbox() {
    return loadInboxInternal()
  }

  function loadInboxForScopeReplacement() {
    return loadInboxInternal(true)
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
    cancelBatchTriagePolling()
    selectedItemId.value = null
    selectedIds.value = new Set()
    showCaptureModal.value = false
    activeItemIndex.value = 0
  }

  watch(activeBoardId, () => {
    resetScopedState()
    void loadScopedBoard()
    void loadInboxForScopeReplacement()
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
    void loadInboxForScopeReplacement()
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
    // Mount is always a fresh scope: captureStore.items can still hold rows from a
    // previously visited board (the store is not reset on route leave), so the first
    // load must replace scope rather than retain stale rows on failure.
    void loadInboxForScopeReplacement()
  })

  onUnmounted(() => {
    cancelBatchTriagePolling()
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
    isScopeReplacement,
    activeBoardName,
    activeColumnName,
    showCaptureModal,
    selectedIds,
    isEditingSuggestion,
    editedText,
    editedTitleHint,

    // Actions
    loadInbox,
    loadInboxForScopeReplacement,
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
