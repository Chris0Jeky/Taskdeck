import { defineStore } from 'pinia'
import { ref } from 'vue'
import { auditApi } from '../api/auditApi'
import { useToastStore } from './toastStore'
import type { AuditEntry } from '../types/audit'
import { getErrorDisplay } from '../composables/useErrorMapper'

export const useAuditStore = defineStore('audit', () => {
  const toast = useToastStore()

  const entries = ref<AuditEntry[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  function clampLimit(limit: number): number {
    if (limit < 1) return 1
    if (limit > 100) return 100
    return limit
  }

  async function fetchBoardHistory(boardId: string, limit = 50) {
    try {
      loading.value = true
      error.value = null
      entries.value = await auditApi.getBoardHistory(boardId, clampLimit(limit))
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to fetch board history').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function fetchEntityHistory(entityType: string, entityId: string, limit = 50) {
    try {
      loading.value = true
      error.value = null
      entries.value = await auditApi.getEntityHistory(entityType, entityId, clampLimit(limit))
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to fetch entity history').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function fetchUserHistory(userId: string, limit = 50) {
    try {
      loading.value = true
      error.value = null
      entries.value = await auditApi.getUserHistory(userId, clampLimit(limit))
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to fetch user history').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }
  return {
    entries,
    loading,
    error,
    fetchBoardHistory,
    fetchEntityHistory,
    fetchUserHistory,
  }
})
