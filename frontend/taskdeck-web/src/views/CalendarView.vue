<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { workspaceApi } from '../api/workspaceApi'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
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
  if (card.isOverdue) return 'paper-cal-card--overdue'
  if (card.isBlocked) return 'paper-cal-card--blocked'
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
  <div class="paper-calendar" role="region" aria-label="Calendar planning view">
    <header class="paper-calendar__panel paper-calendar__hero">
      <div class="paper-calendar__hero-copy">
        <span class="tk-eyebrow paper-calendar__eyebrow" aria-hidden="true">Planning</span>
        <h1 class="tk-h1 paper-calendar__title">Calendar</h1>
        <p class="tk-lede paper-calendar__subtitle">
          See due-date-backed work across all boards in a single view. Spot overdue items, plan ahead, and jump to any card's board context.
        </p>
      </div>
      <div class="paper-calendar__hero-actions">
        <PaperHLBtn
          class="paper-calendar__mode-btn"
          :class="{ 'paper-calendar__mode-btn--active': viewMode === 'calendar' }"
          @click="viewMode = 'calendar'"
        >
          Grid
        </PaperHLBtn>
        <PaperHLBtn
          class="paper-calendar__mode-btn"
          :class="{ 'paper-calendar__mode-btn--active': viewMode === 'timeline' }"
          @click="viewMode = 'timeline'"
        >
          Timeline
        </PaperHLBtn>
      </div>
    </header>

    <WorkspaceHelpCallout
      topic="calendar"
      title="What is the Calendar for?"
      description="The Calendar shows all cards with due dates across your boards. Use it to spot scheduling conflicts, track deadlines, and navigate to board context for any card."
    />

    <!-- Month navigation -->
    <div class="paper-calendar__panel paper-calendar__nav">
      <PaperHLBtn
        class="paper-calendar__nav-btn"
        variant="ghost"
        aria-label="Previous month"
        @click="navigateMonth(-1)"
      >
        &larr;
      </PaperHLBtn>
      <span class="paper-calendar__month-label">{{ monthLabel }}</span>
      <PaperHLBtn
        class="paper-calendar__nav-btn"
        variant="ghost"
        aria-label="Next month"
        @click="navigateMonth(1)"
      >
        &rarr;
      </PaperHLBtn>
      <PaperHLBtn class="paper-calendar__today-btn" variant="ghost" @click="goToToday">
        Today
      </PaperHLBtn>
      <span
        v-if="calendarData"
        class="paper-calendar__card-count"
        aria-live="polite"
      >
        {{ calendarData.totalCards }} card{{ calendarData.totalCards === 1 ? '' : 's' }} this month
      </span>
    </div>

    <!-- Loading state -->
    <div v-if="loading" class="paper-calendar__panel paper-calendar__placeholder" aria-live="polite">
      Loading calendar data...
    </div>

    <!-- Error state -->
    <div v-else-if="error" class="paper-calendar__alert" role="alert">
      {{ error }}
      <PaperHLBtn class="paper-calendar__retry" variant="ghost" @click="fetchCalendar">Retry</PaperHLBtn>
    </div>

    <!-- Empty state -->
    <div
      v-else-if="calendarData && calendarData.totalCards === 0"
      class="paper-calendar__panel paper-calendar__empty"
      role="status"
    >
      <p class="paper-calendar__empty-title">No due dates this month</p>
      <p class="paper-calendar__empty-desc">
        Cards with due dates will appear here. Set due dates on your board cards to see them in the calendar.
      </p>
    </div>

    <!-- Calendar grid view -->
    <template v-else-if="calendarData && viewMode === 'calendar'">
      <div class="paper-calendar__grid" role="grid" aria-label="Calendar grid">
        <div class="paper-calendar__weekdays" role="row">
          <div
            v-for="day in ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']"
            :key="day"
            class="paper-calendar__weekday"
            role="columnheader"
          >
            {{ day }}
          </div>
        </div>
        <div
          v-for="(week, wi) in calendarWeeks"
          :key="wi"
          class="paper-calendar__week"
          role="row"
        >
          <div
            v-for="day in week"
            :key="day.dateKey"
            class="paper-calendar__day"
            :class="{
              'paper-calendar__day--other-month': !day.isCurrentMonth,
              'paper-calendar__day--today': day.isToday,
              'paper-calendar__day--has-cards': (cardsByDate[day.dateKey]?.length ?? 0) > 0,
            }"
            role="gridcell"
            :aria-label="`${day.date.toLocaleDateString('en-US', { month: 'long', day: 'numeric', timeZone: 'UTC' })}, ${cardsByDate[day.dateKey]?.length ?? 0} cards`"
          >
            <span class="paper-calendar__day-number">{{ day.date.getUTCDate() }}</span>
            <div v-if="cardsByDate[day.dateKey]" class="paper-calendar__day-cards">
              <button
                v-for="card in cardsByDate[day.dateKey].slice(0, 3)"
                :key="card.cardId"
                class="paper-cal-card"
                :class="cardStatusClass(card)"
                :title="`${card.title} - ${card.boardName} / ${card.columnName} (${cardStatusLabel(card)})`"
                @click="openBoard(card.boardId)"
              >
                <span class="paper-cal-card__title">{{ card.title }}</span>
              </button>
              <span
                v-if="cardsByDate[day.dateKey].length > 3"
                class="paper-calendar__more"
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
      <div class="paper-calendar__timeline" role="list" aria-label="Timeline">
        <div
          v-for="group in timelineGroups"
          :key="group.dateKey"
          class="paper-timeline-group"
        >
          <div class="paper-timeline-group__header">
            <span class="paper-timeline-group__date">{{ group.dateLabel }}</span>
            <span class="paper-timeline-group__count">{{ group.cards.length }} card{{ group.cards.length === 1 ? '' : 's' }}</span>
          </div>
          <ul class="paper-timeline-group__cards">
            <li
              v-for="card in group.cards"
              :key="card.cardId"
              class="paper-timeline-card-wrapper"
            >
              <button
                class="paper-timeline-card"
                :class="cardStatusClass(card)"
                @click="openBoard(card.boardId)"
              >
              <div class="paper-timeline-card__header">
                <span class="paper-timeline-card__title">{{ card.title }}</span>
                <span
                  class="paper-timeline-card__status"
                  :class="{
                    'paper-timeline-card__status--overdue': card.isOverdue,
                    'paper-timeline-card__status--blocked': card.isBlocked,
                  }"
                >
                  {{ cardStatusLabel(card) }}
                </span>
              </div>
              <div class="paper-timeline-card__meta">
                <span class="paper-timeline-card__board">{{ card.boardName }}</span>
                <span class="paper-timeline-card__separator" aria-hidden="true">/</span>
                <span class="paper-timeline-card__column">{{ card.columnName }}</span>
                <span class="paper-timeline-card__due">Due {{ formatDueDate(card.dueDate) }}</span>
              </div>
              <p
                v-if="card.blockReason"
                class="paper-timeline-card__block-reason"
              >
                Blocked: {{ card.blockReason }}
              </p>
            </button>
            </li>
          </ul>
        </div>

        <div v-if="timelineGroups.length === 0" class="paper-calendar__panel paper-calendar__empty" role="status">
          <p class="paper-calendar__empty-title">No due dates this month</p>
          <p class="paper-calendar__empty-desc">
            Cards with due dates will appear here.
          </p>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — CalendarView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   The tokens live under `.paper` / `.paper-night` (the canonical shell), so the
   var() fallbacks keep this surface legible if it is ever rendered outside the
   Paper shell (Legacy/Obsidian "off" mode).

   Status colors map onto the Paper semantic family: on-track -> --applied,
   overdue -> --overdue, blocked -> --ember. */

.paper-calendar {
  display: flex;
  flex-direction: column;
  gap: var(--s-5, 20px);
  padding: var(--s-8, 32px);
  max-width: 1200px;
  margin: 0 auto;
  font-family: var(--sans, system-ui, sans-serif);
  /* See MetricsView: paint the Paper substrate wherever --ink is set, so
     Legacy ("off") mode does not render near-black ink on the Obsidian
     --td-surface-base. No-op under .paper/.paper-night. */
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
}

/* ── Panels ── */

.paper-calendar__panel {
  padding: var(--s-4, 16px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

/* ── Hero ── */

.paper-calendar__hero {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: var(--s-5, 20px);
}

.paper-calendar__hero-copy {
  flex: 1;
  min-width: 280px;
}

.paper-calendar__eyebrow {
  color: var(--ember, #a8421f);
}

.paper-calendar__title {
  margin: var(--s-1, 4px) 0 0;
  font-size: var(--t-h2, 32px);
}

.paper-calendar__subtitle {
  margin: var(--s-2, 8px) 0 0;
  color: var(--ink-2, #3a352d);
}

.paper-calendar__hero-actions {
  display: flex;
  gap: var(--s-3, 12px);
  flex-shrink: 0;
}

/* Compound selector so this beats the global `.paper .pbtn` rule (0,2,0)
   regardless of stylesheet injection order. */
.paper-calendar__mode-btn.paper-calendar__mode-btn--active {
  background: var(--ember-tint, #f0d9c8);
  border-color: var(--ember, #a8421f);
  color: var(--ember-ink, #6e2810);
}

/* ── Month navigation ── */

.paper-calendar__nav {
  display: flex;
  align-items: center;
  gap: var(--s-4, 16px);
  padding: var(--s-4, 16px) var(--s-5, 20px);
}

.paper-calendar__month-label {
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-lg, 18px);
  font-weight: 500;
  color: var(--ink-deep, #0a0908);
  min-width: 180px;
  text-align: center;
}

.paper-calendar__today-btn {
  margin-left: auto;
}

.paper-calendar__card-count {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  letter-spacing: 0.04em;
  color: var(--mute, #6c6557);
}

/* ── Loading / error / empty states ── */

.paper-calendar__placeholder {
  padding: var(--s-10, 40px);
  text-align: center;
  color: var(--mute, #6c6557);
}

.paper-calendar__alert {
  display: flex;
  align-items: center;
  gap: var(--s-3, 12px);
  padding: var(--s-4, 16px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--overdue, #8c4a26);
  background: var(--overdue-tint, #ecd9c4);
  color: var(--ember-ink, #6e2810);
  font-size: var(--t-md, 13.5px);
}

.paper-calendar__empty {
  padding: var(--s-10, 40px);
  text-align: center;
}

.paper-calendar__empty-title {
  margin: 0 0 var(--s-3, 12px);
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-lg, 18px);
  font-weight: 500;
  color: var(--ink-deep, #0a0908);
}

.paper-calendar__empty-desc {
  margin: 0;
  color: var(--ink-2, #3a352d);
  font-size: var(--t-md, 13.5px);
}

/* ── Calendar grid ── */

.paper-calendar__grid {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  overflow: hidden;
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

.paper-calendar__weekdays {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  background: var(--paper-2, #ebe5d8);
}

.paper-calendar__weekday {
  padding: var(--s-3, 12px) var(--s-2, 8px);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.22em;
  color: var(--mute, #6c6557);
  text-align: center;
}

.paper-calendar__week {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  border-top: 1px solid var(--line, #d8d0bf);
}

.paper-calendar__day {
  min-height: 100px;
  padding: var(--s-2, 8px);
  border-right: 1px solid var(--line-soft, #e3dcc9);
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
}

.paper-calendar__day:last-child {
  border-right: none;
}

.paper-calendar__day--other-month {
  opacity: 0.35;
}

.paper-calendar__day--today {
  background: var(--ember-tint, #f0d9c8);
}

.paper-calendar__day--today .paper-calendar__day-number {
  color: var(--ember, #a8421f);
  font-weight: 700;
}

.paper-calendar__day-number {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-sm, 12px);
  font-weight: 500;
  color: var(--ink-2, #3a352d);
  padding: var(--s-1, 4px);
}

.paper-calendar__day-cards {
  display: flex;
  flex-direction: column;
  gap: 2px;
  overflow: hidden;
}

.paper-cal-card {
  display: block;
  width: 100%;
  padding: var(--s-1, 4px) var(--s-2, 8px);
  border-radius: var(--r-1, 2px);
  background: var(--paper-2, #ebe5d8);
  border: none;
  border-left: 3px solid var(--applied, #4a6b3f);
  cursor: pointer;
  text-align: left;
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease);
  font-family: inherit;
}

.paper-cal-card:hover {
  background: var(--paper, #f3eee5);
}

.paper-cal-card:focus-visible {
  outline: none;
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

.paper-cal-card--overdue {
  border-left-color: var(--overdue, #8c4a26);
}

.paper-cal-card--blocked {
  border-left-color: var(--ember, #a8421f);
}

.paper-cal-card__title {
  font-size: var(--t-xs, 10.5px);
  color: var(--ink, #1a1814);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: block;
}

.paper-calendar__more {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #6c6557);
  padding: 0 var(--s-2, 8px);
}

/* ── Timeline view ── */

.paper-calendar__timeline {
  display: flex;
  flex-direction: column;
  gap: var(--s-5, 20px);
}

.paper-timeline-group {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
}

.paper-timeline-group__header {
  display: flex;
  align-items: center;
  gap: var(--s-4, 16px);
  padding: var(--s-3, 12px) 0;
  border-bottom: 1px solid var(--line, #d8d0bf);
}

.paper-timeline-group__date {
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-bd, 15px);
  font-weight: 500;
  color: var(--ink-deep, #0a0908);
}

.paper-timeline-group__count {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  letter-spacing: 0.04em;
  color: var(--mute, #6c6557);
}

.paper-timeline-group__cards {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
  list-style: none;
  padding: 0;
  margin: 0;
}

.paper-timeline-card-wrapper {
  display: contents;
}

.paper-timeline-card {
  display: block;
  width: 100%;
  padding: var(--s-4, 16px) var(--s-5, 20px);
  border: 1px solid var(--line, #d8d0bf);
  border-left: 4px solid var(--applied, #4a6b3f);
  border-radius: var(--r-3, 6px);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  cursor: pointer;
  text-align: left;
  font-family: inherit;
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-timeline-card:hover {
  background: var(--paper-2, #ebe5d8);
}

.paper-timeline-card:focus-visible {
  outline: none;
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

.paper-timeline-card.paper-cal-card--overdue {
  border-left-color: var(--overdue, #8c4a26);
}

.paper-timeline-card.paper-cal-card--blocked {
  border-left-color: var(--ember, #a8421f);
}

.paper-timeline-card__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--s-4, 16px);
}

.paper-timeline-card__title {
  font-family: var(--serif, Georgia, serif);
  font-size: var(--t-bd, 15px);
  font-weight: 500;
  color: var(--ink-deep, #0a0908);
}

.paper-timeline-card__status {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: 9.5px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.22em;
  padding: 3px 8px 2px;
  border: 1px solid currentColor;
  border-radius: var(--r-1, 2px);
  background: var(--applied-tint, #d8e0ce);
  color: var(--applied, #4a6b3f);
  line-height: 1;
  white-space: nowrap;
}

.paper-timeline-card__status--overdue {
  background: var(--overdue-tint, #ecd9c4);
  color: var(--overdue, #8c4a26);
}

.paper-timeline-card__status--blocked {
  background: var(--ember-tint, #f0d9c8);
  color: var(--ember, #a8421f);
}

.paper-timeline-card__meta {
  display: flex;
  align-items: center;
  gap: var(--s-2, 8px);
  margin-top: var(--s-2, 8px);
  font-size: var(--t-sm, 12px);
  color: var(--ink-2, #3a352d);
}

.paper-timeline-card__separator {
  color: var(--whisper, #c2bba8);
}

.paper-timeline-card__due {
  margin-left: auto;
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  letter-spacing: 0.04em;
  color: var(--mute, #6c6557);
}

.paper-timeline-card__block-reason {
  margin: var(--s-2, 8px) 0 0;
  font-size: var(--t-xs, 10.5px);
  color: var(--ember, #a8421f);
}

/* ── Responsive ── */

@media (max-width: 768px) {
  .paper-calendar {
    padding: var(--s-4, 16px);
  }

  .paper-calendar__day {
    min-height: 60px;
    padding: var(--s-1, 4px);
  }

  .paper-cal-card__title {
    font-size: 9px;
  }

  .paper-calendar__day-cards {
    max-height: 40px;
    overflow: hidden;
  }
}
</style>
