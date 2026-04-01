import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { reactive } from 'vue'
import MetricsView from '../../views/MetricsView.vue'
import type { BoardMetricsResponse } from '../../types/metrics'

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
    { cardId: 'c1', cardTitle: 'Card Alpha', cycleTimeDays: 2.0 },
    { cardId: 'c2', cardTitle: 'Card Beta', cycleTimeDays: 3.0 },
  ],
  wipSnapshots: [
    { columnId: 'col1', columnName: 'To Do', cardCount: 5, wipLimit: null },
    { columnId: 'col2', columnName: 'Doing', cardCount: 3, wipLimit: 2 },
  ],
  totalWip: 8,
  blockedCount: 1,
  blockedCards: [
    { cardId: 'c3', cardTitle: 'Blocked Card', blockReason: 'Waiting', blockedDurationDays: 1.5 },
  ],
}

const EMPTY_METRICS: BoardMetricsResponse = {
  boardId: 'board-1',
  from: '2026-03-01T00:00:00Z',
  to: '2026-03-31T23:59:59Z',
  throughput: [],
  averageCycleTimeDays: 0,
  cycleTimeEntries: [],
  wipSnapshots: [],
  totalWip: 0,
  blockedCount: 0,
  blockedCards: [],
}

const mockMetricsStore = reactive({
  metrics: null as BoardMetricsResponse | null,
  loading: false,
  error: null as string | null,
  fetchBoardMetrics: vi.fn().mockResolvedValue(undefined),
  $reset: vi.fn(),
})

const mockBoardStore = reactive({
  boards: [] as Array<{ id: string; name: string; isArchived: boolean; description: null; createdAt: string; updatedAt: string }>,
  fetchBoards: vi.fn<(...args: unknown[]) => Promise<void>>(),
})

vi.mock('../../store/metricsStore', () => ({
  useMetricsStore: () => mockMetricsStore,
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
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

describe('MetricsView', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    mockMetricsStore.metrics = null
    mockMetricsStore.loading = false
    mockMetricsStore.error = null

    mockBoardStore.boards = []
    mockBoardStore.fetchBoards.mockImplementation(async () => {
      seedBoards()
    })
  })

  it('renders header and board selector', async () => {
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Board Metrics')
    expect(wrapper.find('#board-select').exists()).toBe(true)
    expect(wrapper.find('#range-select').exists()).toBe(true)
  })

  it('loads boards on mount and auto-selects first', async () => {
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(mockBoardStore.fetchBoards).toHaveBeenCalled()
    const boardSelect = wrapper.get('#board-select')
    expect((boardSelect.element as HTMLSelectElement).value).toBe('board-1')
  })

  it('shows "Select a board" prompt when no board selected', async () => {
    mockBoardStore.fetchBoards.mockImplementation(async () => {
      // no boards
    })
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Select a board above to view its metrics.')
  })

  it('shows loading spinner when loading is true', async () => {
    mockMetricsStore.loading = true
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Loading metrics...')
    expect(wrapper.find('.td-metrics__spinner').exists()).toBe(true)
  })

  it('shows error state with retry button', async () => {
    mockMetricsStore.error = 'Something went wrong'
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Something went wrong')
    const retryBtn = wrapper.find('.td-metrics__state--error button')
    expect(retryBtn.exists()).toBe(true)
    expect(retryBtn.text()).toBe('Retry')
  })

  it('shows empty data state when metrics is null but board is selected', async () => {
    // Auto-select board
    const wrapper = mount(MetricsView)
    await waitForUi()

    // Board is selected but metrics is still null (no data yet returned)
    expect(wrapper.text()).toContain('No metrics data available')
  })

  it('renders dashboard with metric data', async () => {
    mockMetricsStore.metrics = MOCK_METRICS
    const wrapper = mount(MetricsView)
    await waitForUi()

    // Summary cards
    expect(wrapper.text()).toContain('Total Throughput')
    expect(wrapper.text()).toContain('4') // 3 + 1
    expect(wrapper.text()).toContain('Avg Cycle Time')
    expect(wrapper.text()).toContain('2.5')
    expect(wrapper.text()).toContain('Current WIP')
    expect(wrapper.text()).toContain('8')
    expect(wrapper.text()).toContain('Blocked')

    // Throughput chart
    expect(wrapper.text()).toContain('Throughput Trend')
    expect(wrapper.findAll('.td-metrics__bar-group')).toHaveLength(2)

    // WIP chart
    expect(wrapper.text()).toContain('WIP by Column')
    expect(wrapper.text()).toContain('To Do')
    expect(wrapper.text()).toContain('Doing')

    // WIP limit violation highlighting
    const overLimitBars = wrapper.findAll('.td-metrics__wip-bar-fill--over')
    expect(overLimitBars.length).toBe(1) // Doing: 3 > wipLimit 2

    // WIP limit display
    expect(wrapper.text()).toContain('/ 2')

    // Cycle time table
    expect(wrapper.text()).toContain('Cycle Time Details')
    expect(wrapper.text()).toContain('Card Alpha')
    expect(wrapper.text()).toContain('Card Beta')

    // Blocked cards table
    expect(wrapper.text()).toContain('Blocked Cards')
    expect(wrapper.text()).toContain('Blocked Card')
    expect(wrapper.text()).toContain('Waiting')
  })

  it('renders empty chart placeholders when metrics lists are empty', async () => {
    mockMetricsStore.metrics = EMPTY_METRICS
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('No completed cards in this period.')
    expect(wrapper.text()).toContain('No columns found.')
    expect(wrapper.text()).toContain('No completed cards to compute cycle time.')
    expect(wrapper.text()).toContain('No blocked cards. Great!')
  })

  it('applies alert class when blocked count > 0', async () => {
    mockMetricsStore.metrics = MOCK_METRICS
    const wrapper = mount(MetricsView)
    await waitForUi()

    const alertCard = wrapper.find('.td-metrics__card--alert')
    expect(alertCard.exists()).toBe(true)
    expect(alertCard.text()).toContain('Blocked')
  })

  it('does not apply alert class when blocked count is 0', async () => {
    mockMetricsStore.metrics = EMPTY_METRICS
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.find('.td-metrics__card--alert').exists()).toBe(false)
  })

  it('shows null block reason as "No reason given"', async () => {
    mockMetricsStore.metrics = {
      ...MOCK_METRICS,
      blockedCards: [
        { cardId: 'c3', cardTitle: 'No Reason Card', blockReason: null, blockedDurationDays: 0.5 },
      ],
    }
    const wrapper = mount(MetricsView)
    await waitForUi()

    expect(wrapper.text()).toContain('No reason given')
  })

  it('date range selector has all expected options', async () => {
    const wrapper = mount(MetricsView)
    await waitForUi()

    const rangeSelect = wrapper.get('#range-select')
    const options = rangeSelect.findAll('option')
    const values = options.map((o) => Number((o.element as HTMLOptionElement).value))
    expect(values).toEqual([7, 14, 30, 60, 90])
  })

  it('fetches metrics when board selection changes', async () => {
    const wrapper = mount(MetricsView)
    await waitForUi()

    // Auto-select triggers first fetch
    expect(mockMetricsStore.fetchBoardMetrics).toHaveBeenCalled()
    vi.clearAllMocks()

    // Change board
    await wrapper.get('#board-select').setValue('board-2')
    await waitForUi()

    expect(mockMetricsStore.fetchBoardMetrics).toHaveBeenCalledWith(
      expect.objectContaining({ boardId: 'board-2' }),
    )
  })

  it('fetches metrics when date range changes', async () => {
    const wrapper = mount(MetricsView)
    await waitForUi()
    vi.clearAllMocks()

    await wrapper.get('#range-select').setValue('7')
    await waitForUi()

    expect(mockMetricsStore.fetchBoardMetrics).toHaveBeenCalled()
  })
})
