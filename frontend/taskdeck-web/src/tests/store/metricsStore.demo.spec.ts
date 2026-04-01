import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: true,
  }
})

vi.mock('../../api/metricsApi', () => ({
  metricsApi: {
    getBoardMetrics: vi.fn(),
  },
}))

const toastMocks = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

import { useMetricsStore } from '../../store/metricsStore'
import { metricsApi } from '../../api/metricsApi'

describe('metricsStore demo mode', () => {
  let store: ReturnType<typeof useMetricsStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    store = useMetricsStore()
  })

  it('fetchBoardMetrics in demo mode does not call API', async () => {
    await store.fetchBoardMetrics({ boardId: 'board-1' })

    expect(metricsApi.getBoardMetrics).not.toHaveBeenCalled()
    expect(store.metrics).toBeNull()
    expect(store.loading).toBe(false)
  })

  it('fetchBoardMetrics in demo mode shows demo mode message', async () => {
    await store.fetchBoardMetrics({ boardId: 'board-1' })

    expect(store.error).toBe('Metrics are not available in demo mode.')
  })
})
