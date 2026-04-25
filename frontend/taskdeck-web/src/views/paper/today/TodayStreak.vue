<script setup lang="ts">
import { computed } from 'vue'
import type { DossierStreak } from '../../../composables/useTodayDossier'

/**
 * TodayStreak — 90-day grid (30 cols × 3 rows) of ember-intensity cells.
 * Buckets are deterministic: 0 → line, 1 → paper-card, 2 → ember-bloom,
 * 3 → ember-tint, 4 → ember.  The "today" cell is highlighted with an
 * ember outline.
 */
const props = defineProps<{
  streak: DossierStreak
}>()

const cells = computed(() => props.streak.cells)

function bucketBg(value: number): string {
  switch (value) {
    case 0:
      return 'var(--line)'
    case 1:
      return 'var(--paper-card)'
    case 2:
      return 'var(--ember-bloom)'
    case 3:
      return 'var(--ember-tint)'
    default:
      return 'var(--ember)'
  }
}

function isToday(index: number): boolean {
  return index === props.streak.todayIndex
}
</script>

<template>
  <div class="today-streak" data-section="streak">
    <div class="today-streak__grid">
      <div
        v-for="(value, i) in cells"
        :key="i"
        class="today-streak__cell"
        :class="{ 'today-streak__cell--today': isToday(i) }"
        :data-bucket="value"
        :data-today="isToday(i) ? 'true' : null"
        :style="{ background: bucketBg(value) }"
      />
    </div>
    <p class="tk-body today-streak__caption">
      <b>{{ streak.totalDays }} days.</b> Your longest this year was {{ streak.longestThisYear }}.
    </p>
  </div>
</template>

<style scoped>
.today-streak__grid {
  display: grid;
  grid-template-columns: repeat(30, 1fr);
  gap: 2px;
  padding: 8px 0;
}
.today-streak__cell {
  aspect-ratio: 1;
}
.today-streak__cell--today {
  outline: 1px solid var(--ember);
  outline-offset: -1px;
}
.today-streak__caption {
  margin: 10px 0 0;
  font-size: 12.5px;
  color: var(--ink-2);
}
.today-streak__caption b {
  color: var(--ink-deep);
}
</style>
