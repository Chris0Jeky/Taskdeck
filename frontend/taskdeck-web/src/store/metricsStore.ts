import { defineStore } from 'pinia'
import { ref } from 'vue'
import { metricsApi } from '../api/metricsApi'
import { useToastStore } from './toastStore'
import { isDemoMode } from '../utils/demoMode'
import { getErrorDisplay } from '../composables/useErrorMapper'
import type { BoardMetricsResponse, MetricsQuery } from '../types/metrics'

export const useMetricsStore = defineStore('metrics', () => {
  const toast = useToastStore()

  const metrics = ref<BoardMetricsResponse | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetchBoardMetrics(query: MetricsQuery) {
    if (isDemoMode) {
      loading.value = true
      error.value = null
      metrics.value = null
      loading.value = false
      return
    }
    try {
      loading.value = true
      error.value = null
      metrics.value = await metricsApi.getBoardMetrics(query)
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to fetch board metrics').message
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  function $reset() {
    metrics.value = null
    loading.value = false
    error.value = null
  }

  return {
    metrics,
    loading,
    error,
    fetchBoardMetrics,
    $reset,
  }
})
