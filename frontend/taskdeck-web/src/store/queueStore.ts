import { defineStore } from 'pinia'
import { ref } from 'vue'
import { queueApi } from '../api/queueApi'
import { useToastStore } from './toastStore'
import { useSessionStore } from './sessionStore'
import { isDemoMode, DemoModeError } from '../utils/demoMode'
import type { QueueRequest, CreateQueueRequestDto, QueueStats } from '../types/queue'
import { getErrorDisplay } from '../composables/useErrorMapper'

export const useQueueStore = defineStore('queue', () => {
  const toast = useToastStore()
  const session = useSessionStore()

  const requests = ref<QueueRequest[]>([])
  const stats = ref<QueueStats | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  function guardDemoMutation(): never | void {
    if (isDemoMode) {
      toast.info('This action is view-only in demo mode.')
      throw new DemoModeError()
    }
  }

  async function fetchUserRequests() {
    if (isDemoMode) {
      loading.value = true
      error.value = null
      requests.value = []
      loading.value = false
      return
    }
    try {
      loading.value = true
      error.value = null
      session.requireUserId('queue operations')
      requests.value = await queueApi.getUserRequests()
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to fetch queue requests').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function fetchByStatus(status: string) {
    if (isDemoMode) {
      loading.value = true
      error.value = null
      requests.value = []
      loading.value = false
      return
    }
    try {
      loading.value = true
      error.value = null
      requests.value = await queueApi.getRequestsByStatus(status)
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to fetch requests by status').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function submitRequest(dto: CreateQueueRequestDto) {
    guardDemoMutation()
    try {
      loading.value = true
      error.value = null
      session.requireUserId('queue operations')
      const request = await queueApi.createRequest(dto)
      requests.value.push(request)
      toast.success('Request submitted')
      return request
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to submit request').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function cancelRequest(requestId: string) {
    guardDemoMutation()
    try {
      loading.value = true
      error.value = null
      session.requireUserId('queue operations')
      await queueApi.cancelRequest(requestId)
      requests.value = requests.value.filter(r => r.id !== requestId)
      toast.success('Request cancelled')
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to cancel request').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function processNext() {
    guardDemoMutation()
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
      const msg = getErrorDisplay(e, 'Failed to process request').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function fetchStats() {
    if (isDemoMode) {
      loading.value = true
      error.value = null
      stats.value = { pendingCount: 0, processingCount: 0, completedCount: 0, failedCount: 0 }
      loading.value = false
      return
    }
    try {
      loading.value = true
      error.value = null
      stats.value = await queueApi.getStats()
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to fetch queue stats').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
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
