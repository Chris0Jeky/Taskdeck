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
  if (value <= 0) return '0%'
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
        <h2 class="tk-h3 cohort-dashboard__title">Cohort Performance</h2>
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
/* ── Paper & Graphite — CohortDashboard ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens live under `.paper` / `.paper-night`, so var() fallbacks keep the
   panel legible if rendered outside the Paper shell.  The accept/edit/reject
   series previously used raw Tailwind-palette hexes (#16a34a / #d97706 /
   #dc2626); they now read the earth-tone semantic tokens. */

.cohort-dashboard {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  padding: var(--s-6, 24px);
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

.cohort-dashboard__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--s-4, 16px);
  margin-bottom: var(--s-5, 20px);
}

.cohort-dashboard__title {
  margin: 0;
  font-size: var(--t-lg, 18px);
}

.cohort-dashboard__subtitle {
  font-size: var(--t-md, 13.5px);
  color: var(--ink-2, #3a352d);
  margin: var(--s-1, 4px) 0 0;
}

.cohort-dashboard__select {
  padding: var(--s-1, 4px) var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-md, 13.5px);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
}

.cohort-dashboard__select:focus {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

.cohort-dashboard__loading {
  padding: var(--s-10, 40px);
  text-align: center;
  color: var(--mute, #6c6557);
}

.cohort-dashboard__error {
  padding: var(--s-6, 24px);
  text-align: center;
  color: var(--overdue, #8c4a26);
}

.cohort-dashboard__retry {
  margin-top: var(--s-2, 8px);
  padding: var(--s-1, 4px) var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  cursor: pointer;
  font-family: inherit;
  font-size: var(--t-md, 13.5px);
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease);
}

.cohort-dashboard__retry:hover { background: var(--paper-2, #ebe5d8); }

.cohort-dashboard__summary {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: var(--s-4, 16px);
  margin-bottom: var(--s-6, 24px);
}

.cohort-dashboard__stat {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: var(--s-4, 16px);
  background: var(--paper, #f3eee5);
  border: 1px solid var(--line-soft, #e3dcc9);
  border-radius: var(--r-2, 4px);
}

.cohort-dashboard__stat-value {
  font-family: var(--mono, ui-monospace, monospace);
  font-feature-settings: "tnum" 1;
  font-size: var(--t-h3, 22px);
  font-weight: 700;
  color: var(--ink-deep, #0a0908);
}

.cohort-dashboard__stat-value--accept { color: var(--applied, #4a6b3f); }
.cohort-dashboard__stat-value--edit { color: var(--overdue, #8c4a26); }
.cohort-dashboard__stat-value--reject { color: var(--ember-deep, #7a2e15); }

.cohort-dashboard__stat-label {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--mute, #6c6557);
  margin-top: var(--s-1, 4px);
}

.cohort-dashboard__table-wrap {
  overflow-x: auto;
}

.cohort-dashboard__table {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--t-md, 13.5px);
}

.cohort-dashboard__table th {
  text-align: left;
  padding: var(--s-2, 8px) var(--s-3, 12px);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--mute, #6c6557);
  border-bottom: 1px solid var(--line, #d8d0bf);
}

.cohort-dashboard__table td {
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border-bottom: 1px solid var(--line-soft, #e3dcc9);
  color: var(--ink, #1a1814);
}

.cohort-dashboard__table tbody tr:last-child td {
  border-bottom: none;
}

.cohort-dashboard__version {
  font-weight: 600;
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-sm, 12px);
}

.cohort-dashboard__bars {
  min-width: 120px;
}

.cohort-dashboard__bar-stack {
  display: flex;
  height: 16px;
  border-radius: var(--r-1, 2px);
  overflow: hidden;
  background: var(--paper-2, #ebe5d8);
}

.cohort-dashboard__bar {
  height: 100%;
  transition: width var(--d-press, 320ms) var(--ease-paper, ease);
}

.cohort-dashboard__bar--accept { background: var(--applied, #4a6b3f); }
.cohort-dashboard__bar--edit { background: var(--overdue, #8c4a26); }
.cohort-dashboard__bar--reject { background: var(--ember-deep, #7a2e15); }

.cohort-dashboard__insight {
  margin-top: var(--s-4, 16px);
  padding: var(--s-2, 8px) var(--s-4, 16px);
  background: var(--applied-tint, #d8e0ce);
  border-left: 3px solid var(--applied, #4a6b3f);
  border-radius: 0 var(--r-2, 4px) var(--r-2, 4px) 0;
  font-size: var(--t-md, 13.5px);
  color: var(--ink-2, #3a352d);
}

.cohort-dashboard__empty {
  padding: var(--s-10, 40px);
  text-align: center;
  color: var(--mute, #6c6557);
}

@media (max-width: 768px) {
  .cohort-dashboard__summary {
    grid-template-columns: repeat(2, 1fr);
  }

  .cohort-dashboard__header {
    flex-direction: column;
    gap: var(--s-3, 12px);
  }
}
</style>
