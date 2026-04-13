import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { reactive } from 'vue'
import MetricsView from '../../views/MetricsView.vue'
import type { BoardMetricsResponse, BoardForecastResponse } from '../../types/metrics'

const MOCK_METRICS: BoardMetricsResponse = {
  boardId: 'board-1',
  from: '2026-03-01T00:00:00Z',
  to: '2026-03-31T23:59:59Z',
  throughput: [
    { date: '2026-03-15T00:00:00Z', completedCount: 3 },
  ],
  averageCycleTimeDays: 2.5,
  cycleTimeEntries: [],
  wipSnapshots: [],
  totalWip: 4,
  blockedCount: 0,
  blockedCards: [],
}

const MOCK_FORECAST: BoardForecastResponse = {
  boardId: 'board-1',
  remainingCards: 12,
  averageThroughputPerDay: 1.5,
  estimatedCompletionDate: '2026-04-20T00:00:00Z',
  historyDaysUsed: 30,
  dataPointCount: 28,
  assumptions: ['Throughput remains constant', 'No new cards added'],
  caveats: ['Low data confidence'],
  confidenceBand: {
    lowEstimate: '2026-04-15T00:00:00Z',
    expectedEstimate: '2026-04-20T00:00:00Z',
    highEstimate: '2026-04-28T00:00:00Z',
    lowThroughputPerDay: 0.8,
    expectedThroughputPerDay: 1.5,
    highThroughputPerDay: 2.2,
  },
}

const mockMetricsStore = reactive({
  metrics: null as BoardMetricsResponse | null,
  loading: false,
  error: null as string | null,
  forecast: null as BoardForecastResponse | null,
  forecastLoading: false,
  forecastError: null as string | null,
  fetchBoardMetrics: vi.fn().mockResolvedValue(undefined),
  fetchBoardForecast: vi.fn().mockResolvedValue(undefined),
  $reset: vi.fn(),
})

const mockBoardStore = reactive({
  boards: [] as Array<{ id: string; name: string; isArchived: boolean; description: null; createdAt: string; updatedAt: string }>,
  fetchBoards: vi.fn<(...args: unknown[]) => Promise<void>>(),
})

const mockToastStore = reactive({
  error: vi.fn(),
  success: vi.fn(),
})

const mockMetricsApiMethods = vi.hoisted(() => ({
  exportBoardMetricsCsv: vi.fn(),
}))

vi.mock('../../store/metricsStore', () => ({
  useMetricsStore: () => mockMetricsStore,
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => mockToastStore,
}))

vi.mock('../../api/metricsApi', () => ({
  metricsApi: {
    exportBoardMetricsCsv: mockMetricsApiMethods.exportBoardMetricsCsv,
  },
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

function seedBoards() {
  mockBoardStore.boards = [
    { id: 'board-1', name: 'Board One', isArchived: false, description: null, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() },
    { id: 'board-2', name: 'Board Two', isArchived: false, description: null, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() },
  ]
}

async function waitForUi() {
  await flushPromises()
  await flushPromises()
}

describe('MetricsView — retry and export', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockMetricsStore.metrics = null
    mockMetricsStore.loading = false
    mockMetricsStore.error = null
    mockMetricsStore.forecast = null
    mockMetricsStore.forecastLoading = false
    mockMetricsStore.forecastError = null
    mockBoardStore.boards = []
    mockBoardStore.fetchBoards.mockImplementation(async () => { seedBoards() })
    mockMetricsApiMethods.exportBoardMetricsCsv.mockResolvedValue(undefined)
  })

  it('calls fetchBoardMetrics when retry button is clicked in error state', async () => {
    mockMetricsStore.error = 'Connection timeout'
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Connection timeout')

    const retryBtn = wrapper.find('.td-metrics__state--error button')
    expect(retryBtn.exists()).toBe(true)

    vi.clearAllMocks()
    await retryBtn.trigger('click')
    await waitForUi()

    expect(mockMetricsStore.fetchBoardMetrics).toHaveBeenCalled()
  })

  it('renders Export CSV button as disabled when no data is available', async () => {
    const wrapper = mount(MetricsView)
    await waitForUi()

    const exportBtn = wrapper.findAll('button').find((b) => b.text().includes('Export CSV'))
    expect(exportBtn).toBeDefined()
    expect(exportBtn!.attributes('disabled')).toBeDefined()
  })

  it('enables Export CSV button when metrics data is present', async () => {
    mockMetricsStore.metrics = MOCK_METRICS
    const wrapper = mount(MetricsView)
    await waitForUi()

    const exportBtn = wrapper.findAll('button').find((b) => b.text().includes('Export CSV'))
    expect(exportBtn).toBeDefined()
    expect(exportBtn!.attributes('disabled')).toBeUndefined()
  })

  it('calls metricsApi.exportBoardMetricsCsv when Export CSV is clicked', async () => {
    mockMetricsStore.metrics = MOCK_METRICS
    const wrapper = mount(MetricsView)
    await waitForUi()

    const exportBtn = wrapper.findAll('button').find((b) => b.text().includes('Export CSV'))
    await exportBtn!.trigger('click')
    await waitForUi()

    expect(mockMetricsApiMethods.exportBoardMetricsCsv).toHaveBeenCalledWith(
      expect.objectContaining({ boardId: 'board-1' }),
    )
  })

  it('shows error toast when CSV export fails', async () => {
    mockMetricsStore.metrics = MOCK_METRICS
    mockMetricsApiMethods.exportBoardMetricsCsv.mockRejectedValueOnce(new Error('export failed'))

    const wrapper = mount(MetricsView)
    await waitForUi()

    const exportBtn = wrapper.findAll('button').find((b) => b.text().includes('Export CSV'))
    await exportBtn!.trigger('click')
    await waitForUi()

    expect(mockToastStore.error).toHaveBeenCalledWith('Failed to export CSV')
  })
})

describe('MetricsView — forecast section', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockMetricsStore.metrics = MOCK_METRICS
    mockMetricsStore.loading = false
    mockMetricsStore.error = null
    mockMetricsStore.forecast = null
    mockMetricsStore.forecastLoading = false
    mockMetricsStore.forecastError = null
    mockBoardStore.boards = []
    mockBoardStore.fetchBoards.mockImplementation(async () => { seedBoards() })
  })

  it('shows forecast loading spinner when forecastLoading is true', async () => {
    mockMetricsStore.forecastLoading = true
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Computing forecast...')
    expect(wrapper.find('.td-metrics__forecast-loading').exists()).toBe(true)
  })

  it('shows forecast error state with retry button', async () => {
    mockMetricsStore.forecastError = 'Forecast computation failed'
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Forecast computation failed')
    const retryBtn = wrapper.find('.td-metrics__forecast-error button')
    expect(retryBtn.exists()).toBe(true)
    expect(retryBtn.text()).toBe('Retry')
  })

  it('renders forecast data with remaining cards, throughput, and estimated completion', async () => {
    mockMetricsStore.forecast = MOCK_FORECAST
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Completion Forecast')
    expect(wrapper.text()).toContain('Remaining')
    expect(wrapper.text()).toContain('12')
    expect(wrapper.text()).toContain('Avg Throughput')
    expect(wrapper.text()).toContain('1.50')
    expect(wrapper.text()).toContain('Estimated Completion')
    expect(wrapper.text()).toContain('Data Points')
    expect(wrapper.text()).toContain('28')
    expect(wrapper.text()).toContain('over 30 days')
  })

  it('renders confidence band with optimistic, expected, and pessimistic estimates', async () => {
    mockMetricsStore.forecast = MOCK_FORECAST
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Confidence Range')
    expect(wrapper.text()).toContain('Optimistic')
    expect(wrapper.text()).toContain('Expected')
    expect(wrapper.text()).toContain('Pessimistic')
    expect(wrapper.text()).toContain('2.20 cards/day')
    expect(wrapper.text()).toContain('1.50 cards/day')
    expect(wrapper.text()).toContain('0.80 cards/day')
  })

  it('renders caveats when present', async () => {
    mockMetricsStore.forecast = MOCK_FORECAST
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Caveats')
    expect(wrapper.text()).toContain('Low data confidence')
  })

  it('renders assumptions section as expandable details', async () => {
    mockMetricsStore.forecast = MOCK_FORECAST
    const wrapper = mount(MetricsView)
    await waitForUi()

    const details = wrapper.find('.td-metrics__assumptions')
    expect(details.exists()).toBe(true)
    expect(wrapper.text()).toContain('Assumptions (2)')
  })

  it('shows N/A for estimated completion when date is null', async () => {
    mockMetricsStore.forecast = {
      ...MOCK_FORECAST,
      estimatedCompletionDate: null,
      confidenceBand: null,
    }
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('N/A')
  })
})

describe('MetricsView — accessibility', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockMetricsStore.metrics = null
    mockMetricsStore.loading = false
    mockMetricsStore.error = null
    mockMetricsStore.forecast = null
    mockMetricsStore.forecastLoading = false
    mockMetricsStore.forecastError = null
    mockBoardStore.boards = []
    mockBoardStore.fetchBoards.mockImplementation(async () => { seedBoards() })
  })

  it('has proper aria attributes on filter section', async () => {
    const wrapper = mount(MetricsView)
    await waitForUi()

    const filterSection = wrapper.find('[aria-label="Metric filters"]')
    expect(filterSection.exists()).toBe(true)
  })

  it('marks loading state with role="status" and aria-live', async () => {
    mockMetricsStore.loading = true
    const wrapper = mount(MetricsView)
    await waitForUi()

    const loadingDiv = wrapper.find('[role="status"]')
    expect(loadingDiv.exists()).toBe(true)
    expect(loadingDiv.attributes('aria-live')).toBe('polite')
  })

  it('marks error state with role="alert"', async () => {
    mockMetricsStore.error = 'Some error'
    const wrapper = mount(MetricsView)
    await waitForUi()

    const errorDiv = wrapper.find('[role="alert"]')
    expect(errorDiv.exists()).toBe(true)
  })

  it('has aria-label on throughput bar chart', async () => {
    mockMetricsStore.metrics = MOCK_METRICS
    const wrapper = mount(MetricsView)
    await waitForUi()

    const chart = wrapper.find('[aria-label="Throughput bar chart"]')
    expect(chart.exists()).toBe(true)
  })
})
