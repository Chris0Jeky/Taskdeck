<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

/**
 * ReviewMiniCadence — week-cadence sparkline. The last bar (today)
 * is rendered in ember; preceding days in ink-deep at 0.65 opacity.
 *
 * There is intentionally no default for `days`: without real activity counts
 * the component renders nothing rather than inventing a plausible-looking
 * week. Callers pass real history or omit the prop.
 */
const props = defineProps<{
  /**
   * Real per-day activity counts, oldest → newest. Heights are normalised to
   * the maximum. Omit (or pass an empty array) when there is no history.
   */
  days?: number[]
}>()

const { t } = useI18n()

/** Real counts to render; empty when the caller supplied no history. */
const bars = computed<number[]>(() => (Array.isArray(props.days) ? props.days : []))

const max = computed(() => Math.max(1, ...bars.value))

/** Describes the actual number of rendered days, never an assumed week. */
const label = computed(() =>
  t('review.cadence.ariaLabel', { count: bars.value.length }, bars.value.length),
)
</script>

<template>
  <div
    v-if="bars.length > 0"
    class="paper-review-cadence"
    role="img"
    :aria-label="label"
    data-testid="paper-review-mini-cadence"
  >
    <div
      v-for="(d, i) in bars"
      :key="i"
      class="paper-review-cadence__col"
    >
      <div
        class="paper-review-cadence__bar"
        :class="{ 'paper-review-cadence__bar--today': i === bars.length - 1 }"
        :style="{ height: `${(d / max) * 100}%` }"
      />
    </div>
  </div>
</template>

<style scoped>
.paper-review-cadence {
  display: flex;
  align-items: flex-end;
  gap: 4px;
  height: 36px;
}
.paper-review-cadence__col {
  flex: 1;
  height: 100%;
  display: flex;
  align-items: flex-end;
}
.paper-review-cadence__bar {
  width: 100%;
  background: var(--ink-deep);
  opacity: 0.65;
}
.paper-review-cadence__bar--today {
  background: var(--ember);
  opacity: 1;
}
</style>
