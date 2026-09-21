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

  type ReadRetry = () => Promise<void>

  interface RequestOwner {
    epoch: number
    token: symbol
  }

  let credentialEpoch = 0
  let metricsOwner: RequestOwner | null = null
  let forecastOwner: RequestOwner | null = null
  let metricsRetry: ReadRetry | null = null
  let forecastRetry: ReadRetry | null = null

  function beginMetricsRequest(retry: ReadRetry): RequestOwner {
    const owner = { epoch: credentialEpoch, token: Symbol('board-metrics') }
    metricsOwner = owner
    metricsRetry = retry
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
    metricsRetry = null
    loading.value = false
  }

  function beginForecastRequest(retry: ReadRetry): RequestOwner {
    const owner = { epoch: credentialEpoch, token: Symbol('board-forecast') }
    forecastOwner = owner
    forecastRetry = retry
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
    forecastRetry = null
    forecastLoading.value = false
  }

  function invalidateRequests(): void {
    credentialEpoch += 1
    metricsOwner = null
    forecastOwner = null
    metricsRetry = null
    forecastRetry = null
    loading.value = false
    error.value = null
    forecastLoading.value = false
    forecastError.value = null
  }

  function retryEmptyActiveRequests(): void {
    const pendingMetricsRetry = metricsOwner && metrics.value === null ? metricsRetry : null
    const pendingForecastRetry = forecastOwner && forecast.value === null ? forecastRetry : null

    invalidateRequests()
    if (pendingMetricsRetry) {
      void pendingMetricsRetry().catch(() => {
        // The retried store action owns current error/toast state.
      })
    }
    if (pendingForecastRetry) {
      void pendingForecastRetry().catch(() => {
        // The retried store action owns current error/toast state.
      })
    }
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
    retryEmptyActiveRequests,
    { flush: 'sync' },
  )

  async function fetchBoardMetrics(query: MetricsQuery) {
    if (isDemoMode) {
      metricsOwner = null
      metricsRetry = null
      loading.value = false
      error.value = 'Metrics are not available in demo mode.'
      metrics.value = null
      return
    }

    const owner = beginMetricsRequest(() => fetchBoardMetrics(query))
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
      forecastRetry = null
      forecastLoading.value = false
      forecastError.value = 'Forecast is not available in demo mode.'
      forecast.value = null
      return
    }

    const owner = beginForecastRequest(() => fetchBoardForecast(query))
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
