import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { metricsApi } from '../../api/metricsApi'
import { useMetricsStore } from '../../store/metricsStore'
import { useSessionStore } from '../../store/sessionStore'
import type { BoardForecastResponse, BoardMetricsResponse } from '../../types/metrics'

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: false }
})

vi.mock('../../api/metricsApi', () => ({
  metricsApi: {
    getBoardMetrics: vi.fn(),
    getBoardForecast: vi.fn(),
    exportBoardMetricsCsv: vi.fn(),
  },
}))

vi.mock('../../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    changePassword: vi.fn(),
    refreshToken: vi.fn(),
    exchangeOAuthCode: vi.fn(),
    exchangeOidcCode: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}

function metrics(boardId: string): BoardMetricsResponse {
  return {
    boardId,
    from: '2026-08-01T00:00:00Z',
    to: '2026-09-01T00:00:00Z',
    throughput: [],
    averageCycleTimeDays: 0,
    cycleTimeEntries: [],
    wipSnapshots: [],
    totalWip: 0,
    blockedCount: 0,
    blockedCards: [],
  }
}

function forecast(boardId: string): BoardForecastResponse {
  return {
    boardId,
    remainingCards: 0,
    completedCards: 0,
    averageThroughputPerDay: 0,
    throughputStdDev: 0,
    averageCycleTimeDays: 0,
    estimatedCompletionDate: null,
    confidenceBand: null,
    dataPointCount: 0,
    historyDaysUsed: 30,
    assumptions: [],
    caveats: [],
  }
}

describe('metricsStore async ownership', () => {
  let session: ReturnType<typeof useSessionStore>
  let store: ReturnType<typeof useMetricsStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'user-a'
    session.token = 'token-a'
    store = useMetricsStore()
    vi.clearAllMocks()
  })

  it('keeps the newest metrics request when responses settle in reverse order', async () => {
    const older = deferred<BoardMetricsResponse>()
    const newer = deferred<BoardMetricsResponse>()
    vi.mocked(metricsApi.getBoardMetrics)
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise)

    const oldRequest = store.fetchBoardMetrics({ boardId: 'board-old' })
    const newRequest = store.fetchBoardMetrics({ boardId: 'board-new' })
    newer.resolve(metrics('board-new'))
    await newRequest
    older.resolve(metrics('board-old'))
    await oldRequest

    expect(store.metrics?.boardId).toBe('board-new')
  })

  it('keeps the newest forecast request when responses settle in reverse order', async () => {
    const older = deferred<BoardForecastResponse>()
    const newer = deferred<BoardForecastResponse>()
    vi.mocked(metricsApi.getBoardForecast)
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise)

    const oldRequest = store.fetchBoardForecast({ boardId: 'board-old' })
    const newRequest = store.fetchBoardForecast({ boardId: 'board-new' })
    newer.resolve(forecast('board-new'))
    await newRequest
    older.resolve(forecast('board-old'))
    await oldRequest

    expect(store.forecast?.boardId).toBe('board-new')
  })

  it('suppresses stale metrics failure UI after a newer success while preserving rejection', async () => {
    const older = deferred<BoardMetricsResponse>()
    const newer = deferred<BoardMetricsResponse>()
    vi.mocked(metricsApi.getBoardMetrics)
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise)

    const oldRequest = store.fetchBoardMetrics({ boardId: 'board-old' })
    const newRequest = store.fetchBoardMetrics({ boardId: 'board-new' })
    newer.resolve(metrics('board-new'))
    await newRequest
    older.reject(new Error('stale metrics failure'))
    await expect(oldRequest).rejects.toThrow('stale metrics failure')

    expect(store.metrics?.boardId).toBe('board-new')
    expect(store.error).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('does not let an older metrics finally clear the current metrics loading owner', async () => {
    const older = deferred<BoardMetricsResponse>()
    const newer = deferred<BoardMetricsResponse>()
    vi.mocked(metricsApi.getBoardMetrics)
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise)

    const oldRequest = store.fetchBoardMetrics({ boardId: 'board-old' })
    const newRequest = store.fetchBoardMetrics({ boardId: 'board-new' })
    older.resolve(metrics('board-old'))
    await oldRequest
    expect(store.loading).toBe(true)

    newer.resolve(metrics('board-new'))
    await newRequest
    expect(store.loading).toBe(false)
  })

  it('keeps metrics loading cleared when the newer request settles first', async () => {
    const older = deferred<BoardMetricsResponse>()
    const newer = deferred<BoardMetricsResponse>()
    vi.mocked(metricsApi.getBoardMetrics)
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise)

    const oldRequest = store.fetchBoardMetrics({ boardId: 'board-old' })
    const newRequest = store.fetchBoardMetrics({ boardId: 'board-new' })
    newer.resolve(metrics('board-new'))
    await newRequest
    expect(store.loading).toBe(false)

    older.resolve(metrics('board-old'))
    await oldRequest
    expect(store.metrics?.boardId).toBe('board-new')
    expect(store.loading).toBe(false)
  })

  it('keeps metrics and forecast lanes independently concurrent', async () => {
    const pendingMetrics = deferred<BoardMetricsResponse>()
    const pendingForecast = deferred<BoardForecastResponse>()
    vi.mocked(metricsApi.getBoardMetrics).mockReturnValue(pendingMetrics.promise)
    vi.mocked(metricsApi.getBoardForecast).mockReturnValue(pendingForecast.promise)

    const metricsRequest = store.fetchBoardMetrics({ boardId: 'board-a' })
    const forecastRequest = store.fetchBoardForecast({ boardId: 'board-a' })

    pendingMetrics.resolve(metrics('board-a'))
    await metricsRequest
    expect(store.loading).toBe(false)
    expect(store.forecastLoading).toBe(true)

    pendingForecast.resolve(forecast('board-a'))
    await forecastRequest
    expect(store.forecastLoading).toBe(false)
  })

  it('$reset invalidates pending success and failure settlements', async () => {
    const pendingMetrics = deferred<BoardMetricsResponse>()
    const pendingForecast = deferred<BoardForecastResponse>()
    vi.mocked(metricsApi.getBoardMetrics).mockReturnValue(pendingMetrics.promise)
    vi.mocked(metricsApi.getBoardForecast).mockReturnValue(pendingForecast.promise)

    const metricsRequest = store.fetchBoardMetrics({ boardId: 'board-old' })
    const forecastRequest = store.fetchBoardForecast({ boardId: 'board-old' })
    store.$reset()

    pendingMetrics.resolve(metrics('board-old'))
    pendingForecast.reject(new Error('stale forecast failure'))
    await metricsRequest
    await expect(forecastRequest).rejects.toThrow('stale forecast failure')

    expect(store.metrics).toBeNull()
    expect(store.forecast).toBeNull()
    expect(store.loading).toBe(false)
    expect(store.forecastLoading).toBe(false)
    expect(store.error).toBeNull()
    expect(store.forecastError).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('preserves loaded dashboard data while invalidating old-token work on refresh', async () => {
    store.metrics = metrics('existing')
    store.forecast = forecast('existing')
    const pendingMetrics = deferred<BoardMetricsResponse>()
    const pendingForecast = deferred<BoardForecastResponse>()
    vi.mocked(metricsApi.getBoardMetrics).mockReturnValue(pendingMetrics.promise)
    vi.mocked(metricsApi.getBoardForecast).mockReturnValue(pendingForecast.promise)

    const metricsRequest = store.fetchBoardMetrics({ boardId: 'board-old' })
    const forecastRequest = store.fetchBoardForecast({ boardId: 'board-old' })
    session.token = 'token-b'

    expect(store.metrics?.boardId).toBe('existing')
    expect(store.forecast?.boardId).toBe('existing')
    expect(store.loading).toBe(false)
    expect(store.forecastLoading).toBe(false)

    pendingMetrics.resolve(metrics('old-token'))
    pendingForecast.reject(new Error('old-token forecast failure'))
    await metricsRequest
    await expect(forecastRequest).rejects.toThrow('old-token forecast failure')

    expect(store.metrics?.boardId).toBe('existing')
    expect(store.forecast?.boardId).toBe('existing')
    expect(store.error).toBeNull()
    expect(store.forecastError).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('preserves settled errors when same-user token rotation retires old work', async () => {
    vi.mocked(metricsApi.getBoardMetrics).mockRejectedValueOnce(new Error('metrics failure'))
    vi.mocked(metricsApi.getBoardForecast).mockRejectedValueOnce(new Error('forecast failure'))

    await expect(store.fetchBoardMetrics({ boardId: 'board-a' })).rejects.toThrow('metrics failure')
    await expect(store.fetchBoardForecast({ boardId: 'board-a' })).rejects.toThrow('forecast failure')
    expect(store.error).toBe('metrics failure')
    expect(store.forecastError).toBe('forecast failure')

    session.token = 'token-b'

    expect(store.error).toBe('metrics failure')
    expect(store.forecastError).toBe('forecast failure')
    expect(toastMocks.error).toHaveBeenCalledTimes(2)
  })

  it('surfaces a failure from a token-rotation retry', async () => {
    const oldMetrics = deferred<BoardMetricsResponse>()
    const freshMetrics = deferred<BoardMetricsResponse>()
    vi.mocked(metricsApi.getBoardMetrics)
      .mockReturnValueOnce(oldMetrics.promise)
      .mockReturnValueOnce(freshMetrics.promise)

    const oldRequest = store.fetchBoardMetrics({ boardId: 'board-a' })
    session.token = 'token-b'
    oldMetrics.reject(new Error('old-token failure'))
    await expect(oldRequest).rejects.toThrow('old-token failure')

    freshMetrics.reject(new Error('fresh-token failure'))
    await vi.waitFor(() => {
      expect(store.error).toBe('fresh-token failure')
    })
    expect(toastMocks.error).toHaveBeenCalledWith('fresh-token failure')
  })

  it('retries empty initial metrics and forecast reads after same-user token rotation', async () => {
    const oldMetrics = deferred<BoardMetricsResponse>()
    const freshMetrics = deferred<BoardMetricsResponse>()
    const oldForecast = deferred<BoardForecastResponse>()
    const freshForecast = deferred<BoardForecastResponse>()
    vi.mocked(metricsApi.getBoardMetrics)
      .mockReturnValueOnce(oldMetrics.promise)
      .mockReturnValueOnce(freshMetrics.promise)
    vi.mocked(metricsApi.getBoardForecast)
      .mockReturnValueOnce(oldForecast.promise)
      .mockReturnValueOnce(freshForecast.promise)

    const metricsRequest = store.fetchBoardMetrics({ boardId: 'board-a' })
    const forecastRequest = store.fetchBoardForecast({ boardId: 'board-a' })
    session.token = 'token-b'

    expect(metricsApi.getBoardMetrics).toHaveBeenCalledTimes(2)
    expect(metricsApi.getBoardForecast).toHaveBeenCalledTimes(2)
    expect(store.metrics).toBeNull()
    expect(store.forecast).toBeNull()
    expect(store.loading).toBe(true)
    expect(store.forecastLoading).toBe(true)

    oldMetrics.resolve(metrics('old-token'))
    oldForecast.reject(new Error('old-token forecast failure'))
    await metricsRequest
    await expect(forecastRequest).rejects.toThrow('old-token forecast failure')

    expect(store.metrics).toBeNull()
    expect(store.forecast).toBeNull()
    expect(store.loading).toBe(true)
    expect(store.forecastLoading).toBe(true)
    expect(store.error).toBeNull()
    expect(store.forecastError).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()

    freshMetrics.resolve(metrics('fresh-token'))
    freshForecast.resolve(forecast('fresh-token'))
    await vi.waitFor(() => {
      expect(store.metrics?.boardId).toBe('fresh-token')
      expect(store.forecast?.boardId).toBe('fresh-token')
      expect(store.loading).toBe(false)
      expect(store.forecastLoading).toBe(false)
    })
  })
})
