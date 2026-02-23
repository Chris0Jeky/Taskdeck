import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { captureApi } from '../api/captureApi'
import type { CaptureItem, CaptureItemSummary, CaptureListQuery, CreateCaptureItemDto } from '../types/capture'
import { useToastStore } from './toastStore'
import { getErrorDisplay } from '../composables/useErrorMapper'

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

export const useCaptureStore = defineStore('capture', () => {
  const toast = useToastStore()

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

  async function fetchItems(query?: CaptureListQuery) {
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

  async function fetchDetail(itemId: string, forceRefresh = false) {
    if (!forceRefresh && detailById.value[itemId]) {
      return detailById.value[itemId]
    }

    try {
      loadingDetail.value = true
      detailError.value = null
      const detail = await captureApi.getItem(itemId)
      detailById.value[itemId] = detail
      upsertSummary(toSummary(detail))
      return detail
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to load inbox item').message
      detailError.value = message
      toast.error(message)
      throw e
    } finally {
      loadingDetail.value = false
    }
  }

  async function createItem(dto: CreateCaptureItemDto) {
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
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      await captureApi.ignoreItem(itemId)
      await fetchDetail(itemId, true)
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
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      await captureApi.cancelItem(itemId)
      await fetchDetail(itemId, true)
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

  async function triageItem(itemId: string) {
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      const triageResult = await captureApi.enqueueTriage(itemId)

      const existingDetail = detailById.value[itemId]
      if (existingDetail) {
        detailById.value[itemId] = {
          ...existingDetail,
          status: triageResult.status,
        }
        upsertSummary(toSummary(detailById.value[itemId]))
      } else {
        const existingSummary = items.value.find((item) => item.id === itemId)
        if (existingSummary) {
          upsertSummary({
            ...existingSummary,
            status: triageResult.status,
          })
        }
      }

      await fetchDetail(itemId, true)
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
    fetchItems,
    fetchDetail,
    createItem,
    ignoreItem,
    cancelItem,
    triageItem,
  }
})
