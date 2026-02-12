import { defineStore } from 'pinia'
import { ref } from 'vue'
import { auditApi } from '../api/auditApi'
import { useToastStore } from './toastStore'
import type { AuditEntry } from '../types/audit'

export const useAuditStore = defineStore('audit', () => {
  const toast = useToastStore()

  const entries = ref<AuditEntry[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetchBoardHistory(boardId: string, limit = 50) {
    try {
      loading.value = true
      error.value = null
      entries.value = await auditApi.getBoardHistory(boardId, limit)
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to fetch board history')
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
      entries.value = await auditApi.getEntityHistory(entityType, entityId, limit)
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to fetch entity history')
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
      entries.value = await auditApi.getUserHistory(userId, limit)
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to fetch user history')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  function getErrorMessage(err: unknown, fallback: string): string {
    if (typeof err !== 'object' || err === null) return fallback
    const typed = err as { response?: { data?: { message?: unknown } }; message?: unknown }
    const responseMessage = typed.response?.data?.message
    if (typeof responseMessage === 'string' && responseMessage.trim().length > 0) return responseMessage
    if (typeof typed.message === 'string' && typed.message.trim().length > 0) return typed.message
    return fallback
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
