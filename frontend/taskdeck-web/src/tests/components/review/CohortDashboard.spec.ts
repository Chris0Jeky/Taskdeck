import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, computed } from 'vue'
import CohortDashboard from '../../../components/review/CohortDashboard.vue'
import type { CohortMetrics } from '../../../composables/useCohortMetrics'

// ---------------------------------------------------------------------------
// Composable mock
// ---------------------------------------------------------------------------

const mockFetchCohortMetrics = vi.fn<(days?: number) => Promise<void>>()

const mockState = {
  loading: ref(false),
  error: ref<string | null>(null),
  data: ref<{ cohorts: CohortMetrics[]; dateRange: { from: string; to: string } } | null>(null),
  cohorts: computed(() => mockState.data.value?.cohorts ?? []),
  summary: computed(() => {
    const cs = mockState.data.value?.cohorts ?? []
    if (cs.length === 0) return null
    const totals = cs.reduce(
      (acc, c) => ({
        proposals: acc.proposals + c.totalProposals,
        accepted: acc.accepted + c.accepted,
        edited: acc.edited + c.edited,
        rejected: acc.rejected + c.rejected,
      }),
      { proposals: 0, accepted: 0, edited: 0, rejected: 0 },
    )
    return {
      ...totals,
      acceptanceRate: totals.proposals > 0 ? totals.accepted / totals.proposals : 0,
      editRate: totals.proposals > 0 ? totals.edited / totals.proposals : 0,
      rejectionRate: totals.proposals > 0 ? totals.rejected / totals.proposals : 0,
    }
  }),
  fetchCohortMetrics: mockFetchCohortMetrics,
  acceptanceRate: (c: CohortMetrics) => (c.totalProposals > 0 ? c.accepted / c.totalProposals : 0),
  editRate: (c: CohortMetrics) => (c.totalProposals > 0 ? c.edited / c.totalProposals : 0),
  rejectionRate: (c: CohortMetrics) => (c.totalProposals > 0 ? c.rejected / c.totalProposals : 0),
  formatDuration: (ms: number) => {
    if (ms < 1000) return `${ms}ms`
    if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`
    return `${(ms / 60_000).toFixed(1)}m`
  },
}

vi.mock('../../../composables/useCohortMetrics', () => ({
  useCohortMetrics: vi.fn(() => mockState),
}))

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function buildCohort(overrides?: Partial<CohortMetrics>): CohortMetrics {
  return {
    cohortId: 'cohort-a',
    promptVersion: 'v1.0.0',
    totalProposals: 100,
    accepted: 70,
    edited: 20,
    rejected: 10,
    averageTimeToDecisionMs: 5500,
    ...overrides,
  }
}

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

function mountDashboard(props?: { days?: number }) {
  return mount(CohortDashboard, { props })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('CohortDashboard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockState.loading.value = false
    mockState.error.value = null
    mockState.data.value = null
    mockFetchCohortMetrics.mockResolvedValue(undefined)
  })

  // 1. Loading state
  it('shows loading state initially', async () => {
    mockState.loading.value = true

    const wrapper = mountDashboard()
    await waitForUi()

    expect(wrapper.find('[role="status"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Loading cohort data...')
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  // 2. Empty state
  it('shows empty state when no cohorts are returned', async () => {
    mockState.data.value = { cohorts: [], dateRange: { from: '', to: '' } }

    const wrapper = mountDashboard()
    await waitForUi()

    expect(wrapper.text()).toContain('No cohort data available for this period.')
    expect(wrapper.find('table').exists()).toBe(false)
  })

  // 3. Error state + retry button present
  it('shows error state with a retry button when fetch fails', async () => {
    mockState.error.value = 'Failed to fetch cohort metrics'

    const wrapper = mountDashboard()
    await waitForUi()

    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Failed to fetch cohort metrics')
    expect(wrapper.find('.cohort-dashboard__retry').exists()).toBe(true)
  })

  // 4. Summary cards with correct percentages
  it('renders summary cards with correct percentages when data exists', async () => {
    mockState.data.value = {
      cohorts: [buildCohort({ totalProposals: 100, accepted: 70, edited: 20, rejected: 10 })],
      dateRange: { from: '2026-04-16', to: '2026-05-16' },
    }

    const wrapper = mountDashboard()
    await waitForUi()

    const text = wrapper.text()
    // summary values derived from 100 proposals, 70 accepted, 20 edited, 10 rejected
    expect(text).toContain('100') // Total Proposals
    expect(text).toContain('70.0%') // 70/100 acceptance
    expect(text).toContain('20.0%') // 20/100 edit
    expect(text).toContain('10.0%') // 10/100 rejection
    expect(text).toContain('Total Proposals')
    expect(text).toContain('Acceptance Rate')
    expect(text).toContain('Edit Rate')
    expect(text).toContain('Rejection Rate')
  })

  // 5. Table rows for each cohort
  it('renders a table row for each cohort', async () => {
    mockState.data.value = {
      cohorts: [
        buildCohort({ cohortId: 'a', promptVersion: 'v1.0.0', totalProposals: 50, accepted: 40, edited: 5, rejected: 5 }),
        buildCohort({ cohortId: 'b', promptVersion: 'v1.1.0', totalProposals: 80, accepted: 60, edited: 10, rejected: 10 }),
      ],
      dateRange: { from: '2026-04-16', to: '2026-05-16' },
    }

    const wrapper = mountDashboard()
    await waitForUi()

    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[0].text()).toContain('v1.0.0')
    expect(rows[1].text()).toContain('v1.1.0')
  })

  it('does not render visible bar segments for zero-rate buckets', async () => {
    mockState.data.value = {
      cohorts: [
        buildCohort({
          totalProposals: 10,
          accepted: 10,
          edited: 0,
          rejected: 0,
        }),
      ],
      dateRange: { from: '2026-04-16', to: '2026-05-16' },
    }

    const wrapper = mountDashboard()
    await waitForUi()

    const editBar = wrapper.find('.cohort-dashboard__bar--edit')
    const rejectBar = wrapper.find('.cohort-dashboard__bar--reject')
    expect(editBar.attributes('style')).toContain('width: 0%')
    expect(rejectBar.attributes('style')).toContain('width: 0%')
  })

  // 6. Best performing insight section
  it('shows the "Best performing" insight section when cohorts exist', async () => {
    mockState.data.value = {
      cohorts: [
        buildCohort({ cohortId: 'low', promptVersion: 'v0.9.0', totalProposals: 100, accepted: 50, edited: 30, rejected: 20 }),
        buildCohort({ cohortId: 'high', promptVersion: 'v1.2.0', totalProposals: 100, accepted: 90, edited: 5, rejected: 5 }),
      ],
      dateRange: { from: '2026-04-16', to: '2026-05-16' },
    }

    const wrapper = mountDashboard()
    await waitForUi()

    const text = wrapper.text()
    expect(text).toContain('Best performing')
    // v1.2.0 has the highest acceptance rate (90%)
    expect(text).toContain('v1.2.0')
    expect(text).toContain('by acceptance rate')
  })

  // 7. Retry button calls fetchCohortMetrics again
  it('retry button calls fetchCohortMetrics again', async () => {
    mockState.error.value = 'Network error'

    const wrapper = mountDashboard()
    await waitForUi()

    // fetchCohortMetrics was already called once by onMounted
    const callsBefore = mockFetchCohortMetrics.mock.calls.length

    await wrapper.find('.cohort-dashboard__retry').trigger('click')
    await waitForUi()

    expect(mockFetchCohortMetrics.mock.calls.length).toBe(callsBefore + 1)
  })

  // Extra: calls fetchCohortMetrics on mount with the default days prop
  it('calls fetchCohortMetrics on mount with the default days (30)', async () => {
    mountDashboard()
    await waitForUi()

    expect(mockFetchCohortMetrics).toHaveBeenCalledWith(30)
  })

  // Extra: respects a custom days prop
  it('calls fetchCohortMetrics on mount with a custom days prop', async () => {
    mountDashboard({ days: 14 })
    await waitForUi()

    expect(mockFetchCohortMetrics).toHaveBeenCalledWith(14)
  })
})
