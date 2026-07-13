<script setup lang="ts">
import { computed } from 'vue'
import type { DossierStatCard } from '../../../composables/useTodayDossier'

/**
 * TodayStats — live Today-summary stat cards. Numeric values run through
 * `Intl.NumberFormat` so 1_000+ render with the requested locale.
 */
const props = defineProps<{
  stats: DossierStatCard[]
  /** Locale override for number formatting tests. */
  locale?: string
}>()

const formatter = computed(() => new Intl.NumberFormat(props.locale ?? 'en-US'))

function display(stat: DossierStatCard): string {
  if (stat.numeric && typeof stat.value === 'number') {
    return formatter.value.format(stat.value)
  }
  return String(stat.value)
}

function toneColor(tone: DossierStatCard['tone']): string {
  switch (tone) {
    case 'ember':
      return 'var(--ember)'
    case 'applied':
      return 'var(--applied)'
    case 'overdue':
      return 'var(--overdue)'
    case 'ink':
    default:
      return 'var(--ink-deep)'
  }
}
</script>

<template>
  <section class="today-stats" data-section="stats">
    <article
      v-for="stat in stats"
      :key="stat.id"
      class="card today-stat"
      :data-stat-id="stat.id"
      :data-tone="stat.tone"
    >
      <span class="today-stat__accent" :style="{ background: toneColor(stat.tone) }" aria-hidden="true" />
      <div class="tk-eyebrow today-stat__label">{{ stat.label }}</div>
      <div class="today-stat__value" data-testid="stat-value">{{ display(stat) }}</div>
      <div class="tk-meta today-stat__sub">{{ stat.sub }}</div>
    </article>
  </section>
</template>

<style scoped>
.today-stats {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 16px;
  padding: 28px 56px 12px;
}
.today-stat {
  padding: 16px;
  position: relative;
  overflow: hidden;
}
.today-stat__accent {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  height: 2px;
  opacity: 0.8;
}
.today-stat__value {
  font-family: var(--serif);
  font-size: 38px;
  font-weight: 400;
  font-style: italic;
  color: var(--ink-deep);
  line-height: 1;
  margin: 8px 0 4px;
}
.today-stat__sub {
  font-size: 10.5px;
}

@media (max-width: 1100px) {
  .today-stats {
    grid-template-columns: repeat(2, 1fr);
    padding: 24px 24px 12px;
  }
}
</style>
