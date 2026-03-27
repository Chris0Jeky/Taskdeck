import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { captureApi } from '../api/captureApi'
import { isTriageTerminalStatus } from '../types/capture'
import type { CaptureItem, CaptureItemSummary, CaptureListQuery, CreateCaptureItemDto } from '../types/capture'
import { useToastStore } from './toastStore'
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
  }
}

type DetailLoadOptions = {
  forceRefresh?: boolean
  recordError?: boolean
  showToast?: boolean
  syncSummary?: boolean
}

export const useCaptureStore = defineStore('capture', () => {
  const toast = useToastStore()

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
    if (isDemoMode) {
      loadingList.value = true
      listError.value = null
      items.value = buildDemoCaptureItems()
      loadingList.value = false
      return
    }

    try {
      loadingList.value = true
      listError.value = null
      items.value = await captureApi.listItems(query)
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to load inbox items').message
      listError.value = message
      toast.error(message)
      throw e
    } finally {
      loadingList.value = false
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

  async function createItem(dto: CreateCaptureItemDto) {
    guardDemoMutation()
    try {
      actionError.value = null
      const created = await captureApi.createItem(dto)
      detailById.value[created.id] = created
      upsertSummary(toSummary(created))
      toast.success('Capture saved to inbox')
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
    const MAX_POLLS = 15
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

  async function triageItem(itemId: string) {
    guardDemoMutation()
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      const triageResult = await captureApi.enqueueTriage(itemId)

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
      toast.success(triageResult.alreadyTriaging ? 'Capture item is already triaging' : 'Capture item triage queued')
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
  }
})
