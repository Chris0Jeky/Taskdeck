<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useBoardStore } from '../store/boardStore'
import { useMetricsStore } from '../store/metricsStore'
import type { MetricsQuery, ForecastQuery } from '../types/metrics'
import type { Board } from '../types/board'

const boardStore = useBoardStore()
const metricsStore = useMetricsStore()

const selectedBoardId = ref<string>('')
const dateRangeDays = ref(30)
const boards = ref<Board[]>([])
const boardsLoading = ref(false)

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
  <div class="td-metrics">
    <header class="td-metrics__hero">
      <div class="td-metrics__hero-copy">
        <span class="td-metrics__eyebrow">Analytics</span>
        <h1 class="td-page-title">Board Metrics</h1>
        <p class="td-metrics__subtitle">
          Throughput, cycle time, work-in-progress, and blocked card trends for your boards.
        </p>
      </div>
    </header>

    <!-- Filters -->
    <section class="td-metrics__filters" aria-label="Metric filters">
      <div class="td-metrics__filter-group">
        <label for="board-select" class="td-metrics__label">Board</label>
        <select
          id="board-select"
          v-model="selectedBoardId"
          class="td-metrics__select"
          :disabled="boardsLoading"
        >
          <option value="" disabled>Select a board</option>
          <option v-for="b in boards" :key="b.id" :value="b.id">
            {{ b.name }}
          </option>
        </select>
      </div>

      <div class="td-metrics__filter-group">
        <label for="range-select" class="td-metrics__label">Date Range</label>
        <select id="range-select" v-model="dateRangeDays" class="td-metrics__select">
          <option :value="7">Last 7 days</option>
          <option :value="14">Last 14 days</option>
          <option :value="30">Last 30 days</option>
          <option :value="60">Last 60 days</option>
          <option :value="90">Last 90 days</option>
        </select>
      </div>
    </section>

    <!-- Loading state -->
    <div v-if="loading" class="td-metrics__state" role="status" aria-live="polite">
      <div class="td-metrics__spinner" />
      <p>Loading metrics...</p>
    </div>

    <!-- Error state -->
    <div v-else-if="error" class="td-metrics__state td-metrics__state--error" role="alert">
      <p class="td-metrics__error-message">{{ error }}</p>
      <button class="td-btn td-btn--primary td-btn--sm" @click="fetchMetrics">Retry</button>
    </div>

    <!-- Empty state -->
    <div v-else-if="!hasData && canFetch" class="td-metrics__state">
      <p>No metrics data available. Select a board to get started.</p>
    </div>

    <div v-else-if="!canFetch" class="td-metrics__state">
      <p>Select a board above to view its metrics.</p>
    </div>

    <!-- Dashboard -->
    <div v-else-if="hasData && metrics" class="td-metrics__dashboard">
      <!-- Summary cards -->
      <div class="td-metrics__summary">
        <div class="td-metrics__card">
          <span class="td-metrics__card-label">Total Throughput</span>
          <span class="td-metrics__card-value">
            {{ metrics.throughput.reduce((sum, d) => sum + d.completedCount, 0) }}
          </span>
          <span class="td-metrics__card-unit">cards completed</span>
        </div>

        <div class="td-metrics__card">
          <span class="td-metrics__card-label">Avg Cycle Time</span>
          <span class="td-metrics__card-value">{{ metrics.averageCycleTimeDays }}</span>
          <span class="td-metrics__card-unit">days</span>
        </div>

        <div class="td-metrics__card">
          <span class="td-metrics__card-label">Current WIP</span>
          <span class="td-metrics__card-value">{{ metrics.totalWip }}</span>
          <span class="td-metrics__card-unit">cards in progress</span>
        </div>

        <div class="td-metrics__card" :class="{ 'td-metrics__card--alert': metrics.blockedCount > 0 }">
          <span class="td-metrics__card-label">Blocked</span>
          <span class="td-metrics__card-value">{{ metrics.blockedCount }}</span>
          <span class="td-metrics__card-unit">cards blocked</span>
        </div>
      </div>

      <!-- Forecast section -->
      <section class="td-metrics__section td-metrics__forecast" aria-label="Completion forecast">
        <h2 class="td-metrics__section-title">Completion Forecast</h2>

        <div v-if="forecastLoading" class="td-metrics__forecast-loading">
          <div class="td-metrics__spinner td-metrics__spinner--sm" />
          <span>Computing forecast...</span>
        </div>

        <div v-else-if="forecastError" class="td-metrics__forecast-error" role="alert">
          <p>{{ forecastError }}</p>
          <button class="td-btn td-btn--ghost td-btn--sm" @click="fetchForecast">Retry</button>
        </div>

        <div v-else-if="forecast" class="td-metrics__forecast-content">
          <!-- Estimate cards -->
          <div class="td-metrics__forecast-grid">
            <div class="td-metrics__card">
              <span class="td-metrics__card-label">Remaining</span>
              <span class="td-metrics__card-value">{{ forecast.remainingCards }}</span>
              <span class="td-metrics__card-unit">cards left</span>
            </div>

            <div class="td-metrics__card">
              <span class="td-metrics__card-label">Avg Throughput</span>
              <span class="td-metrics__card-value">{{ forecast.averageThroughputPerDay.toFixed(2) }}</span>
              <span class="td-metrics__card-unit">cards / day</span>
            </div>

            <div class="td-metrics__card">
              <span class="td-metrics__card-label">Estimated Completion</span>
              <span class="td-metrics__card-value td-metrics__card-value--date">
                {{ forecast.estimatedCompletionDate ? formatDate(forecast.estimatedCompletionDate) : 'N/A' }}
              </span>
              <span v-if="forecast.estimatedCompletionDate" class="td-metrics__card-unit">
                ~{{ daysFromNow(forecast.estimatedCompletionDate) }} from now
              </span>
            </div>

            <div class="td-metrics__card">
              <span class="td-metrics__card-label">Data Points</span>
              <span class="td-metrics__card-value">{{ forecast.dataPointCount }}</span>
              <span class="td-metrics__card-unit">over {{ forecast.historyDaysUsed }} days</span>
            </div>
          </div>

          <!-- Confidence band -->
          <div v-if="forecast.confidenceBand" class="td-metrics__confidence">
            <h3 class="td-metrics__confidence-title">Confidence Range</h3>
            <div class="td-metrics__confidence-band">
              <div class="td-metrics__confidence-row">
                <span class="td-metrics__confidence-label td-metrics__confidence-label--optimistic">Optimistic</span>
                <span class="td-metrics__confidence-date">
                  {{ formatDate(forecast.confidenceBand.lowEstimate) }}
                </span>
                <span class="td-metrics__confidence-rate">
                  ({{ forecast.confidenceBand.highThroughputPerDay.toFixed(2) }} cards/day)
                </span>
              </div>
              <div class="td-metrics__confidence-row td-metrics__confidence-row--expected">
                <span class="td-metrics__confidence-label">Expected</span>
                <span class="td-metrics__confidence-date">
                  {{ formatDate(forecast.confidenceBand.expectedEstimate) }}
                </span>
                <span class="td-metrics__confidence-rate">
                  ({{ forecast.confidenceBand.expectedThroughputPerDay.toFixed(2) }} cards/day)
                </span>
              </div>
              <div class="td-metrics__confidence-row">
                <span class="td-metrics__confidence-label td-metrics__confidence-label--pessimistic">Pessimistic</span>
                <span class="td-metrics__confidence-date">
                  {{ formatDate(forecast.confidenceBand.highEstimate) }}
                </span>
                <span class="td-metrics__confidence-rate">
                  ({{ forecast.confidenceBand.lowThroughputPerDay.toFixed(2) }} cards/day)
                </span>
              </div>
            </div>
          </div>

          <!-- Caveats -->
          <div v-if="forecast.caveats.length > 0" class="td-metrics__caveats" role="note">
            <h3 class="td-metrics__caveats-title">Caveats</h3>
            <ul class="td-metrics__caveats-list">
              <li v-for="(caveat, i) in forecast.caveats" :key="i">{{ caveat }}</li>
            </ul>
          </div>

          <!-- Assumptions -->
          <details class="td-metrics__assumptions">
            <summary class="td-metrics__assumptions-summary">Assumptions ({{ forecast.assumptions.length }})</summary>
            <ul class="td-metrics__assumptions-list">
              <li v-for="(assumption, i) in forecast.assumptions" :key="i">{{ assumption }}</li>
            </ul>
          </details>
        </div>
      </section>

      <!-- Throughput chart -->
      <section class="td-metrics__section" aria-label="Throughput trend">
        <h2 class="td-metrics__section-title">Throughput Trend</h2>
        <div v-if="metrics.throughput.length === 0" class="td-metrics__empty-chart">
          <p>No completed cards in this period.</p>
        </div>
        <div v-else class="td-metrics__bar-chart" role="img" aria-label="Throughput bar chart">
          <div
            v-for="dp in metrics.throughput"
            :key="dp.date"
            class="td-metrics__bar-group"
          >
            <div
              class="td-metrics__bar"
              :style="{ height: `${(dp.completedCount / maxThroughput) * 100}%` }"
              :title="`${dp.completedCount} completed`"
            />
            <span class="td-metrics__bar-label">{{ new Date(dp.date).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) }}</span>
          </div>
        </div>
      </section>

      <!-- WIP by column -->
      <section class="td-metrics__section" aria-label="WIP by column">
        <h2 class="td-metrics__section-title">WIP by Column</h2>
        <div v-if="metrics.wipSnapshots.length === 0" class="td-metrics__empty-chart">
          <p>No columns found.</p>
        </div>
        <div v-else class="td-metrics__wip-chart">
          <div
            v-for="wip in metrics.wipSnapshots"
            :key="wip.columnId"
            class="td-metrics__wip-row"
          >
            <span class="td-metrics__wip-name">{{ wip.columnName }}</span>
            <div class="td-metrics__wip-bar-track">
              <div
                class="td-metrics__wip-bar-fill"
                :style="{ width: `${(wip.cardCount / maxWipCount) * 100}%` }"
                :class="{ 'td-metrics__wip-bar-fill--over': wip.wipLimit !== null && wip.cardCount > wip.wipLimit }"
              />
            </div>
            <span class="td-metrics__wip-count">
              {{ wip.cardCount }}
              <template v-if="wip.wipLimit !== null"> / {{ wip.wipLimit }}</template>
            </span>
          </div>
        </div>
      </section>

      <!-- Cycle time entries -->
      <section class="td-metrics__section" aria-label="Cycle time entries">
        <h2 class="td-metrics__section-title">Cycle Time Details</h2>
        <div v-if="metrics.cycleTimeEntries.length === 0" class="td-metrics__empty-chart">
          <p>No completed cards to compute cycle time.</p>
        </div>
        <table v-else class="td-metrics__table">
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
      <section class="td-metrics__section" aria-label="Blocked cards">
        <h2 class="td-metrics__section-title">Blocked Cards</h2>
        <div v-if="metrics.blockedCards.length === 0" class="td-metrics__empty-chart">
          <p>No blocked cards. Great!</p>
        </div>
        <table v-else class="td-metrics__table">
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
              class="td-metrics__row--blocked"
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
.td-metrics {
  max-width: 1200px;
  margin: 0 auto;
  padding: var(--td-space-6);
}

.td-metrics__hero {
  margin-bottom: var(--td-space-8);
}

.td-metrics__eyebrow {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.15em;
  text-transform: uppercase;
  color: var(--td-color-ember);
}

.td-page-title {
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-2xl);
  font-weight: 800;
  letter-spacing: -0.04em;
  color: var(--td-text-primary);
  margin: var(--td-space-2) 0;
}

.td-metrics__subtitle {
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
  max-width: 600px;
}

/* Filters */
.td-metrics__filters {
  display: flex;
  gap: var(--td-space-4);
  margin-bottom: var(--td-space-6);
  flex-wrap: wrap;
}

.td-metrics__filter-group {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-metrics__label {
  font-size: var(--td-font-xs);
  font-weight: 600;
  color: var(--td-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.1em;
}

.td-metrics__select {
  padding: var(--td-space-2) var(--td-space-3);
  border: 1px solid var(--td-border-ghost);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-container);
  color: var(--td-text-primary);
  font-size: var(--td-font-sm);
  min-width: 180px;
}

.td-metrics__select:focus {
  outline: none;
  box-shadow: var(--td-focus-ring);
}

/* States */
.td-metrics__state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: var(--td-space-12);
  color: var(--td-text-secondary);
  text-align: center;
  gap: var(--td-space-4);
}

.td-metrics__state--error {
  color: var(--td-color-danger, #e53e3e);
}

.td-metrics__error-message {
  font-weight: 600;
}

.td-metrics__spinner {
  width: 32px;
  height: 32px;
  border: 3px solid var(--td-border-ghost);
  border-top-color: var(--td-color-ember);
  border-radius: 50%;
  animation: td-spin 0.8s linear infinite;
}

@keyframes td-spin {
  to { transform: rotate(360deg); }
}

/* Summary cards */
.td-metrics__summary {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: var(--td-space-4);
  margin-bottom: var(--td-space-8);
}

.td-metrics__card {
  background: var(--td-surface-container);
  border: 1px solid var(--td-border-ghost);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-5);
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-metrics__card--alert {
  border-color: var(--td-color-danger, #e53e3e);
}

.td-metrics__card-label {
  font-size: var(--td-font-xs);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--td-text-tertiary);
}

.td-metrics__card-value {
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-2xl);
  font-weight: 800;
  color: var(--td-text-primary);
}

.td-metrics__card-unit {
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
}

/* Sections */
.td-metrics__section {
  margin-bottom: var(--td-space-8);
}

.td-metrics__section-title {
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-lg);
  font-weight: 700;
  color: var(--td-text-primary);
  margin-bottom: var(--td-space-4);
}

.td-metrics__empty-chart {
  padding: var(--td-space-8);
  text-align: center;
  color: var(--td-text-secondary);
  background: var(--td-surface-container);
  border-radius: var(--td-radius-md);
  border: 1px dashed var(--td-border-ghost);
}

/* Throughput bar chart */
.td-metrics__bar-chart {
  display: flex;
  align-items: flex-end;
  gap: var(--td-space-2);
  height: 200px;
  padding: var(--td-space-4);
  background: var(--td-surface-container);
  border-radius: var(--td-radius-md);
  border: 1px solid var(--td-border-ghost);
  overflow-x: auto;
}

.td-metrics__bar-group {
  display: flex;
  flex-direction: column;
  align-items: center;
  flex: 1;
  min-width: 40px;
  height: 100%;
  justify-content: flex-end;
}

.td-metrics__bar {
  width: 100%;
  max-width: 40px;
  background: var(--td-color-ember);
  border-radius: var(--td-radius-sm) var(--td-radius-sm) 0 0;
  min-height: 4px;
  transition: height 0.3s ease;
}

.td-metrics__bar-label {
  font-size: 10px;
  color: var(--td-text-tertiary);
  margin-top: var(--td-space-1);
  white-space: nowrap;
}

/* WIP chart */
.td-metrics__wip-chart {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-metrics__wip-row {
  display: grid;
  grid-template-columns: 120px 1fr 60px;
  align-items: center;
  gap: var(--td-space-3);
}

.td-metrics__wip-name {
  font-size: var(--td-font-sm);
  font-weight: 600;
  color: var(--td-text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.td-metrics__wip-bar-track {
  height: 24px;
  background: var(--td-surface-container);
  border-radius: var(--td-radius-sm);
  border: 1px solid var(--td-border-ghost);
  overflow: hidden;
}

.td-metrics__wip-bar-fill {
  height: 100%;
  background: var(--td-color-ember);
  border-radius: var(--td-radius-sm);
  transition: width 0.3s ease;
  min-width: 4px;
}

.td-metrics__wip-bar-fill--over {
  background: var(--td-color-danger, #e53e3e);
}

.td-metrics__wip-count {
  font-size: var(--td-font-sm);
  font-weight: 600;
  color: var(--td-text-secondary);
  text-align: right;
}

/* Tables */
.td-metrics__table {
  width: 100%;
  border-collapse: collapse;
  background: var(--td-surface-container);
  border-radius: var(--td-radius-md);
  border: 1px solid var(--td-border-ghost);
  overflow: hidden;
}

.td-metrics__table th {
  text-align: left;
  padding: var(--td-space-3) var(--td-space-4);
  font-size: var(--td-font-xs);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--td-text-tertiary);
  background: var(--td-surface-container-high);
  border-bottom: 1px solid var(--td-border-ghost);
}

.td-metrics__table td {
  padding: var(--td-space-3) var(--td-space-4);
  font-size: var(--td-font-sm);
  color: var(--td-text-primary);
  border-bottom: 1px solid var(--td-border-ghost);
}

.td-metrics__table tbody tr:last-child td {
  border-bottom: none;
}

.td-metrics__row--blocked td {
  color: var(--td-color-danger, #e53e3e);
}

/* Forecast */
.td-metrics__forecast {
  background: var(--td-surface-container);
  border: 1px solid var(--td-border-ghost);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-6);
}

.td-metrics__forecast-loading {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
}

.td-metrics__spinner--sm {
  width: 20px;
  height: 20px;
  border-width: 2px;
}

.td-metrics__forecast-error {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
  color: var(--td-color-danger, #e53e3e);
  font-size: var(--td-font-sm);
}

.td-metrics__forecast-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: var(--td-space-4);
  margin-bottom: var(--td-space-6);
}

.td-metrics__card-value--date {
  font-size: var(--td-font-lg);
}

.td-metrics__confidence {
  margin-bottom: var(--td-space-5);
}

.td-metrics__confidence-title {
  font-size: var(--td-font-sm);
  font-weight: 700;
  color: var(--td-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.1em;
  margin-bottom: var(--td-space-3);
}

.td-metrics__confidence-band {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  padding: var(--td-space-4);
  background: var(--td-surface-container-high);
  border-radius: var(--td-radius-md);
  border: 1px solid var(--td-border-ghost);
}

.td-metrics__confidence-row {
  display: grid;
  grid-template-columns: 100px 1fr auto;
  gap: var(--td-space-3);
  align-items: center;
  font-size: var(--td-font-sm);
}

.td-metrics__confidence-row--expected {
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-metrics__confidence-label {
  font-weight: 600;
  color: var(--td-text-secondary);
}

.td-metrics__confidence-label--optimistic {
  color: var(--td-color-success, #38a169);
}

.td-metrics__confidence-label--pessimistic {
  color: var(--td-color-danger, #e53e3e);
}

.td-metrics__confidence-date {
  color: var(--td-text-primary);
}

.td-metrics__confidence-rate {
  color: var(--td-text-tertiary);
  font-size: var(--td-font-xs);
}

.td-metrics__caveats {
  margin-bottom: var(--td-space-4);
  padding: var(--td-space-4);
  background: rgba(237, 137, 54, 0.08);
  border-left: 3px solid var(--td-color-ember);
  border-radius: 0 var(--td-radius-md) var(--td-radius-md) 0;
}

.td-metrics__caveats-title {
  font-size: var(--td-font-xs);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--td-color-ember);
  margin-bottom: var(--td-space-2);
}

.td-metrics__caveats-list {
  list-style: disc;
  padding-left: var(--td-space-5);
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
}

.td-metrics__caveats-list li {
  margin-bottom: var(--td-space-1);
}

.td-metrics__assumptions {
  font-size: var(--td-font-sm);
  color: var(--td-text-tertiary);
}

.td-metrics__assumptions-summary {
  cursor: pointer;
  font-weight: 600;
  color: var(--td-text-secondary);
  padding: var(--td-space-2) 0;
}

.td-metrics__assumptions-list {
  list-style: disc;
  padding-left: var(--td-space-5);
  margin-top: var(--td-space-2);
}

.td-metrics__assumptions-list li {
  margin-bottom: var(--td-space-1);
}

/* Responsive */
@media (max-width: 640px) {
  .td-metrics {
    padding: var(--td-space-4);
  }

  .td-metrics__summary {
    grid-template-columns: 1fr 1fr;
  }

  .td-metrics__wip-row {
    grid-template-columns: 80px 1fr 50px;
  }

  .td-metrics__filters {
    flex-direction: column;
  }
}
</style>
