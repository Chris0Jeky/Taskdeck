import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { metricsApi } from '../api/metricsApi'
import { useToastStore } from './toastStore'
import { useSessionStore } from './sessionStore'
import { isDemoMode } from '../utils/demoMode'
import { getErrorDisplay } from '../composables/useErrorMapper'
import type { BoardMetricsResponse, BoardForecastResponse, MetricsQuery, ForecastQuery } from '../types/metrics'

export const useMetricsStore = defineStore('metrics', () => {
  const toast = useToastStore()
  const session = useSessionStore()

  const metrics = ref<BoardMetricsResponse | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const forecast = ref<BoardForecastResponse | null>(null)
  const forecastLoading = ref(false)
  const forecastError = ref<string | null>(null)

  interface RequestOwner {
    epoch: number
    token: symbol
  }

  let credentialEpoch = 0
  let metricsOwner: RequestOwner | null = null
  let forecastOwner: RequestOwner | null = null

  function beginMetricsRequest(): RequestOwner {
    const owner = { epoch: credentialEpoch, token: Symbol('board-metrics') }
    metricsOwner = owner
    loading.value = true
    error.value = null
    return owner
  }

  function ownsMetricsRequest(owner: RequestOwner): boolean {
    return owner.epoch === credentialEpoch && metricsOwner?.token === owner.token
  }

  function finishMetricsRequest(owner: RequestOwner): void {
    if (!ownsMetricsRequest(owner)) return
    metricsOwner = null
    loading.value = false
  }

  function beginForecastRequest(): RequestOwner {
    const owner = { epoch: credentialEpoch, token: Symbol('board-forecast') }
    forecastOwner = owner
    forecastLoading.value = true
    forecastError.value = null
    return owner
  }

  function ownsForecastRequest(owner: RequestOwner): boolean {
    return owner.epoch === credentialEpoch && forecastOwner?.token === owner.token
  }

  function finishForecastRequest(owner: RequestOwner): void {
    if (!ownsForecastRequest(owner)) return
    forecastOwner = null
    forecastLoading.value = false
  }

  function invalidateRequests(): void {
    credentialEpoch += 1
    metricsOwner = null
    forecastOwner = null
    loading.value = false
    error.value = null
    forecastLoading.value = false
    forecastError.value = null
  }

  function $reset(): void {
    invalidateRequests()
    metrics.value = null
    forecast.value = null
  }

  watch(
    () => [session.userId, session.isAuthenticated, session.isDemo],
    $reset,
    { flush: 'sync' },
  )

  watch(
    () => session.token,
    invalidateRequests,
    { flush: 'sync' },
  )

  async function fetchBoardMetrics(query: MetricsQuery) {
    if (isDemoMode) {
      metricsOwner = null
      loading.value = false
      error.value = 'Metrics are not available in demo mode.'
      metrics.value = null
      return
    }

    const owner = beginMetricsRequest()
    try {
      const result = await metricsApi.getBoardMetrics(query)
      if (!ownsMetricsRequest(owner)) return
      metrics.value = result
    } catch (e: unknown) {
      if (ownsMetricsRequest(owner)) {
        const msg = getErrorDisplay(e, 'Failed to fetch board metrics').message
        error.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishMetricsRequest(owner)
    }
  }

  async function fetchBoardForecast(query: ForecastQuery) {
    if (isDemoMode) {
      forecastOwner = null
      forecastLoading.value = false
      forecastError.value = 'Forecast is not available in demo mode.'
      forecast.value = null
      return
    }

    const owner = beginForecastRequest()
    try {
      const result = await metricsApi.getBoardForecast(query)
      if (!ownsForecastRequest(owner)) return
      forecast.value = result
    } catch (e: unknown) {
      if (ownsForecastRequest(owner)) {
        const msg = getErrorDisplay(e, 'Failed to fetch board forecast').message
        forecastError.value = msg
        toast.error(msg)
      }
      throw e
    } finally {
      finishForecastRequest(owner)
    }
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
