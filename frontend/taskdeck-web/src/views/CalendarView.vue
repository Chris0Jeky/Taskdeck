<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { workspaceApi } from '../api/workspaceApi'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import type { CalendarCard, CalendarData } from '../types/workspace'

const router = useRouter()

const loading = ref(false)
const error = ref<string | null>(null)
const calendarData = ref<CalendarData | null>(null)

/** Current view month (first day of month in UTC). */
const viewDate = ref(startOfMonth(new Date()))

/** Active view mode: 'calendar' for monthly grid, 'timeline' for linear list. */
const viewMode = ref<'calendar' | 'timeline'>('calendar')

function startOfMonth(date: Date): Date {
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1))
}

/** Returns the first day of the next month (exclusive upper bound for date range queries). */
function startOfNextMonth(date: Date): Date {
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 1))
}

const monthLabel = computed(() => {
  const d = viewDate.value
  return d.toLocaleDateString('en-US', { year: 'numeric', month: 'long', timeZone: 'UTC' })
})

function navigateMonth(delta: number) {
  const d = viewDate.value
  viewDate.value = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + delta, 1))
}

function goToToday() {
  viewDate.value = startOfMonth(new Date())
}

async function fetchCalendar() {
  loading.value = true
  error.value = null

  try {
    const from = viewDate.value.toISOString()
    const to = startOfNextMonth(viewDate.value).toISOString()
    calendarData.value = await workspaceApi.getCalendar(from, to)
  } catch (e: unknown) {
    calendarData.value = null
    const msg = e instanceof Error ? e.message : 'Failed to load calendar data'
    error.value = msg
  } finally {
    loading.value = false
  }
}

/** Group cards by their due date (date-only string key). */
const cardsByDate = computed<Record<string, CalendarCard[]>>(() => {
  if (!calendarData.value) return {}

  const groups: Record<string, CalendarCard[]> = {}
  for (const card of calendarData.value.cards) {
    const dateKey = card.dueDate.slice(0, 10) // YYYY-MM-DD
    if (!groups[dateKey]) {
      groups[dateKey] = []
    }
    groups[dateKey].push(card)
  }
  return groups
})

/** Build the calendar grid: weeks containing day cells. */
const calendarWeeks = computed(() => {
  const year = viewDate.value.getUTCFullYear()
  const month = viewDate.value.getUTCMonth()
  const firstDay = new Date(Date.UTC(year, month, 1))
  const lastDay = new Date(Date.UTC(year, month + 1, 0))

  // Start from the Sunday before (or the first day if it is Sunday)
  const startDow = firstDay.getUTCDay()
  const gridStart = new Date(Date.UTC(year, month, 1 - startDow))

  const weeks: { date: Date; dateKey: string; isCurrentMonth: boolean; isToday: boolean }[][] = []
  const today = new Date()
  const todayKey = `${today.getUTCFullYear()}-${String(today.getUTCMonth() + 1).padStart(2, '0')}-${String(today.getUTCDate()).padStart(2, '0')}`

  let cursor = new Date(gridStart)
  while (cursor <= lastDay || weeks.length === 0 || weeks[weeks.length - 1].length < 7) {
    if (!weeks.length || weeks[weeks.length - 1].length === 7) {
      weeks.push([])
    }

    const dateKey = `${cursor.getUTCFullYear()}-${String(cursor.getUTCMonth() + 1).padStart(2, '0')}-${String(cursor.getUTCDate()).padStart(2, '0')}`
    weeks[weeks.length - 1].push({
      date: new Date(cursor),
      dateKey,
      isCurrentMonth: cursor.getUTCMonth() === month,
      isToday: dateKey === todayKey,
    })

    cursor = new Date(Date.UTC(cursor.getUTCFullYear(), cursor.getUTCMonth(), cursor.getUTCDate() + 1))

    // Stop after 6 weeks max
    if (weeks.length >= 6 && weeks[weeks.length - 1].length === 7) break
  }

  return weeks
})

/** Timeline: sorted list of cards for the current month. */
const timelineCards = computed(() => {
  if (!calendarData.value) return []
  return [...calendarData.value.cards].sort(
    (a, b) => new Date(a.dueDate).getTime() - new Date(b.dueDate).getTime(),
  )
})

/** Timeline: group by date for section headers. */
const timelineGroups = computed(() => {
  const groups: { dateKey: string; dateLabel: string; cards: CalendarCard[] }[] = []
  let currentKey = ''

  for (const card of timelineCards.value) {
    const dateKey = card.dueDate.slice(0, 10)
    if (dateKey !== currentKey) {
      currentKey = dateKey
      groups.push({
        dateKey,
        dateLabel: new Date(dateKey + 'T00:00:00Z').toLocaleDateString('en-US', {
          weekday: 'short',
          month: 'short',
          day: 'numeric',
          timeZone: 'UTC',
        }),
        cards: [],
      })
    }
    groups[groups.length - 1].cards.push(card)
  }

  return groups
})

function formatDueDate(value: string): string {
  return new Date(value).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  })
}

function openBoard(boardId: string) {
  void router.push(`/workspace/boards/${boardId}`)
}

function cardStatusClass(card: CalendarCard): string {
  if (card.isOverdue) return 'td-cal-card--overdue'
  if (card.isBlocked) return 'td-cal-card--blocked'
  return ''
}

function cardStatusLabel(card: CalendarCard): string {
  if (card.isOverdue) return 'Overdue'
  if (card.isBlocked) return 'Blocked'
  return 'On track'
}

// Fetch on mount and when viewDate changes
onMounted(fetchCalendar)
watch(viewDate, fetchCalendar)
</script>

<template>
  <div class="td-calendar" role="region" aria-label="Calendar planning view">
    <header class="td-calendar__hero td-panel">
      <div class="td-calendar__hero-copy">
        <span class="td-calendar__eyebrow" aria-hidden="true">Planning</span>
        <h1 class="td-page-title">Calendar</h1>
        <p class="td-calendar__subtitle">
          See due-date-backed work across all boards in a single view. Spot overdue items, plan ahead, and jump to any card's board context.
        </p>
      </div>
      <div class="td-calendar__hero-actions">
        <button
          class="td-btn td-btn--secondary"
          :class="{ 'td-btn--active': viewMode === 'calendar' }"
          @click="viewMode = 'calendar'"
        >
          Grid
        </button>
        <button
          class="td-btn td-btn--secondary"
          :class="{ 'td-btn--active': viewMode === 'timeline' }"
          @click="viewMode = 'timeline'"
        >
          Timeline
        </button>
      </div>
    </header>

    <WorkspaceHelpCallout
      topic="calendar"
      title="What is the Calendar for?"
      description="The Calendar shows all cards with due dates across your boards. Use it to spot scheduling conflicts, track deadlines, and navigate to board context for any card."
    />

    <!-- Month navigation -->
    <div class="td-calendar__nav td-panel">
      <button
        class="td-btn td-btn--ghost"
        aria-label="Previous month"
        @click="navigateMonth(-1)"
      >
        &larr;
      </button>
      <span class="td-calendar__month-label">{{ monthLabel }}</span>
      <button
        class="td-btn td-btn--ghost"
        aria-label="Next month"
        @click="navigateMonth(1)"
      >
        &rarr;
      </button>
      <button class="td-btn td-btn--ghost td-calendar__today-btn" @click="goToToday">
        Today
      </button>
      <span
        v-if="calendarData"
        class="td-calendar__card-count"
        aria-live="polite"
      >
        {{ calendarData.totalCards }} card{{ calendarData.totalCards === 1 ? '' : 's' }} this month
      </span>
    </div>

    <!-- Loading state -->
    <div v-if="loading" class="td-panel td-calendar__placeholder" aria-live="polite">
      Loading calendar data...
    </div>

    <!-- Error state -->
    <div v-else-if="error" class="td-alert td-alert--error" role="alert">
      {{ error }}
      <button class="td-btn td-btn--ghost td-btn--sm" @click="fetchCalendar">Retry</button>
    </div>

    <!-- Empty state -->
    <div
      v-else-if="calendarData && calendarData.totalCards === 0"
      class="td-panel td-calendar__empty"
      role="status"
    >
      <p class="td-calendar__empty-title">No due dates this month</p>
      <p class="td-calendar__empty-desc">
        Cards with due dates will appear here. Set due dates on your board cards to see them in the calendar.
      </p>
    </div>

    <!-- Calendar grid view -->
    <template v-else-if="calendarData && viewMode === 'calendar'">
      <div class="td-calendar__grid" role="grid" aria-label="Calendar grid">
        <div class="td-calendar__weekdays" role="row">
          <div
            v-for="day in ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']"
            :key="day"
            class="td-calendar__weekday"
            role="columnheader"
          >
            {{ day }}
          </div>
        </div>
        <div
          v-for="(week, wi) in calendarWeeks"
          :key="wi"
          class="td-calendar__week"
          role="row"
        >
          <div
            v-for="day in week"
            :key="day.dateKey"
            class="td-calendar__day"
            :class="{
              'td-calendar__day--other-month': !day.isCurrentMonth,
              'td-calendar__day--today': day.isToday,
              'td-calendar__day--has-cards': (cardsByDate[day.dateKey]?.length ?? 0) > 0,
            }"
            role="gridcell"
            :aria-label="`${day.date.toLocaleDateString('en-US', { month: 'long', day: 'numeric', timeZone: 'UTC' })}, ${cardsByDate[day.dateKey]?.length ?? 0} cards`"
          >
            <span class="td-calendar__day-number">{{ day.date.getUTCDate() }}</span>
            <div v-if="cardsByDate[day.dateKey]" class="td-calendar__day-cards">
              <button
                v-for="card in cardsByDate[day.dateKey].slice(0, 3)"
                :key="card.cardId"
                class="td-cal-card"
                :class="cardStatusClass(card)"
                :title="`${card.title} - ${card.boardName} / ${card.columnName} (${cardStatusLabel(card)})`"
                @click="openBoard(card.boardId)"
              >
                <span class="td-cal-card__title">{{ card.title }}</span>
              </button>
              <span
                v-if="cardsByDate[day.dateKey].length > 3"
                class="td-calendar__more"
              >
                +{{ cardsByDate[day.dateKey].length - 3 }} more
              </span>
            </div>
          </div>
        </div>
      </div>
    </template>

    <!-- Timeline view -->
    <template v-else-if="calendarData && viewMode === 'timeline'">
      <div class="td-calendar__timeline" role="list" aria-label="Timeline">
        <div
          v-for="group in timelineGroups"
          :key="group.dateKey"
          class="td-timeline-group"
        >
          <div class="td-timeline-group__header">
            <span class="td-timeline-group__date">{{ group.dateLabel }}</span>
            <span class="td-timeline-group__count">{{ group.cards.length }} card{{ group.cards.length === 1 ? '' : 's' }}</span>
          </div>
          <ul class="td-timeline-group__cards">
            <li
              v-for="card in group.cards"
              :key="card.cardId"
              class="td-timeline-card-wrapper"
            >
              <button
                class="td-timeline-card td-panel"
                :class="cardStatusClass(card)"
                @click="openBoard(card.boardId)"
              >
              <div class="td-timeline-card__header">
                <span class="td-timeline-card__title">{{ card.title }}</span>
                <span
                  class="td-timeline-card__status"
                  :class="{
                    'td-timeline-card__status--overdue': card.isOverdue,
                    'td-timeline-card__status--blocked': card.isBlocked,
                  }"
                >
                  {{ cardStatusLabel(card) }}
                </span>
              </div>
              <div class="td-timeline-card__meta">
                <span class="td-timeline-card__board">{{ card.boardName }}</span>
                <span class="td-timeline-card__separator" aria-hidden="true">/</span>
                <span class="td-timeline-card__column">{{ card.columnName }}</span>
                <span class="td-timeline-card__due">Due {{ formatDueDate(card.dueDate) }}</span>
              </div>
              <p
                v-if="card.blockReason"
                class="td-timeline-card__block-reason"
              >
                Blocked: {{ card.blockReason }}
              </p>
            </button>
            </li>
          </ul>
        </div>

        <div v-if="timelineGroups.length === 0" class="td-panel td-calendar__empty" role="status">
          <p class="td-calendar__empty-title">No due dates this month</p>
          <p class="td-calendar__empty-desc">
            Cards with due dates will appear here.
          </p>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.td-calendar {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-5);
  padding: var(--td-space-8);
  max-width: 1200px;
  margin: 0 auto;
}

.td-calendar__hero {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: var(--td-space-5);
}

.td-calendar__hero-copy {
  flex: 1;
  min-width: 280px;
}

.td-calendar__eyebrow {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.2em;
  text-transform: uppercase;
  color: var(--td-color-ember);
}

.td-calendar__subtitle {
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
  margin-top: var(--td-space-2);
}

.td-calendar__hero-actions {
  display: flex;
  gap: var(--td-space-3);
}

.td-btn--active {
  background: var(--td-color-ember-dim);
  color: var(--td-color-ember);
  border-color: var(--td-color-ember);
}

/* Month navigation */
.td-calendar__nav {
  display: flex;
  align-items: center;
  gap: var(--td-space-4);
  padding: var(--td-space-4) var(--td-space-5);
}

.td-calendar__month-label {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-lg);
  font-weight: 700;
  color: var(--td-text-primary);
  min-width: 180px;
  text-align: center;
}

.td-calendar__today-btn {
  margin-left: auto;
}

.td-calendar__card-count {
  font-size: var(--td-font-sm);
  color: var(--td-text-tertiary);
}

/* Loading & empty states */
.td-calendar__placeholder {
  padding: var(--td-space-10);
  text-align: center;
  color: var(--td-text-tertiary);
}

.td-calendar__empty {
  padding: var(--td-space-10);
  text-align: center;
}

.td-calendar__empty-title {
  font-size: var(--td-font-lg);
  font-weight: 700;
  color: var(--td-text-primary);
  margin-bottom: var(--td-space-3);
}

.td-calendar__empty-desc {
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
}

/* Calendar grid */
.td-calendar__grid {
  background: var(--td-surface-container);
  border-radius: var(--td-radius-lg);
  overflow: hidden;
  box-shadow: var(--td-shadow-sm);
}

.td-calendar__weekdays {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  background: var(--td-surface-container-high);
}

.td-calendar__weekday {
  padding: var(--td-space-3) var(--td-space-2);
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--td-text-tertiary);
  text-align: center;
}

.td-calendar__week {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  border-top: 1px solid var(--td-border-ghost);
}

.td-calendar__day {
  min-height: 100px;
  padding: var(--td-space-2);
  border-right: 1px solid var(--td-border-ghost);
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-calendar__day:last-child {
  border-right: none;
}

.td-calendar__day--other-month {
  opacity: 0.35;
}

.td-calendar__day--today {
  background: var(--td-color-ember-dim);
}

.td-calendar__day--today .td-calendar__day-number {
  color: var(--td-color-ember);
  font-weight: 700;
}

.td-calendar__day-number {
  font-size: var(--td-font-sm);
  font-weight: 600;
  color: var(--td-text-secondary);
  padding: var(--td-space-1);
}

.td-calendar__day-cards {
  display: flex;
  flex-direction: column;
  gap: 2px;
  overflow: hidden;
}

.td-cal-card {
  display: block;
  width: 100%;
  padding: var(--td-space-1) var(--td-space-2);
  border-radius: var(--td-radius-sm);
  background: var(--td-surface-container-high);
  border: none;
  border-left: 3px solid var(--td-color-success);
  cursor: pointer;
  text-align: left;
  transition: background var(--td-transition-fast);
  font-family: inherit;
}

.td-cal-card:hover {
  background: var(--td-surface-bright);
}

.td-cal-card:focus-visible {
  box-shadow: var(--td-focus-ring);
  outline: none;
}

.td-cal-card--overdue {
  border-left-color: var(--td-color-error);
}

.td-cal-card--blocked {
  border-left-color: var(--td-color-warning);
}

.td-cal-card__title {
  font-size: var(--td-font-xs);
  color: var(--td-text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: block;
}

.td-calendar__more {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  padding: 0 var(--td-space-2);
}

/* Timeline view */
.td-calendar__timeline {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-5);
}

.td-timeline-group {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-timeline-group__header {
  display: flex;
  align-items: center;
  gap: var(--td-space-4);
  padding: var(--td-space-3) 0;
  border-bottom: 1px solid var(--td-border-ghost);
}

.td-timeline-group__date {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-base);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-timeline-group__count {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
}

.td-timeline-group__cards {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
  list-style: none;
  padding: 0;
  margin: 0;
}

.td-timeline-card-wrapper {
  display: contents;
}

.td-timeline-card {
  display: block;
  width: 100%;
  padding: var(--td-space-4) var(--td-space-5);
  border: none;
  border-left: 4px solid var(--td-color-success);
  cursor: pointer;
  text-align: left;
  font-family: inherit;
  transition: background var(--td-transition-fast);
}

.td-timeline-card:hover {
  background: var(--td-surface-bright);
}

.td-timeline-card:focus-visible {
  box-shadow: var(--td-focus-ring);
  outline: none;
}

.td-timeline-card.td-cal-card--overdue {
  border-left-color: var(--td-color-error);
}

.td-timeline-card.td-cal-card--blocked {
  border-left-color: var(--td-color-warning);
}

.td-timeline-card__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--td-space-4);
}

.td-timeline-card__title {
  font-size: var(--td-font-base);
  font-weight: 600;
  color: var(--td-text-primary);
}

.td-timeline-card__status {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  padding: var(--td-space-1) var(--td-space-3);
  border-radius: var(--td-radius-sm);
  background: var(--td-color-success-light);
  color: var(--td-color-success);
}

.td-timeline-card__status--overdue {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
}

.td-timeline-card__status--blocked {
  background: var(--td-color-warning-light);
  color: var(--td-color-warning);
}

.td-timeline-card__meta {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  margin-top: var(--td-space-2);
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
}

.td-timeline-card__separator {
  color: var(--td-text-tertiary);
}

.td-timeline-card__due {
  margin-left: auto;
  color: var(--td-text-tertiary);
}

.td-timeline-card__block-reason {
  margin-top: var(--td-space-2);
  font-size: var(--td-font-xs);
  color: var(--td-color-warning);
}

/* Responsive */
@media (max-width: 768px) {
  .td-calendar {
    padding: var(--td-space-4);
  }

  .td-calendar__day {
    min-height: 60px;
    padding: var(--td-space-1);
  }

  .td-cal-card__title {
    font-size: 0.5625rem;
  }

  .td-calendar__day-cards {
    max-height: 40px;
    overflow: hidden;
  }
}
</style>
