import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useCaptureStore } from '../store/captureStore'
import { isTriageTerminalStatus } from '../types/capture'
import type { CaptureItem, CaptureItemSummary } from '../types/capture'
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

  // Batch selection state
  const selectedIds = ref<Set<string>>(new Set())
  const isEditingSuggestion = ref(false)
  const editedText = ref('')
  const editedTitleHint = ref('')

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

  // ---- Batch selection ----

  function toggleItemSelection(itemId: string) {
    const next = new Set(selectedIds.value)
    if (next.has(itemId)) {
      next.delete(itemId)
    } else {
      next.add(itemId)
    }
    selectedIds.value = next
  }

  function toggleSelectAll() {
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
    showCaptureModal.value = true
  }

  function closeCaptureModal() {
    showCaptureModal.value = false
  }

  async function handleCaptureCreated() {
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
    primeSelection(itemId, preferredIndex)
    hashLoadFailedItemId.value = null
    try {
      if (preloadedDetail) {
        captureStore.cacheDetail(preloadedDetail, cacheSummary)
        return true
      }
      await captureStore.fetchDetail(itemId)
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
    if (!selectedItemId.value) return
    try {
      await captureStore.ignoreItem(selectedItemId.value)
    } catch {
      // Store handles toast + error state.
    }
  }

  async function cancelSelected() {
    if (!selectedItemId.value) return
    try {
      await captureStore.cancelItem(selectedItemId.value)
    } catch {
      // Store handles toast + error state.
    }
  }

  async function triageSelected() {
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
      await captureStore.fetchDetail(selectedItemId.value, { forceRefresh: true })
    } catch {
      // Store handles toast + error state.
    }
  }

  // ---- Routing helpers ----

  function reviewRoute(proposalId?: string, boardId?: string | null) {
    const effectiveBoardId = boardId ?? activeBoardId.value
    return {
      name: 'workspace-review',
      query: effectiveBoardId ? { boardId: effectiveBoardId } : undefined,
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

  watch(activeBoardId, () => {
    selectedItemId.value = null
    activeItemIndex.value = 0
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
