<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
import { useBoardStore } from '../store/boardStore'
import { useMetricsStore } from '../store/metricsStore'
import { useToastStore } from '../store/toastStore'
import { TdSkeleton } from '../components/ui'
import { metricsApi } from '../api/metricsApi'
import { getErrorDisplay } from '../composables/useErrorMapper'
import type { MetricsQuery, ForecastQuery } from '../types/metrics'
import type { Board } from '../types/board'

const boardStore = useBoardStore()
const metricsStore = useMetricsStore()
const toast = useToastStore()

const selectedBoardId = ref<string>('')
const dateRangeDays = ref(30)
const boards = ref<Board[]>([])
const boardsLoading = ref(false)
const exporting = ref(false)

const fromDate = computed(() => {
  const d = new Date()
  d.setDate(d.getDate() - dateRangeDays.value)
  return d.toISOString()
})

const toDate = computed(() => new Date().toISOString())

const canFetch = computed(() => !!selectedBoardId.value)

async function loadBoards() {
  boardsLoading.value = true
  try {
    await boardStore.fetchBoards()
    boards.value = boardStore.boards
  } finally {
    boardsLoading.value = false
  }
}

async function fetchMetrics() {
  if (!selectedBoardId.value) return

  const query: MetricsQuery = {
    boardId: selectedBoardId.value,
    from: fromDate.value,
    to: toDate.value,
  }

  try {
    await metricsStore.fetchBoardMetrics(query)
  } catch {
    // Error is surfaced by the store via toast
  }
}

async function fetchForecast() {
  if (!selectedBoardId.value) return

  const query: ForecastQuery = {
    boardId: selectedBoardId.value,
    historyDays: dateRangeDays.value,
  }

  try {
    await metricsStore.fetchBoardForecast(query)
  } catch {
    // Error is surfaced by the store via toast
  }
}

// Auto-fetch when board selection or date range changes
watch([selectedBoardId, dateRangeDays], () => {
  if (canFetch.value) {
    void fetchMetrics()
    void fetchForecast()
  }
})

onMounted(async () => {
  await loadBoards()

  // Auto-select the first board if available
  if (boards.value.length > 0 && !selectedBoardId.value) {
    selectedBoardId.value = boards.value[0].id
  }
})

async function exportCsv() {
  if (!selectedBoardId.value) return
  exporting.value = true
  try {
    const query: MetricsQuery = {
      boardId: selectedBoardId.value,
      from: fromDate.value,
      to: toDate.value,
    }
    await metricsApi.exportBoardMetricsCsv(query)
  } catch (e: unknown) {
    const { message } = getErrorDisplay(e, 'Failed to export CSV')
    toast.error(message)
  } finally {
    exporting.value = false
  }
}

// Computed helpers for the template
const metrics = computed(() => metricsStore.metrics)
const loading = computed(() => metricsStore.loading)
const error = computed(() => metricsStore.error)
const hasData = computed(() => !!metrics.value)

// Forecast helpers
const forecast = computed(() => metricsStore.forecast)
const forecastLoading = computed(() => metricsStore.forecastLoading)
const forecastError = computed(() => metricsStore.forecastError)

function formatDate(iso: string | null): string {
  if (!iso) return 'Unknown'
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

function daysFromNow(iso: string | null): string {
  if (!iso) return '?'
  const diff = Math.ceil((new Date(iso).getTime() - Date.now()) / (1000 * 60 * 60 * 24))
  if (diff <= 0) return 'today'
  if (diff === 1) return '1 day'
  return `${diff} days`
}

const maxThroughput = computed(() => {
  if (!metrics.value?.throughput.length) return 1
  // Use reduce instead of Math.max(...spread) to avoid stack overflow on large arrays
  return metrics.value.throughput.reduce((max, d) => Math.max(max, d.completedCount), 1)
})

const maxWipCount = computed(() => {
  if (!metrics.value?.wipSnapshots.length) return 1
  // Use reduce instead of Math.max(...spread) to avoid stack overflow on large arrays
  return metrics.value.wipSnapshots.reduce((max, w) => Math.max(max, w.cardCount), 1)
})
</script>

<template>
  <div class="paper-metrics">
    <header class="paper-metrics__hero">
      <div class="paper-metrics__hero-copy">
        <span class="tk-eyebrow paper-metrics__eyebrow">Analytics</span>
        <h1 class="tk-h1 paper-metrics__title">Board Metrics</h1>
        <p class="tk-lede paper-metrics__subtitle">
          Throughput, cycle time, work-in-progress, and blocked card trends for your boards.
        </p>
      </div>
    </header>

    <!-- Filters -->
    <section class="paper-metrics__filters" aria-label="Metric filters">
      <div class="paper-metrics__filter-group">
        <label for="board-select" class="paper-metrics__label">Board</label>
        <select
          id="board-select"
          v-model="selectedBoardId"
          class="paper-metrics__select"
          :disabled="boardsLoading"
        >
          <option value="" disabled>Select a board</option>
          <option v-for="b in boards" :key="b.id" :value="b.id">
            {{ b.name }}
          </option>
        </select>
      </div>

      <div class="paper-metrics__filter-group">
        <label for="range-select" class="paper-metrics__label">Date Range</label>
        <select id="range-select" v-model="dateRangeDays" class="paper-metrics__select">
          <option :value="7">Last 7 days</option>
          <option :value="14">Last 14 days</option>
          <option :value="30">Last 30 days</option>
          <option :value="60">Last 60 days</option>
          <option :value="90">Last 90 days</option>
        </select>
      </div>

      <div class="paper-metrics__filter-group paper-metrics__filter-group--action">
        <PaperHLBtn
          class="paper-metrics__export-btn"
          :disabled="!hasData || exporting"
          @click="exportCsv"
          title="Export current metrics as CSV"
        >
          {{ exporting ? 'Exporting...' : 'Export CSV' }}
        </PaperHLBtn>
      </div>
    </section>

    <!-- Loading state -->
    <div v-if="loading" class="paper-metrics__skeleton" role="status" aria-live="polite">
      <span class="sr-only">Loading metrics...</span>
      <!-- Summary card skeletons -->
      <div class="paper-metrics__summary">
        <div v-for="n in 4" :key="n" class="paper-metrics__card">
          <TdSkeleton width="100px" height="12px" />
          <TdSkeleton width="60px" height="28px" />
          <TdSkeleton width="80px" height="10px" />
        </div>
      </div>
      <!-- Chart skeleton -->
      <div class="paper-metrics__section">
        <TdSkeleton width="160px" height="18px" />
        <div class="paper-metrics__skeleton-chart">
          <TdSkeleton width="100%" height="200px" />
        </div>
      </div>
      <!-- WIP skeleton -->
      <div class="paper-metrics__section">
        <TdSkeleton width="120px" height="18px" />
        <div class="paper-metrics__skeleton-rows">
          <div v-for="n in 3" :key="n" class="paper-metrics__skeleton-wip-row">
            <TdSkeleton width="100px" height="14px" />
            <TdSkeleton width="100%" height="24px" />
            <TdSkeleton width="40px" height="14px" />
          </div>
        </div>
      </div>
    </div>

    <!-- Error state -->
    <div v-else-if="error" class="paper-metrics__state paper-metrics__state--error" role="alert">
      <p class="paper-metrics__error-message">{{ error }}</p>
      <PaperHLBtn class="paper-metrics__retry" variant="ember" @click="fetchMetrics">Retry</PaperHLBtn>
    </div>

    <!-- Empty state -->
    <div v-else-if="!hasData && canFetch" class="paper-metrics__state">
      <p>No metrics data available. Select a board to get started.</p>
    </div>

    <div v-else-if="!canFetch" class="paper-metrics__state">
      <p>Select a board above to view its metrics.</p>
    </div>

    <!-- Dashboard -->
    <div v-else-if="hasData && metrics" class="paper-metrics__dashboard">
      <!-- Summary cards -->
      <div class="paper-metrics__summary">
        <div class="paper-metrics__card">
          <span class="paper-metrics__card-label">Total Throughput</span>
          <span class="paper-metrics__card-value">
            {{ metrics.throughput.reduce((sum, d) => sum + d.completedCount, 0) }}
          </span>
          <span class="paper-metrics__card-unit">cards completed</span>
        </div>

        <div class="paper-metrics__card">
          <span class="paper-metrics__card-label">Avg Cycle Time</span>
          <span class="paper-metrics__card-value">{{ metrics.averageCycleTimeDays }}</span>
          <span class="paper-metrics__card-unit">days</span>
        </div>

        <div class="paper-metrics__card">
          <span class="paper-metrics__card-label">Current WIP</span>
          <span class="paper-metrics__card-value">{{ metrics.totalWip }}</span>
          <span class="paper-metrics__card-unit">cards in progress</span>
        </div>

        <div class="paper-metrics__card" :class="{ 'paper-metrics__card--alert': metrics.blockedCount > 0 }">
          <span class="paper-metrics__card-label">Blocked</span>
          <span class="paper-metrics__card-value">{{ metrics.blockedCount }}</span>
          <span class="paper-metrics__card-unit">cards blocked</span>
        </div>
      </div>

      <!-- Forecast section -->
      <section class="paper-metrics__section paper-metrics__forecast" aria-label="Completion forecast">
        <h2 class="paper-metrics__section-title">Completion Forecast</h2>

        <div v-if="forecastLoading" class="paper-metrics__forecast-loading" role="status">
          <span class="sr-only">Computing forecast...</span>
          <div class="paper-metrics__forecast-grid">
            <div v-for="n in 4" :key="n" class="paper-metrics__card">
              <TdSkeleton width="80px" height="12px" />
              <TdSkeleton width="50px" height="24px" />
              <TdSkeleton width="70px" height="10px" />
            </div>
          </div>
        </div>

        <div v-else-if="forecastError" class="paper-metrics__forecast-error" role="alert">
          <p>{{ forecastError }}</p>
          <PaperHLBtn class="paper-metrics__retry" variant="ghost" @click="fetchForecast">Retry</PaperHLBtn>
        </div>

        <div v-else-if="forecast" class="paper-metrics__forecast-content">
          <!-- Estimate cards -->
          <div class="paper-metrics__forecast-grid">
            <div class="paper-metrics__card">
              <span class="paper-metrics__card-label">Remaining</span>
              <span class="paper-metrics__card-value">{{ forecast.remainingCards }}</span>
              <span class="paper-metrics__card-unit">cards left</span>
            </div>

            <div class="paper-metrics__card">
              <span class="paper-metrics__card-label">Avg Throughput</span>
              <span class="paper-metrics__card-value">{{ forecast.averageThroughputPerDay.toFixed(2) }}</span>
              <span class="paper-metrics__card-unit">cards / day</span>
            </div>

            <div class="paper-metrics__card">
              <span class="paper-metrics__card-label">Estimated Completion</span>
              <span class="paper-metrics__card-value paper-metrics__card-value--date">
                {{ forecast.estimatedCompletionDate ? formatDate(forecast.estimatedCompletionDate) : 'N/A' }}
              </span>
              <span v-if="forecast.estimatedCompletionDate" class="paper-metrics__card-unit">
                ~{{ daysFromNow(forecast.estimatedCompletionDate) }} from now
              </span>
            </div>

            <div class="paper-metrics__card">
              <span class="paper-metrics__card-label">Data Points</span>
              <span class="paper-metrics__card-value">{{ forecast.dataPointCount }}</span>
              <span class="paper-metrics__card-unit">over {{ forecast.historyDaysUsed }} days</span>
            </div>
          </div>

          <!-- Confidence band -->
          <div v-if="forecast.confidenceBand" class="paper-metrics__confidence">
            <h3 class="paper-metrics__confidence-title">Confidence Range</h3>
            <div class="paper-metrics__confidence-band">
              <div class="paper-metrics__confidence-row">
                <span class="paper-metrics__confidence-label paper-metrics__confidence-label--optimistic">Optimistic</span>
                <span class="paper-metrics__confidence-date">
                  {{ formatDate(forecast.confidenceBand.lowEstimate) }}
                </span>
                <span class="paper-metrics__confidence-rate">
                  ({{ forecast.confidenceBand.highThroughputPerDay.toFixed(2) }} cards/day)
                </span>
              </div>
              <div class="paper-metrics__confidence-row paper-metrics__confidence-row--expected">
                <span class="paper-metrics__confidence-label">Expected</span>
                <span class="paper-metrics__confidence-date">
                  {{ formatDate(forecast.confidenceBand.expectedEstimate) }}
                </span>
                <span class="paper-metrics__confidence-rate">
                  ({{ forecast.confidenceBand.expectedThroughputPerDay.toFixed(2) }} cards/day)
                </span>
              </div>
              <div class="paper-metrics__confidence-row">
                <span class="paper-metrics__confidence-label paper-metrics__confidence-label--pessimistic">Pessimistic</span>
                <span class="paper-metrics__confidence-date">
                  {{ formatDate(forecast.confidenceBand.highEstimate) }}
                </span>
                <span class="paper-metrics__confidence-rate">
                  ({{ forecast.confidenceBand.lowThroughputPerDay.toFixed(2) }} cards/day)
                </span>
              </div>
            </div>
          </div>

          <!-- Caveats -->
          <div v-if="forecast.caveats.length > 0" class="paper-metrics__caveats" role="note">
            <h3 class="paper-metrics__caveats-title">Caveats</h3>
            <ul class="paper-metrics__caveats-list">
              <li v-for="(caveat, i) in forecast.caveats" :key="i">{{ caveat }}</li>
            </ul>
          </div>

          <!-- Assumptions -->
          <details class="paper-metrics__assumptions">
            <summary class="paper-metrics__assumptions-summary">Assumptions ({{ forecast.assumptions.length }})</summary>
            <ul class="paper-metrics__assumptions-list">
              <li v-for="(assumption, i) in forecast.assumptions" :key="i">{{ assumption }}</li>
            </ul>
          </details>
        </div>
      </section>

      <!-- Throughput chart -->
      <section class="paper-metrics__section" aria-label="Throughput trend">
        <h2 class="paper-metrics__section-title">Throughput Trend</h2>
        <div v-if="metrics.throughput.length === 0" class="paper-metrics__empty-chart">
          <p>No completed cards in this period.</p>
        </div>
        <div v-else class="paper-metrics__bar-chart" role="img" aria-label="Throughput bar chart">
          <div
            v-for="dp in metrics.throughput"
            :key="dp.date"
            class="paper-metrics__bar-group"
          >
            <div
              class="paper-metrics__bar"
              :style="{ '--pm-bar-size': `${(dp.completedCount / maxThroughput) * 100}%` }"
              :title="`${dp.completedCount} completed`"
            />
            <span class="paper-metrics__bar-label">{{ new Date(dp.date).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) }}</span>
          </div>
        </div>
      </section>

      <!-- WIP by column -->
      <section class="paper-metrics__section" aria-label="WIP by column">
        <h2 class="paper-metrics__section-title">WIP by Column</h2>
        <div v-if="metrics.wipSnapshots.length === 0" class="paper-metrics__empty-chart">
          <p>No columns found.</p>
        </div>
        <div v-else class="paper-metrics__wip-chart">
          <div
            v-for="wip in metrics.wipSnapshots"
            :key="wip.columnId"
            class="paper-metrics__wip-row"
          >
            <span class="paper-metrics__wip-name">{{ wip.columnName }}</span>
            <div class="paper-metrics__wip-bar-track">
              <div
                class="paper-metrics__wip-bar-fill"
                :style="{ '--pm-bar-size': `${(wip.cardCount / maxWipCount) * 100}%` }"
                :class="{ 'paper-metrics__wip-bar-fill--over': wip.wipLimit !== null && wip.cardCount > wip.wipLimit }"
              />
            </div>
            <span class="paper-metrics__wip-count">
              {{ wip.cardCount }}
              <template v-if="wip.wipLimit !== null"> / {{ wip.wipLimit }}</template>
            </span>
          </div>
        </div>
      </section>

      <!-- Cycle time entries -->
      <section class="paper-metrics__section" aria-label="Cycle time entries">
        <h2 class="paper-metrics__section-title">Cycle Time Details</h2>
        <div v-if="metrics.cycleTimeEntries.length === 0" class="paper-metrics__empty-chart">
          <p>No completed cards to compute cycle time.</p>
        </div>
        <table v-else class="paper-metrics__table">
          <thead>
            <tr>
              <th>Card</th>
              <th>Cycle Time (days)</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="entry in metrics.cycleTimeEntries" :key="entry.cardId">
              <td>{{ entry.cardTitle }}</td>
              <td>{{ entry.cycleTimeDays }}</td>
            </tr>
          </tbody>
        </table>
      </section>

      <!-- Blocked cards -->
      <section class="paper-metrics__section" aria-label="Blocked cards">
        <h2 class="paper-metrics__section-title">Blocked Cards</h2>
        <div v-if="metrics.blockedCards.length === 0" class="paper-metrics__empty-chart">
          <p>No blocked cards. Great!</p>
        </div>
        <table v-else class="paper-metrics__table">
          <thead>
            <tr>
              <th>Card</th>
              <th>Reason</th>
              <th>Duration (days)</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="blocked in metrics.blockedCards"
              :key="blocked.cardId"
              class="paper-metrics__row--blocked"
            >
              <td>{{ blocked.cardTitle }}</td>
              <td>{{ blocked.blockReason ?? 'No reason given' }}</td>
              <td>{{ blocked.blockedDurationDays }}</td>
            </tr>
          </tbody>
        </table>
      </section>
    </div>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — MetricsView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   The tokens live under `.paper` / `.paper-night` (the canonical shell), so the
   var() fallbacks keep this surface legible if it is ever rendered outside the
   Paper shell (Legacy/Obsidian "off" mode).

   This was the heaviest --td-* consumer in the app.  Semantic colors map onto
   the Paper family: alert/danger -> --overdue, optimistic -> --applied, chart
   accent + caveats -> --ember.  The chart-size custom property is renamed
   --td-bar-size -> --pm-bar-size; it is a view-local variable set inline in the
   template, not a design token. */

.paper-metrics {
  max-width: 1200px;
  margin: 0 auto;
  padding: var(--s-6, 24px);
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

/* ── Hero ── */

.paper-metrics__hero {
  margin-bottom: var(--s-8, 32px);
}

.paper-metrics__eyebrow {
  color: var(--ember, #a8421f);
}

.paper-metrics__title {
  margin: var(--s-2, 8px) 0;
  font-size: var(--t-h2, 32px);
}

.paper-metrics__subtitle {
  margin: 0;
  color: var(--ink-2, #3a352d);
  max-width: 600px;
}

/* ── Filters ── */

.paper-metrics__filters {
  display: flex;
  gap: var(--s-4, 16px);
  margin-bottom: var(--s-6, 24px);
  flex-wrap: wrap;
}

.paper-metrics__filter-group {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
}

.paper-metrics__label {
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  color: var(--mute, #6c6557);
  text-transform: uppercase;
  letter-spacing: 0.1em;
}

.paper-metrics__filter-group--action {
  justify-content: flex-end;
  align-self: flex-end;
}

.paper-metrics__select {
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-md, 13.5px);
  min-width: 180px;
  transition: border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-metrics__select:focus {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

.paper-metrics__select:disabled {
  color: var(--mute, #6c6557);
  cursor: not-allowed;
}

/* ── States ── */

.paper-metrics__state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: var(--s-12, 56px);
  color: var(--ink-2, #3a352d);
  text-align: center;
  gap: var(--s-4, 16px);
}

.paper-metrics__state--error {
  color: var(--overdue, #8c4a26);
}

.paper-metrics__error-message {
  margin: 0;
  font-weight: 600;
}

.paper-metrics__skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--s-8, 32px);
}

.paper-metrics__skeleton-chart {
  margin-top: var(--s-4, 16px);
  border-radius: var(--r-2, 4px);
  overflow: hidden;
}

.paper-metrics__skeleton-rows {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
  margin-top: var(--s-4, 16px);
}

.paper-metrics__skeleton-wip-row {
  display: grid;
  grid-template-columns: 120px 1fr 60px;
  align-items: center;
  gap: var(--s-3, 12px);
}

/* ── Summary cards ── */

.paper-metrics__summary {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: var(--s-4, 16px);
  margin-bottom: var(--s-8, 32px);
}

.paper-metrics__card {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  padding: var(--s-5, 20px);
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
}

.paper-metrics__card--alert {
  border-color: var(--overdue, #8c4a26);
  background: var(--overdue-tint, #ecd9c4);
}

.paper-metrics__card-label {
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--mute, #6c6557);
}

.paper-metrics__card-value {
  font-family: var(--mono, ui-monospace, monospace);
  font-feature-settings: 'tnum' 1;
  font-size: var(--t-h3, 22px);
  font-weight: 600;
  color: var(--ink-deep, #0a0908);
}

.paper-metrics__card-unit {
  font-size: var(--t-xs, 10.5px);
  color: var(--ink-2, #3a352d);
}

/* ── Sections ── */

.paper-metrics__section {
  margin-bottom: var(--s-8, 32px);
}

.paper-metrics__section-title {
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-lg, 18px);
  font-weight: 500;
  color: var(--ink-deep, #0a0908);
  margin: 0 0 var(--s-4, 16px);
}

.paper-metrics__empty-chart {
  padding: var(--s-8, 32px);
  text-align: center;
  color: var(--ink-2, #3a352d);
  background: var(--paper-2, #ebe5d8);
  border-radius: var(--r-2, 4px);
  border: 1px dashed var(--line, #d8d0bf);
}

/* ── Throughput bar chart ── */

.paper-metrics__bar-chart {
  display: flex;
  align-items: flex-end;
  gap: var(--s-2, 8px);
  height: 200px;
  padding: var(--s-4, 16px);
  background: var(--paper-card, #fbf7ee);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  overflow-x: auto;
}

.paper-metrics__bar-group {
  display: flex;
  flex-direction: column;
  align-items: center;
  flex: 1;
  min-width: 40px;
  height: 100%;
  justify-content: flex-end;
}

.paper-metrics__bar {
  width: 100%;
  max-width: 40px;
  height: var(--pm-bar-size, 0%);
  background: var(--ember, #a8421f);
  border-radius: var(--r-1, 2px) var(--r-1, 2px) 0 0;
  min-height: 4px;
  transition: height var(--d-base, 240ms) var(--ease-paper, ease);
}

.paper-metrics__bar-label {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #6c6557);
  margin-top: var(--s-1, 4px);
  white-space: nowrap;
}

/* ── WIP chart ── */

.paper-metrics__wip-chart {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
}

.paper-metrics__wip-row {
  display: grid;
  grid-template-columns: 120px 1fr 60px;
  align-items: center;
  gap: var(--s-3, 12px);
}

.paper-metrics__wip-name {
  font-size: var(--t-sm, 12px);
  font-weight: 600;
  color: var(--ink, #1a1814);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.paper-metrics__wip-bar-track {
  height: 24px;
  background: var(--paper-2, #ebe5d8);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  overflow: hidden;
}

.paper-metrics__wip-bar-fill {
  height: 100%;
  width: var(--pm-bar-size, 0%);
  background: var(--ember, #a8421f);
  border-radius: var(--r-2, 4px);
  transition: width var(--d-base, 240ms) var(--ease-paper, ease);
  min-width: 4px;
}

.paper-metrics__wip-bar-fill--over {
  background: var(--overdue, #8c4a26);
}

.paper-metrics__wip-count {
  font-family: var(--mono, ui-monospace, monospace);
  font-feature-settings: 'tnum' 1;
  font-size: var(--t-sm, 12px);
  font-weight: 600;
  color: var(--ink-2, #3a352d);
  text-align: right;
}

/* ── Tables ── */

.paper-metrics__table {
  width: 100%;
  border-collapse: collapse;
  background: var(--paper-card, #fbf7ee);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  overflow: hidden;
}

.paper-metrics__table th {
  text-align: left;
  padding: var(--s-3, 12px) var(--s-4, 16px);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.22em;
  color: var(--mute, #6c6557);
  background: var(--paper-2, #ebe5d8);
  border-bottom: 1px solid var(--line, #d8d0bf);
}

.paper-metrics__table td {
  padding: var(--s-3, 12px) var(--s-4, 16px);
  font-size: var(--t-sm, 12px);
  color: var(--ink, #1a1814);
  border-bottom: 1px solid var(--line-soft, #e3dcc9);
}

.paper-metrics__table tbody tr:last-child td {
  border-bottom: none;
}

.paper-metrics__row--blocked td {
  color: var(--overdue, #8c4a26);
}

/* ── Forecast ── */

.paper-metrics__forecast {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  padding: var(--s-6, 24px);
}

.paper-metrics__forecast-loading {
  display: flex;
  flex-direction: column;
  gap: var(--s-4, 16px);
}

.paper-metrics__forecast-error {
  display: flex;
  align-items: center;
  gap: var(--s-3, 12px);
  color: var(--overdue, #8c4a26);
  font-size: var(--t-sm, 12px);
}

.paper-metrics__forecast-error p {
  margin: 0;
}

.paper-metrics__forecast-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: var(--s-4, 16px);
  margin-bottom: var(--s-6, 24px);
}

.paper-metrics__card-value--date {
  font-size: var(--t-lg, 18px);
}

.paper-metrics__confidence {
  margin-bottom: var(--s-5, 20px);
}

.paper-metrics__confidence-title {
  font-size: var(--t-sm, 12px);
  font-weight: 700;
  color: var(--ink-2, #3a352d);
  text-transform: uppercase;
  letter-spacing: 0.1em;
  margin: 0 0 var(--s-3, 12px);
}

.paper-metrics__confidence-band {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  padding: var(--s-4, 16px);
  background: var(--paper-2, #ebe5d8);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line-soft, #e3dcc9);
  box-shadow: var(--shadow-press, inset 0 1px 0 #1a181410);
}

.paper-metrics__confidence-row {
  display: grid;
  grid-template-columns: 100px 1fr auto;
  gap: var(--s-3, 12px);
  align-items: center;
  font-size: var(--t-sm, 12px);
}

.paper-metrics__confidence-row--expected {
  font-weight: 700;
  color: var(--ink-deep, #0a0908);
}

.paper-metrics__confidence-label {
  font-weight: 600;
  color: var(--ink-2, #3a352d);
}

.paper-metrics__confidence-label--optimistic {
  color: var(--applied, #4a6b3f);
}

.paper-metrics__confidence-label--pessimistic {
  color: var(--overdue, #8c4a26);
}

.paper-metrics__confidence-date {
  color: var(--ink, #1a1814);
}

.paper-metrics__confidence-rate {
  font-family: var(--mono, ui-monospace, monospace);
  color: var(--mute, #6c6557);
  font-size: var(--t-xs, 10.5px);
}

/* ── Caveats & assumptions ── */

.paper-metrics__caveats {
  margin-bottom: var(--s-4, 16px);
  padding: var(--s-4, 16px);
  background: var(--ember-bloom, #a8421f1a);
  border-left: 3px solid var(--ember, #a8421f);
  border-radius: 0 var(--r-2, 4px) var(--r-2, 4px) 0;
}

.paper-metrics__caveats-title {
  font-size: var(--t-xs, 10.5px);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--ember, #a8421f);
  margin: 0 0 var(--s-2, 8px);
}

.paper-metrics__caveats-list {
  list-style: disc;
  padding-left: var(--s-5, 20px);
  margin: 0;
  font-size: var(--t-sm, 12px);
  color: var(--ink-2, #3a352d);
}

.paper-metrics__caveats-list li {
  margin-bottom: var(--s-1, 4px);
}

.paper-metrics__assumptions {
  font-size: var(--t-sm, 12px);
  color: var(--mute, #6c6557);
}

.paper-metrics__assumptions-summary {
  cursor: pointer;
  font-weight: 600;
  color: var(--ink-2, #3a352d);
  padding: var(--s-2, 8px) 0;
}

.paper-metrics__assumptions-list {
  list-style: disc;
  padding-left: var(--s-5, 20px);
  margin-top: var(--s-2, 8px);
}

.paper-metrics__assumptions-list li {
  margin-bottom: var(--s-1, 4px);
}

/* ── Responsive ── */

@media (max-width: 640px) {
  .paper-metrics {
    padding: var(--s-4, 16px);
  }

  .paper-metrics__summary {
    grid-template-columns: 1fr 1fr;
  }

  .paper-metrics__wip-row {
    grid-template-columns: 80px 1fr 50px;
  }

  .paper-metrics__filters {
    flex-direction: column;
  }
}
</style>
