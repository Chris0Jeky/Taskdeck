<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

/**
 * PaperUndoTimeline — dashed timeline that crossfades dashes left-to-right
 * as the undo window closes.  Default `windowMs` is 6 hours, matching the
 * reversibility budget called out in the styleguide.
 *
 * Implementation notes:
 *  - We update via `requestAnimationFrame`, but cap actual progress writes
 *    to ~1Hz to avoid wasted paints.  The timeline doesn't need sub-second
 *    fidelity; the user perceives it as a slow countdown.
 *  - Under `prefers-reduced-motion: reduce` we render a static labelled bar
 *    with no rAF loop and no listener.
 *  - The rAF handle and a media-query listener are torn down in
 *    `onBeforeUnmount`.
 */
const DASH_COUNT = 24
const props = withDefaults(
  defineProps<{
    appliedAt: Date | number
    windowMs?: number
    /** Optional textual labels rendered above the timeline. */
    leftLabel?: string
    rightLabel?: string
  }>(),
  {
    windowMs: 6 * 60 * 60 * 1000,
    leftLabel: 'applied',
    rightLabel: 'window closes',
  },
)

const appliedTimestamp = computed(() =>
  props.appliedAt instanceof Date ? props.appliedAt.getTime() : Number(props.appliedAt),
)

/** Progress in [0,1].  When `1`, the window has closed and no dashes faded. */
const progress = ref(0)

const reducedMotion = ref(false)
let mql: MediaQueryList | null = null
let mqlHandler: ((e: MediaQueryListEvent) => void) | null = null

let rafId: number | null = null
let lastTickAt = 0

function computeProgress(now: number) {
  const elapsed = now - appliedTimestamp.value
  if (elapsed <= 0) return 0
  if (elapsed >= props.windowMs) return 1
  return elapsed / props.windowMs
}

function tick(now: number) {
  if (lastTickAt === 0 || now - lastTickAt >= 1000) {
    progress.value = computeProgress(Date.now())
    lastTickAt = now
  }
  if (progress.value < 1) {
    rafId = requestAnimationFrame(tick)
  } else {
    rafId = null
  }
}

function startLoop() {
  if (rafId != null) return
  lastTickAt = 0
  rafId = requestAnimationFrame(tick)
}

function stopLoop() {
  if (rafId != null) {
    cancelAnimationFrame(rafId)
    rafId = null
  }
}

function syncReducedMotion(matches: boolean) {
  reducedMotion.value = matches
  if (matches) {
    stopLoop()
    progress.value = computeProgress(Date.now())
  } else {
    startLoop()
  }
}

/**
 * If `appliedAt` or `windowMs` change after the loop has self-stopped (e.g. a
 * fresh undo window opens after the previous one closed), restart the rAF
 * loop so the timeline animates the new window instead of remaining stuck at
 * `progress = 1`.  Under reduced motion we just refresh `progress` once.
 */
watch(
  () => [appliedTimestamp.value, props.windowMs] as const,
  () => {
    lastTickAt = 0
    progress.value = computeProgress(Date.now())
    if (reducedMotion.value) return
    if (progress.value < 1) {
      startLoop()
    }
  },
)

onMounted(() => {
  if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
    mql = window.matchMedia('(prefers-reduced-motion: reduce)')
    mqlHandler = (e: MediaQueryListEvent) => syncReducedMotion(e.matches)
    if (typeof mql.addEventListener === 'function') {
      mql.addEventListener('change', mqlHandler)
    }
    syncReducedMotion(mql.matches)
  } else {
    syncReducedMotion(false)
  }
})

onBeforeUnmount(() => {
  stopLoop()
  if (mql && mqlHandler) {
    if (typeof mql.removeEventListener === 'function') {
      mql.removeEventListener('change', mqlHandler)
    }
  }
  mql = null
  mqlHandler = null
})

const dashes = computed(() => {
  const filled = Math.round(progress.value * DASH_COUNT)
  return Array.from({ length: DASH_COUNT }, (_, i) => i < filled)
})
</script>

<template>
  <div class="paper-undo" :data-reduced="reducedMotion ? 'true' : null">
    <div class="paper-undo__labels tk-meta">
      <span>{{ leftLabel }}</span>
      <span>{{ rightLabel }}</span>
    </div>
    <div
      v-if="!reducedMotion"
      class="paper-undo__track"
      role="progressbar"
      :aria-valuenow="Math.round(progress * 100)"
      aria-valuemin="0"
      aria-valuemax="100"
    >
      <span
        v-for="(spent, i) in dashes"
        :key="i"
        class="paper-undo__dash"
        :data-spent="spent ? 'true' : null"
      />
    </div>
    <div
      v-else
      class="paper-undo__static"
      role="progressbar"
      :aria-valuenow="Math.round(progress * 100)"
      aria-valuemin="0"
      aria-valuemax="100"
    >
      <span class="paper-undo__static-fill" :style="{ width: `${progress * 100}%` }" />
    </div>
  </div>
</template>

<style scoped>
.paper-undo {
  display: flex;
  flex-direction: column;
  gap: 4px;
  width: 100%;
}
.paper-undo__labels {
  display: flex;
  justify-content: space-between;
  color: var(--mute);
}
.paper-undo__track {
  display: flex;
  gap: 2px;
  align-items: center;
}
.paper-undo__dash {
  flex: 1;
  height: 6px;
  background: var(--ember);
  opacity: 0.85;
  transition: opacity 240ms cubic-bezier(0.2, 0.65, 0.25, 1);
}
.paper-undo__dash[data-spent='true'] {
  opacity: 0.18;
}
.paper-undo__static {
  position: relative;
  height: 6px;
  background: var(--line-soft);
}
.paper-undo__static-fill {
  position: absolute;
  inset: 0 auto 0 0;
  background: var(--ember);
  opacity: 0.6;
}
@media (prefers-reduced-motion: reduce) {
  .paper-undo__dash {
    transition: none;
  }
}
</style>
