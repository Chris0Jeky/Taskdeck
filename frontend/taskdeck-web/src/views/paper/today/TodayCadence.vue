<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { DossierCadence } from '../../../composables/useTodayDossier'

/**
 * TodayCadence — 24-hour activity strip rendered as 24 SVG bars (one per
 * hour).  The peak hour glows ember and pulses gently — but only when
 * the user hasn't asked for reduced motion.  The pulse lives in CSS,
 * keyed off a `data-pulse="off"` attribute we set when `prefers-reduced-
 * motion: reduce` matches.
 */
const props = defineProps<{
  cadence: DossierCadence
}>()

const HOUR_LABELS = ['00', '', '', '', '', '', '06', '', '', '', '', '', '12', '', '', '', '', '', '18', '', '', '', '', '23']

const reducedMotion = ref(false)
let mq: MediaQueryList | null = null
let mqListener: ((e: MediaQueryListEvent) => void) | null = null

onMounted(() => {
  if (typeof window === 'undefined' || !window.matchMedia) return
  mq = window.matchMedia('(prefers-reduced-motion: reduce)')
  reducedMotion.value = mq.matches
  mqListener = (e) => {
    reducedMotion.value = e.matches
  }
  mq.addEventListener?.('change', mqListener)
})

onBeforeUnmount(() => {
  if (mq && mqListener) {
    mq.removeEventListener?.('change', mqListener)
  }
  mq = null
  mqListener = null
})

const max = computed(() => Math.max(1, ...props.cadence.weights))

function barHeightPercent(weight: number): number {
  if (weight === 0) return 4
  return (weight / max.value) * 100
}
</script>

<template>
  <div class="today-cadence" :data-pulse="reducedMotion ? 'off' : 'on'" data-section="cadence">
    <svg
      class="today-cadence__svg"
      :viewBox="`0 0 ${cadence.weights.length * 10} 64`"
      preserveAspectRatio="none"
      role="img"
      aria-label="24-hour activity cadence"
    >
      <g
        v-for="(weight, i) in cadence.weights"
        :key="i"
        class="today-cadence__bar-group"
      >
        <rect
          :x="i * 10"
          :y="64 - (barHeightPercent(weight) / 100) * 64"
          :width="6"
          :height="(barHeightPercent(weight) / 100) * 64"
          :class="[
            'today-cadence__bar',
            i === cadence.peakHourIndex ? 'today-cadence__bar--peak' : '',
            weight === 0 ? 'today-cadence__bar--idle' : '',
          ]"
          :data-hour="i"
          :data-peak="i === cadence.peakHourIndex ? 'true' : null"
        />
      </g>
    </svg>
    <div class="today-cadence__labels">
      <span
        v-for="(label, i) in HOUR_LABELS"
        :key="`label-${i}`"
        class="today-cadence__label"
      >{{ label }}</span>
    </div>
    <div class="today-cadence__minis">
      <div class="today-cadence__mini">
        <div class="tk-eyebrow">First action</div>
        <div class="today-cadence__mini-v">{{ cadence.firstAction }}</div>
      </div>
      <div class="today-cadence__mini">
        <div class="tk-eyebrow">Peak hour</div>
        <div class="today-cadence__mini-v">{{ cadence.peakAction }}</div>
      </div>
      <div class="today-cadence__mini">
        <div class="tk-eyebrow">Last action</div>
        <div class="today-cadence__mini-v">{{ cadence.lastAction }}</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.today-cadence {
  width: 100%;
}
.today-cadence__svg {
  width: 100%;
  height: 64px;
  display: block;
  margin-top: 6px;
}
.today-cadence__bar {
  fill: var(--ink-deep);
  opacity: 0.8;
}
.today-cadence__bar--idle {
  fill: var(--line);
  opacity: 1;
}
.today-cadence__bar--peak {
  fill: var(--ember);
  opacity: 1;
}
.today-cadence[data-pulse='on'] .today-cadence__bar--peak {
  animation: today-cadence-pulse 2.4s ease-in-out infinite;
}
.today-cadence[data-pulse='off'] .today-cadence__bar--peak {
  /* no animation when prefers-reduced-motion is set */
  animation: none;
}

@keyframes today-cadence-pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.55;
  }
}

.today-cadence__labels {
  display: grid;
  grid-template-columns: repeat(24, 1fr);
  gap: 0;
  margin-top: 4px;
}
.today-cadence__label {
  text-align: center;
  font-family: var(--mono);
  font-size: 9px;
  color: var(--faint);
}

.today-cadence__minis {
  display: flex;
  justify-content: space-between;
  margin-top: 10px;
  gap: 12px;
  flex-wrap: wrap;
}
.today-cadence__mini-v {
  font-family: var(--serif);
  font-style: italic;
  font-size: 14px;
  color: var(--ink-deep);
}
</style>
