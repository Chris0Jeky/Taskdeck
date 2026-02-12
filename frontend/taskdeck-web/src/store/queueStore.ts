import { defineStore } from 'pinia'
import { ref } from 'vue'
import { queueApi } from '../api/queueApi'
import { useToastStore } from './toastStore'
import { useSessionStore } from './sessionStore'
import type { QueueRequest, CreateQueueRequestDto, QueueStats } from '../types/queue'

export const useQueueStore = defineStore('queue', () => {
  const toast = useToastStore()
  const session = useSessionStore()

  const requests = ref<QueueRequest[]>([])
  const stats = ref<QueueStats | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetchUserRequests() {
    try {
      loading.value = true
      error.value = null
      const uid = session.userId ?? ''
      requests.value = await queueApi.getUserRequests(uid)
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to fetch queue requests')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function fetchByStatus(status: string) {
    try {
      loading.value = true
      error.value = null
      requests.value = await queueApi.getRequestsByStatus(status)
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to fetch requests by status')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function submitRequest(dto: CreateQueueRequestDto) {
    try {
      loading.value = true
      error.value = null
      const uid = session.userId ?? ''
      const request = await queueApi.createRequest(dto, uid)
      requests.value.push(request)
      toast.success('Request submitted')
      return request
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to submit request')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function cancelRequest(requestId: string) {
    try {
      loading.value = true
      error.value = null
      const uid = session.userId ?? ''
      await queueApi.cancelRequest(requestId, uid)
      requests.value = requests.value.filter(r => r.id !== requestId)
      toast.success('Request cancelled')
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to cancel request')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function processNext() {
    try {
      loading.value = true
      error.value = null
      const result = await queueApi.processNext()
      if (result) {
        toast.success('Request processed')
      } else {
        toast.info('No pending requests')
      }
      return result
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to process request')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function fetchStats() {
    try {
      loading.value = true
      error.value = null
      stats.value = await queueApi.getStats()
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to fetch queue stats')
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
    requests,
    stats,
    loading,
    error,
    fetchUserRequests,
    fetchByStatus,
    submitRequest,
    cancelRequest,
    processNext,
    fetchStats,
  }
})
