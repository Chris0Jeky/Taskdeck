import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { metricsApi } from '../../api/metricsApi'
import { useMetricsStore } from '../../store/metricsStore'
import type { BoardMetricsResponse } from '../../types/metrics'

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: false,
  }
})

vi.mock('../../api/metricsApi', () => ({
  metricsApi: {
    getBoardMetrics: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

const MOCK_METRICS: BoardMetricsResponse = {
  boardId: 'board-1',
  from: '2026-03-01T00:00:00Z',
  to: '2026-03-31T23:59:59Z',
  throughput: [
    { date: '2026-03-15T00:00:00Z', completedCount: 3 },
    { date: '2026-03-16T00:00:00Z', completedCount: 1 },
  ],
  averageCycleTimeDays: 2.5,
  cycleTimeEntries: [
    { cardId: 'c1', cardTitle: 'Card 1', cycleTimeDays: 2.0 },
    { cardId: 'c2', cardTitle: 'Card 2', cycleTimeDays: 3.0 },
  ],
  wipSnapshots: [
    { columnId: 'col1', columnName: 'To Do', cardCount: 5, wipLimit: null },
    { columnId: 'col2', columnName: 'Doing', cardCount: 3, wipLimit: 4 },
  ],
  totalWip: 8,
  blockedCount: 1,
  blockedCards: [
    { cardId: 'c3', cardTitle: 'Blocked Card', blockReason: 'Waiting', blockedDurationDays: 1.5 },
  ],
}

describe('metricsStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('starts with empty default state', () => {
    const store = useMetricsStore()

    expect(store.metrics).toBeNull()
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('fetchBoardMetrics populates metrics on success', async () => {
    vi.mocked(metricsApi.getBoardMetrics).mockResolvedValue(MOCK_METRICS)
    const store = useMetricsStore()

    await store.fetchBoardMetrics({ boardId: 'board-1' })

    expect(store.metrics).toEqual(MOCK_METRICS)
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
    expect(metricsApi.getBoardMetrics).toHaveBeenCalledWith({ boardId: 'board-1' })
  })

  it('fetchBoardMetrics sets error and shows toast on failure', async () => {
    vi.mocked(metricsApi.getBoardMetrics).mockRejectedValue(new Error('Network error'))
    const store = useMetricsStore()

    await expect(store.fetchBoardMetrics({ boardId: 'board-1' })).rejects.toThrow('Network error')

    expect(store.error).toBe('Failed to fetch board metrics')
    expect(store.loading).toBe(false)
    expect(store.metrics).toBeNull()
    expect(toastMocks.error).toHaveBeenCalledWith('Failed to fetch board metrics')
  })

  it('fetchBoardMetrics sets loading to true during fetch', async () => {
    let resolvePromise: (value: BoardMetricsResponse) => void
    const pendingPromise = new Promise<BoardMetricsResponse>((resolve) => {
      resolvePromise = resolve
    })
    vi.mocked(metricsApi.getBoardMetrics).mockReturnValue(pendingPromise)
    const store = useMetricsStore()

    const fetchPromise = store.fetchBoardMetrics({ boardId: 'board-1' })
    expect(store.loading).toBe(true)

    resolvePromise!(MOCK_METRICS)
    await fetchPromise

    expect(store.loading).toBe(false)
  })

  it('fetchBoardMetrics clears previous error on new fetch', async () => {
    vi.mocked(metricsApi.getBoardMetrics).mockRejectedValueOnce(new Error('fail'))
    const store = useMetricsStore()

    await expect(store.fetchBoardMetrics({ boardId: 'board-1' })).rejects.toThrow()
    expect(store.error).toBe('Failed to fetch board metrics')

    vi.mocked(metricsApi.getBoardMetrics).mockResolvedValueOnce(MOCK_METRICS)
    await store.fetchBoardMetrics({ boardId: 'board-1' })
    expect(store.error).toBeNull()
  })

  it('$reset restores initial state', async () => {
    vi.mocked(metricsApi.getBoardMetrics).mockResolvedValue(MOCK_METRICS)
    const store = useMetricsStore()

    await store.fetchBoardMetrics({ boardId: 'board-1' })
    expect(store.metrics).not.toBeNull()

    store.$reset()
    expect(store.metrics).toBeNull()
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })
})
