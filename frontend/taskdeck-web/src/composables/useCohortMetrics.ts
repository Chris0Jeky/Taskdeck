import { ref, computed } from 'vue'
import http from '../api/http'

export interface CohortMetrics {
  cohortId: string
  promptVersion: string
  totalProposals: number
  accepted: number
  edited: number
  rejected: number
  averageTimeToDecisionMs: number
}

export interface CohortComparison {
  cohorts: CohortMetrics[]
  dateRange: { from: string; to: string }
}

export function useCohortMetrics() {
  const loading = ref(false)
  const error = ref<string | null>(null)
  const data = ref<CohortComparison | null>(null)
  let requestSeq = 0

  const cohorts = computed(() => data.value?.cohorts ?? [])

  const summary = computed(() => {
    if (cohorts.value.length === 0) return null

    const totals = cohorts.value.reduce(
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
  })

  async function fetchCohortMetrics(days: number = 30): Promise<void> {
    const seq = ++requestSeq
    loading.value = true
    error.value = null

    try {
      const from = new Date()
      from.setDate(from.getDate() - days)

      const { data: response } = await http.get<CohortComparison>(
        '/automation/metrics/cohorts',
        { params: { from: from.toISOString(), to: new Date().toISOString() } },
      )
      if (seq !== requestSeq) return
      data.value = response
    } catch (e: unknown) {
      if (seq !== requestSeq) return
      error.value = e instanceof Error ? e.message : 'Failed to fetch cohort metrics'
    } finally {
      if (seq === requestSeq) {
        loading.value = false
      }
    }
  }

  function acceptanceRate(cohort: CohortMetrics): number {
    return cohort.totalProposals > 0 ? cohort.accepted / cohort.totalProposals : 0
  }

  function editRate(cohort: CohortMetrics): number {
    return cohort.totalProposals > 0 ? cohort.edited / cohort.totalProposals : 0
  }

  function rejectionRate(cohort: CohortMetrics): number {
    return cohort.totalProposals > 0 ? cohort.rejected / cohort.totalProposals : 0
  }

  function formatDuration(ms: number): string {
    if (ms < 1000) return `${ms}ms`
    if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`
    return `${(ms / 60_000).toFixed(1)}m`
  }

  return {
    loading,
    error,
    data,
    cohorts,
    summary,
    fetchCohortMetrics,
    acceptanceRate,
    editRate,
    rejectionRate,
    formatDuration,
  }
}
