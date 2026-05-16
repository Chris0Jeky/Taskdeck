<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useCohortMetrics } from '../../composables/useCohortMetrics'
import type { CohortMetrics } from '../../composables/useCohortMetrics'

const props = withDefaults(
  defineProps<{
    days?: number
  }>(),
  { days: 30 },
)

const {
  loading,
  error,
  cohorts,
  summary,
  fetchCohortMetrics,
  acceptanceRate,
  editRate,
  rejectionRate,
  formatDuration,
} = useCohortMetrics()

const selectedDays = ref(props.days)

function pct(value: number): string {
  return `${(value * 100).toFixed(1)}%`
}

function barWidth(value: number): string {
  return `${Math.max(value * 100, 2)}%`
}

function bestCohort(metric: (c: CohortMetrics) => number): string | null {
  if (cohorts.value.length === 0) return null
  const best = cohorts.value.reduce((a, b) => (metric(a) > metric(b) ? a : b))
  return best.promptVersion
}

async function refresh() {
  await fetchCohortMetrics(selectedDays.value)
}

onMounted(() => {
  void fetchCohortMetrics(selectedDays.value)
})
</script>

<template>
  <div class="cohort-dashboard">
    <header class="cohort-dashboard__header">
      <div>
        <h2 class="cohort-dashboard__title">Cohort Performance</h2>
        <p class="cohort-dashboard__subtitle">
          Acceptance, edit, and rejection rates by prompt version
        </p>
      </div>
      <div class="cohort-dashboard__controls">
        <select
          v-model="selectedDays"
          class="cohort-dashboard__select"
          aria-label="Date range"
          @change="refresh"
        >
          <option :value="7">Last 7 days</option>
          <option :value="14">Last 14 days</option>
          <option :value="30">Last 30 days</option>
          <option :value="60">Last 60 days</option>
        </select>
      </div>
    </header>

    <div v-if="loading" class="cohort-dashboard__loading" role="status">
      <span>Loading cohort data...</span>
    </div>

    <div v-else-if="error" class="cohort-dashboard__error" role="alert">
      <p>{{ error }}</p>
      <button class="cohort-dashboard__retry" @click="refresh">Retry</button>
    </div>

    <template v-else-if="cohorts.length > 0">
      <div v-if="summary" class="cohort-dashboard__summary">
        <div class="cohort-dashboard__stat">
          <span class="cohort-dashboard__stat-value">{{ summary.proposals }}</span>
          <span class="cohort-dashboard__stat-label">Total Proposals</span>
        </div>
        <div class="cohort-dashboard__stat">
          <span class="cohort-dashboard__stat-value cohort-dashboard__stat-value--accept">
            {{ pct(summary.acceptanceRate) }}
          </span>
          <span class="cohort-dashboard__stat-label">Acceptance Rate</span>
        </div>
        <div class="cohort-dashboard__stat">
          <span class="cohort-dashboard__stat-value cohort-dashboard__stat-value--edit">
            {{ pct(summary.editRate) }}
          </span>
          <span class="cohort-dashboard__stat-label">Edit Rate</span>
        </div>
        <div class="cohort-dashboard__stat">
          <span class="cohort-dashboard__stat-value cohort-dashboard__stat-value--reject">
            {{ pct(summary.rejectionRate) }}
          </span>
          <span class="cohort-dashboard__stat-label">Rejection Rate</span>
        </div>
      </div>

      <div class="cohort-dashboard__table-wrap">
        <table class="cohort-dashboard__table" aria-label="Cohort comparison">
          <thead>
            <tr>
              <th>Prompt Version</th>
              <th>Proposals</th>
              <th>Accepted</th>
              <th>Edited</th>
              <th>Rejected</th>
              <th>Avg Decision Time</th>
              <th>Distribution</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="cohort in cohorts" :key="cohort.cohortId">
              <td class="cohort-dashboard__version">{{ cohort.promptVersion }}</td>
              <td>{{ cohort.totalProposals }}</td>
              <td>{{ cohort.accepted }} ({{ pct(acceptanceRate(cohort)) }})</td>
              <td>{{ cohort.edited }} ({{ pct(editRate(cohort)) }})</td>
              <td>{{ cohort.rejected }} ({{ pct(rejectionRate(cohort)) }})</td>
              <td>{{ formatDuration(cohort.averageTimeToDecisionMs) }}</td>
              <td class="cohort-dashboard__bars">
                <div class="cohort-dashboard__bar-stack">
                  <div
                    class="cohort-dashboard__bar cohort-dashboard__bar--accept"
                    :style="{ width: barWidth(acceptanceRate(cohort)) }"
                    :title="`Accepted: ${pct(acceptanceRate(cohort))}`"
                  />
                  <div
                    class="cohort-dashboard__bar cohort-dashboard__bar--edit"
                    :style="{ width: barWidth(editRate(cohort)) }"
                    :title="`Edited: ${pct(editRate(cohort))}`"
                  />
                  <div
                    class="cohort-dashboard__bar cohort-dashboard__bar--reject"
                    :style="{ width: barWidth(rejectionRate(cohort)) }"
                    :title="`Rejected: ${pct(rejectionRate(cohort))}`"
                  />
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="bestCohort(acceptanceRate)" class="cohort-dashboard__insight">
        Best performing: <strong>{{ bestCohort(acceptanceRate) }}</strong> by acceptance rate
      </div>
    </template>

    <div v-else class="cohort-dashboard__empty">
      <p>No cohort data available for this period.</p>
    </div>
  </div>
</template>

<style scoped>
.cohort-dashboard {
  background: var(--td-surface-container, #fff);
  border: 1px solid var(--td-border-ghost, #eee);
  border-radius: var(--td-radius-lg, 12px);
  padding: 24px;
}

.cohort-dashboard__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 20px;
}

.cohort-dashboard__title {
  font-size: 18px;
  font-weight: 700;
  margin: 0;
  color: var(--td-text-primary, #111);
}

.cohort-dashboard__subtitle {
  font-size: 13px;
  color: var(--td-text-secondary, #666);
  margin: 4px 0 0;
}

.cohort-dashboard__select {
  padding: 6px 12px;
  border: 1px solid var(--td-border-default, #ddd);
  border-radius: 6px;
  font-size: 13px;
  background: var(--td-surface-container, #fff);
  color: var(--td-text-primary, #333);
}

.cohort-dashboard__loading {
  padding: 40px;
  text-align: center;
  color: var(--td-text-secondary, #666);
}

.cohort-dashboard__error {
  padding: 24px;
  text-align: center;
  color: var(--td-error, #c00);
}

.cohort-dashboard__retry {
  margin-top: 8px;
  padding: 6px 14px;
  border: 1px solid var(--td-border-default, #ddd);
  border-radius: 6px;
  background: none;
  cursor: pointer;
  font-size: 13px;
}

.cohort-dashboard__summary {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
  margin-bottom: 24px;
}

.cohort-dashboard__stat {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 16px;
  background: var(--td-surface-sunken, #f9f9f9);
  border-radius: 8px;
}

.cohort-dashboard__stat-value {
  font-size: 24px;
  font-weight: 700;
  color: var(--td-text-primary, #111);
}

.cohort-dashboard__stat-value--accept {
  color: var(--td-success, #16a34a);
}

.cohort-dashboard__stat-value--edit {
  color: var(--td-warning, #d97706);
}

.cohort-dashboard__stat-value--reject {
  color: var(--td-error, #dc2626);
}

.cohort-dashboard__stat-label {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--td-text-secondary, #666);
  margin-top: 4px;
}

.cohort-dashboard__table-wrap {
  overflow-x: auto;
}

.cohort-dashboard__table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.cohort-dashboard__table th {
  text-align: left;
  padding: 10px 12px;
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--td-text-tertiary, #999);
  border-bottom: 1px solid var(--td-border-ghost, #eee);
}

.cohort-dashboard__table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--td-border-ghost, #f5f5f5);
  color: var(--td-text-primary, #333);
}

.cohort-dashboard__table tbody tr:last-child td {
  border-bottom: none;
}

.cohort-dashboard__version {
  font-weight: 600;
  font-family: monospace;
  font-size: 12px;
}

.cohort-dashboard__bars {
  min-width: 120px;
}

.cohort-dashboard__bar-stack {
  display: flex;
  height: 16px;
  border-radius: 3px;
  overflow: hidden;
  background: var(--td-surface-sunken, #f0f0f0);
}

.cohort-dashboard__bar {
  height: 100%;
  min-width: 2px;
  transition: width 300ms ease;
}

.cohort-dashboard__bar--accept {
  background: var(--td-success, #16a34a);
}

.cohort-dashboard__bar--edit {
  background: var(--td-warning, #d97706);
}

.cohort-dashboard__bar--reject {
  background: var(--td-error, #dc2626);
}

.cohort-dashboard__insight {
  margin-top: 16px;
  padding: 10px 14px;
  background: rgba(22, 163, 74, 0.06);
  border-left: 3px solid var(--td-success, #16a34a);
  border-radius: 0 6px 6px 0;
  font-size: 13px;
  color: var(--td-text-secondary, #555);
}

.cohort-dashboard__empty {
  padding: 40px;
  text-align: center;
  color: var(--td-text-secondary, #666);
}

@media (max-width: 768px) {
  .cohort-dashboard__summary {
    grid-template-columns: repeat(2, 1fr);
  }

  .cohort-dashboard__header {
    flex-direction: column;
    gap: 12px;
  }
}
</style>
