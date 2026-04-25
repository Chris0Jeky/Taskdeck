<script setup lang="ts">
import { computed } from 'vue'

/**
 * PaperConfidenceDial — circular dial used to render LLM/router confidence.
 * 84×84px SVG; 28px radius circle stroked with the ember accent.  The
 * filled portion is driven by `stroke-dasharray` so the value updates
 * cleanly without animation; the serif italic value sits in the centre and
 * the mono "CONF" caption underneath.
 */
const props = withDefaults(
  defineProps<{
    /** 0..1 — values outside the range are clamped. */
    value: number
    /** Caption shown below the value.  Defaults to `CONF`. */
    caption?: string
    /** Optional second line under the caption (e.g. provider name). */
    subline?: string
  }>(),
  { caption: 'CONF' },
)

const RADIUS = 28
const CIRCUMFERENCE = 2 * Math.PI * RADIUS

const clamped = computed(() => {
  const v = props.value
  if (Number.isNaN(v)) return 0
  if (v < 0) return 0
  if (v > 1) return 1
  return v
})

const dasharray = computed(() => {
  const filled = clamped.value * CIRCUMFERENCE
  return `${filled.toFixed(2)} ${(CIRCUMFERENCE - filled).toFixed(2)}`
})

const valueLabel = computed(() => clamped.value.toFixed(2).replace(/^0/, ''))
</script>

<template>
  <div class="paper-confidence" :data-value="clamped">
    <svg viewBox="0 0 84 84" width="84" height="84" aria-hidden="true">
      <!-- Background hairline ring -->
      <circle
        cx="42"
        cy="42"
        :r="RADIUS"
        fill="none"
        stroke="var(--line)"
        stroke-width="1"
      />
      <!-- Ember progress arc, rotated so the start sits at 12 o'clock. -->
      <circle
        class="paper-confidence__arc"
        cx="42"
        cy="42"
        :r="RADIUS"
        fill="none"
        stroke="var(--ember)"
        stroke-width="1.5"
        stroke-linecap="butt"
        :stroke-dasharray="dasharray"
        :stroke-dashoffset="0"
        transform="rotate(-90 42 42)"
      />
      <text
        x="42"
        y="44"
        text-anchor="middle"
        dominant-baseline="middle"
        class="paper-confidence__value"
      >{{ valueLabel }}</text>
    </svg>
    <div class="paper-confidence__caption tk-eyebrow">{{ caption }}</div>
    <div v-if="subline" class="paper-confidence__sub tk-meta">{{ subline }}</div>
  </div>
</template>

<style scoped>
.paper-confidence {
  display: inline-flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
}
.paper-confidence__arc {
  transition: stroke-dasharray 240ms cubic-bezier(0.2, 0.65, 0.25, 1);
}
.paper-confidence__value {
  font-family: var(--serif);
  font-style: italic;
  font-weight: 400;
  font-size: 18px;
  fill: var(--ink-deep);
}
.paper-confidence__caption {
  margin-top: 4px;
}
.paper-confidence__sub {
  color: var(--mute);
}
@media (prefers-reduced-motion: reduce) {
  .paper-confidence__arc {
    transition: none;
  }
}
</style>
