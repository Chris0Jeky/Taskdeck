import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { captureApi } from '../api/captureApi'
import { isTriageTerminalStatus } from '../types/capture'
import type { BatchTriageAction, BatchTriageResult, CaptureItem, CaptureItemSummary, CaptureListQuery, CreateCaptureItemDto, UpdateCaptureSuggestionDto } from '../types/capture'
import { useToastStore } from './toastStore'
import { useWorkspaceStore } from './workspaceStore'
import { getErrorDisplay } from '../composables/useErrorMapper'
import { isDemoMode, DemoModeError } from '../utils/demoMode'
import { buildDemoCaptureItems } from '../utils/demoData'

function toSummary(item: CaptureItem): CaptureItemSummary {
  return {
    id: item.id,
    userId: item.userId,
    boardId: item.boardId,
    status: item.status,
    source: item.source,
    textExcerpt: item.textExcerpt,
    createdAt: item.createdAt,
    processedAt: item.processedAt,
    errorMessage: item.errorMessage ?? null,
  }
}

type DetailLoadOptions = {
  forceRefresh?: boolean
  recordError?: boolean
  showToast?: boolean
  syncSummary?: boolean
}

type CreateItemOptions = {
  /**
   * Whether the badge refresh fires after the capture lands (default true).
   *
   * Pass `false` from a caller that runs its OWN `fetchHomeSummary()` next:
   * both read GET /workspace/home — the heaviest endpoint on the surface —
   * and the full summary already rewrites `workload`, so leaving the notify
   * on would fetch it twice for one keystroke. The full fetch is what those
   * callers need anyway: a capture also moves `onboarding` (the
   * `capture-first-item` milestone is `TotalCaptures > 0` server-side) and
   * `recommendedActions`, neither of which the workload-only refresh carries.
   */
  refreshWorkload?: boolean
}

export const useCaptureStore = defineStore('capture', () => {
  const toast = useToastStore()
  const workspace = useWorkspaceStore()

  /**
   * Tell the sidebar badges a capture's triage state moved (#1974).
   *
   * The badges read a server-computed workload count (`New + Failed`), which
   * is fetched once per session; without this the badge kept a pre-mutation
   * number until a full page reload. Fire-and-forget on purpose: the mutation
   * has already succeeded and its own toast has already been shown, so a badge
   * that refreshes a beat later must never delay or fail the action.
   *
   * Called only after a mutation that can change `New + Failed`:
   * create (+1), triage (-1), ignore/cancel (-1), batch, and a triage poll
   * reaching a terminal status (`Failed` puts one back).
   */
  function notifyTriageCountChanged() {
    void workspace.refreshWorkloadCounts()
  }

  function guardDemoMutation(): never | void {
    if (isDemoMode) {
      toast.info('This action is view-only in demo mode.')
      throw new DemoModeError()
    }
  }

  const items = ref<CaptureItemSummary[]>([])
  const detailById = ref<Record<string, CaptureItem>>({})
  const loadingList = ref(false)
  const loadingDetail = ref(false)
  let latestListLoadRequestId = 0
  const actionBusyItemId = ref<string | null>(null)
  const listError = ref<string | null>(null)
  const detailError = ref<string | null>(null)
  const actionError = ref<string | null>(null)

  const hasItems = computed(() => items.value.length > 0)

  function upsertSummary(summary: CaptureItemSummary) {
    const existingIndex = items.value.findIndex((item) => item.id === summary.id)
    if (existingIndex >= 0) {
      items.value[existingIndex] = summary
      return
    }

    items.value.unshift(summary)
  }

  function cacheDetail(detail: CaptureItem, syncSummary = true) {
    detailById.value[detail.id] = detail
    if (syncSummary) {
      upsertSummary(toSummary(detail))
    }
  }

  async function fetchItems(query?: CaptureListQuery) {
    const requestId = ++latestListLoadRequestId
    if (isDemoMode) {
      loadingList.value = true
      listError.value = null
      if (requestId === latestListLoadRequestId) {
        items.value = buildDemoCaptureItems()
        loadingList.value = false
      }
      return
    }

    try {
      loadingList.value = true
      listError.value = null
      const loadedItems = await captureApi.listItems(query)
      if (requestId !== latestListLoadRequestId) return
      items.value = loadedItems
    } catch (e: unknown) {
      if (requestId !== latestListLoadRequestId) return
      const message = getErrorDisplay(e, 'Failed to load inbox items').message
      listError.value = message
      toast.error(message)
      throw e
    } finally {
      if (requestId === latestListLoadRequestId) {
        loadingList.value = false
      }
    }
  }

  async function fetchDetail(itemId: string, options: DetailLoadOptions = {}) {
    const {
      forceRefresh = false,
      recordError = true,
      showToast = true,
      syncSummary = true,
    } = options

    if (!forceRefresh && detailById.value[itemId]) {
      return detailById.value[itemId]
    }

    if (isDemoMode) {
      const summary = items.value.find((i) => i.id === itemId)
      if (summary) {
        const detail = { ...summary, rawText: summary.textExcerpt, retryCount: 0, provenance: null }
        cacheDetail(detail, syncSummary)
        return detail
      }
    }

    try {
      loadingDetail.value = true
      if (recordError) {
        detailError.value = null
      }
      const detail = await captureApi.getItem(itemId)
      cacheDetail(detail, syncSummary)
      return detail
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to load inbox item').message
      if (recordError) {
        detailError.value = message
      }
      if (showToast) {
        toast.error(message)
      }
      throw e
    } finally {
      loadingDetail.value = false
    }
  }

  async function peekDetail(itemId: string, options: DetailLoadOptions = {}) {
    const {
      forceRefresh = false,
      recordError = true,
      showToast = true,
    } = options

    if (!forceRefresh && detailById.value[itemId]) {
      return detailById.value[itemId]
    }

    try {
      loadingDetail.value = true
      if (recordError) {
        detailError.value = null
      }
      return await captureApi.getItem(itemId)
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to load inbox item').message
      if (recordError) {
        detailError.value = message
      }
      if (showToast) {
        toast.error(message)
      }
      throw e
    } finally {
      loadingDetail.value = false
    }
  }

  async function createItem(dto: CreateCaptureItemDto, options: CreateItemOptions = {}) {
    guardDemoMutation()
    try {
      actionError.value = null
      const created = await captureApi.createItem(dto)
      detailById.value[created.id] = created
      upsertSummary(toSummary(created))
      // SAVED, not APPLIED (#1970): a capture sitting in the inbox has touched
      // no board. Duration stays the store default; only the stamp is named.
      toast.success('Capture saved to inbox', undefined, { label: 'saved' })
      if (options.refreshWorkload !== false) {
        notifyTriageCountChanged()
      }
      return created
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to capture item').message
      actionError.value = message
      toast.error(message)
      throw e
    }
  }

  async function ignoreItem(itemId: string) {
    guardDemoMutation()
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      await captureApi.ignoreItem(itemId)
      await fetchDetail(itemId, { forceRefresh: true })
      toast.success('Capture item ignored')
      notifyTriageCountChanged()
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to ignore capture item').message
      actionError.value = message
      toast.error(message)
      throw e
    } finally {
      actionBusyItemId.value = null
    }
  }

  async function cancelItem(itemId: string) {
    guardDemoMutation()
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      await captureApi.cancelItem(itemId)
      await fetchDetail(itemId, { forceRefresh: true })
      toast.success('Capture item cancelled')
      notifyTriageCountChanged()
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to cancel capture item').message
      actionError.value = message
      toast.error(message)
      throw e
    } finally {
      actionBusyItemId.value = null
    }
  }

  const triagePollingItemId = ref<string | null>(null)
  let activeTriagePollStop: (() => void) | null = null

  function pollTriageCompletion(itemId: string): () => void {
    const POLL_INTERVAL_MS = 2_000
    // About 15 minutes at the normal cadence; #1585 owns provider-aware elapsed-time policy.
    const MAX_POLLS = 450
    let pollCount = 0
    let stopped = false
    let timerId: ReturnType<typeof setTimeout> | null = null

    if (activeTriagePollStop) {
      activeTriagePollStop()
    }

    triagePollingItemId.value = itemId

    async function tick() {
      if (stopped) return
      pollCount++

      try {
        const detail = await captureApi.getItem(itemId)
        if (stopped) return
        cacheDetail(detail)

        if (isTriageTerminalStatus(detail.status)) {
          // Triage finished while the user watched: the badge moves again here
          // (a `Failed` outcome puts the capture back into the pending count).
          notifyTriageCountChanged()
          stop()
          return
        }
      } catch {
        // Silently retry on transient errors; the manual refresh button is still available.
      }

      if (!stopped && pollCount < MAX_POLLS) {
        timerId = setTimeout(tick, POLL_INTERVAL_MS)
      } else {
        stop()
      }
    }

    function stop() {
      stopped = true
      if (timerId !== null) {
        clearTimeout(timerId)
        timerId = null
      }
      if (activeTriagePollStop === stop) {
        activeTriagePollStop = null
      }
      if (triagePollingItemId.value === itemId) {
        triagePollingItemId.value = null
      }
    }

    activeTriagePollStop = stop
    timerId = setTimeout(tick, POLL_INTERVAL_MS)
    return stop
  }

  async function triageItem(itemId: string, boardId?: string | null) {
    guardDemoMutation()
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      const triageResult = await captureApi.enqueueTriage(itemId, boardId)

      const existingDetail = detailById.value[itemId]
      const existingSummary = items.value.find((item) => item.id === itemId)
      let optimisticDetail: CaptureItem | null = null
      if (existingDetail) {
        optimisticDetail = {
          ...existingDetail,
          status: triageResult.status,
        }
        detailById.value[itemId] = optimisticDetail
      }

      if (existingSummary) {
        upsertSummary({
          ...existingSummary,
          status: triageResult.status,
        })
      } else if (optimisticDetail) {
        upsertSummary(toSummary(optimisticDetail))
      }

      await fetchDetail(itemId, { forceRefresh: true, showToast: false })
      // QUEUED (#1970): triage has been enqueued, not run and not applied.
      // Both branches are the same outcome class — the queue already holds it.
      toast.success(
        triageResult.alreadyTriaging ? 'Capture item is already triaging' : 'Capture item triage queued',
        undefined,
        { label: 'queued' },
      )
      notifyTriageCountChanged()
      return triageResult
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to triage capture item').message
      actionError.value = message
      toast.error(message)
      throw e
    } finally {
      actionBusyItemId.value = null
    }
  }

  const batchBusy = ref(false)
  const batchError = ref<string | null>(null)

  async function batchTriage(itemIds: string[], action: BatchTriageAction): Promise<BatchTriageResult> {
    guardDemoMutation()
    try {
      batchBusy.value = true
      batchError.value = null
      actionError.value = null

      const batchItems = itemIds.map((id) => ({ itemId: id, action }))
      const result = await captureApi.batchTriage(batchItems)

      if (result.succeeded > 0) {
        toast.success(`${result.succeeded} of ${result.total} items processed`)
      }
      if (result.failed > 0) {
        const failedMessages = result.results
          .filter((r) => !r.success)
          .map((r) => r.errorMessage ?? 'Unknown error')
          .slice(0, 3)
          .join('; ')
        toast.error(`${result.failed} item(s) failed: ${failedMessages}`)
      }

      // Refresh list to pick up status changes
      await fetchItems()
      notifyTriageCountChanged()

      return result
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to process batch triage').message
      batchError.value = message
      toast.error(message)
      throw e
    } finally {
      batchBusy.value = false
    }
  }

  async function updateSuggestion(itemId: string, dto: UpdateCaptureSuggestionDto): Promise<CaptureItem> {
    guardDemoMutation()
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      const updated = await captureApi.updateSuggestion(itemId, dto)
      cacheDetail(updated)
      // SAVED, not APPLIED (GH-1970): correcting capture text or metadata
      // rewrites the capture and nothing else — no triage ran, no board was
      // touched. The stamp has to say so now that Paper renders it (GH-1951,
      // GH-2005).
      toast.success('Capture updated', undefined, { label: 'saved' })
      return updated
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to update capture').message
      actionError.value = message
      toast.error(message)
      throw e
    } finally {
      actionBusyItemId.value = null
    }
  }

  return {
    items,
    detailById,
    loadingList,
    loadingDetail,
    actionBusyItemId,
    listError,
    detailError,
    actionError,
    hasItems,
    batchBusy,
    batchError,
    cacheDetail,
    fetchItems,
    fetchDetail,
    peekDetail,
    createItem,
    ignoreItem,
    cancelItem,
    triageItem,
    triagePollingItemId,
    pollTriageCompletion,
    batchTriage,
    updateSuggestion,
  }
})
