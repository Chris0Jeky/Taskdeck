<script setup lang="ts">
import { computed } from 'vue'

/**
 * ReviewMiniCadence — 7-bar week-cadence sparkline. The last bar (today)
 * is rendered in ember; preceding days in ink-deep at 0.65 opacity.
 */
const props = withDefaults(
  defineProps<{
    /** 7 numbers, oldest → newest. Heights are normalised to the maximum. */
    days?: number[]
  }>(),
  {
    days: () => [4, 3, 5, 2, 4, 1, 3],
  },
)

const max = computed(() => Math.max(1, ...props.days))
</script>

<template>
  <div class="paper-review-cadence" role="img" aria-label="Activity for the last 7 days">
    <div
      v-for="(d, i) in days"
      :key="i"
      class="paper-review-cadence__col"
    >
      <div
        class="paper-review-cadence__bar"
        :class="{ 'paper-review-cadence__bar--today': i === days.length - 1 }"
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
