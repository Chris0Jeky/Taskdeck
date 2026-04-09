import { defineStore } from 'pinia'
import { ref } from 'vue'
import { metricsApi } from '../api/metricsApi'
import { useToastStore } from './toastStore'
import { isDemoMode } from '../utils/demoMode'
import { getErrorDisplay } from '../composables/useErrorMapper'
import type { BoardMetricsResponse, BoardForecastResponse, MetricsQuery, ForecastQuery } from '../types/metrics'

export const useMetricsStore = defineStore('metrics', () => {
  const toast = useToastStore()

  const metrics = ref<BoardMetricsResponse | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const forecast = ref<BoardForecastResponse | null>(null)
  const forecastLoading = ref(false)
  const forecastError = ref<string | null>(null)

  async function fetchBoardMetrics(query: MetricsQuery) {
    if (isDemoMode) {
      loading.value = true
      error.value = null
      metrics.value = null
      loading.value = false
      error.value = 'Metrics are not available in demo mode.'
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

  async function fetchBoardForecast(query: ForecastQuery) {
    if (isDemoMode) {
      forecastLoading.value = true
      forecastError.value = null
      forecast.value = null
      forecastLoading.value = false
      forecastError.value = 'Forecast is not available in demo mode.'
      return
    }
    try {
      forecastLoading.value = true
      forecastError.value = null
      forecast.value = await metricsApi.getBoardForecast(query)
    } catch (e: unknown) {
      const msg = getErrorDisplay(e, 'Failed to fetch board forecast').message
      forecastError.value = msg
      toast.error(msg)
      throw e
    } finally {
      forecastLoading.value = false
    }
  }

  function $reset() {
    metrics.value = null
    loading.value = false
    error.value = null
    forecast.value = null
    forecastLoading.value = false
    forecastError.value = null
  }

  return {
    metrics,
    loading,
    error,
    forecast,
    forecastLoading,
    forecastError,
    fetchBoardMetrics,
    fetchBoardForecast,
    $reset,
  }
})
