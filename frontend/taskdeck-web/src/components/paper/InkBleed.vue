<script setup lang="ts">
/**
 * Ink Bleed — Paper & Graphite signature LLM thinking state.
 *
 * Replaces every loading / spinner / skeleton in LLM-driven flows. Five phases
 * over 4.6s total: drop, bloom, compose, settle, stamp. After 4.6s the bleed
 * is held in its `dried` state. Reduced-motion users get a 200ms opacity fade
 * with the dried+stamped visual; the timer pipeline is short-circuited so no
 * work queues. The SSR / initial markup also renders the dried+stamped frame
 * so progressive enhancement degrades gracefully when JS is disabled.
 *
 * Spec: design_handoff_taskdeck_paper/paper/surface-motion.jsx + issue #1006.
 */
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

export type InkBleedPhase =
  | 'auto'
  | 'drop'
  | 'bloom'
  | 'compose'
  | 'settle'
  | 'stamp'
  | 'dried'

interface Props {
  phase?: InkBleedPhase
  headline?: string
  containerSize?: number
  loop?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  phase: 'auto',
  headline: '',
  containerSize: undefined,
  loop: false,
})

const emit = defineEmits<{
  (e: 'phasechange', phase: Exclude<InkBleedPhase, 'auto'>): void
  (e: 'done'): void
}>()

/* ---------------------------------------------------------------- timing -- */

// Schedule of phase boundaries in ms (0..4600). Mirrors the spec table.
const PHASE_SCHEDULE: ReadonlyArray<{
  at: number
  phase: Exclude<InkBleedPhase, 'auto'>
}> = [
  { at: 0, phase: 'drop' },
  { at: 400, phase: 'bloom' },
  { at: 1400, phase: 'compose' },
  { at: 3400, phase: 'settle' },
  { at: 4200, phase: 'stamp' },
  { at: 4600, phase: 'dried' },
]

/* --------------------------------------------------- droplets (seeded) --- */

// Deterministic pseudo-random so SSR and hydration agree, and reduced-motion
// renders the same final state. 4 droplets at irregular (non-symmetric) spots.
// Positions are kept inside 20%..80% on x and 32%..72% on y so the bleed never
// leaves the container.
interface Droplet {
  x: number // %
  y: number // %
  delay: number // ms
  size: number // px (responsive cap is via container)
}

const DROPLETS: ReadonlyArray<Droplet> = [
  { x: 38, y: 48, delay: 0, size: 110 },
  { x: 56, y: 56, delay: 700, size: 80 },
  { x: 30, y: 64, delay: 1600, size: 70 },
  { x: 64, y: 44, delay: 2500, size: 90 },
]

/* --------------------------------------------------- reduced-motion ------ */

function detectReducedMotion(): boolean {
  if (typeof globalThis === 'undefined') return false
  // matchMedia may be missing in non-DOM environments; treat absence as "no".
  const mm = (globalThis as { matchMedia?: (q: string) => MediaQueryList })
    .matchMedia
  if (typeof mm !== 'function') return false
  try {
    return mm('(prefers-reduced-motion: reduce)').matches === true
  } catch {
    return false
  }
}

const isReducedMotion = ref(false)

/* --------------------------------------------------- phase state --------- */

// Initial render: dried+stamped frame so SSR / no-JS shows the final state.
// onMounted will rewind to 'drop' for animated users (when phase === 'auto').
const currentPhase = ref<Exclude<InkBleedPhase, 'auto'>>('dried')

const startTime = ref<number | null>(null)
const timers: number[] = []

function clearTimers(): void {
  for (const id of timers.splice(0)) {
    clearTimeout(id)
  }
}

function setPhase(next: Exclude<InkBleedPhase, 'auto'>): void {
  if (currentPhase.value === next) return
  currentPhase.value = next
  emit('phasechange', next)
  if (next === 'dried') emit('done')
}

function scheduleSequence(): void {
  clearTimers()
  startTime.value = Date.now()
  setPhase('drop')
  for (const step of PHASE_SCHEDULE) {
    if (step.at === 0) continue
    const id = (globalThis.setTimeout as typeof setTimeout)(() => {
      setPhase(step.phase)
    }, step.at) as unknown as number
    timers.push(id)
  }
}

/* --------------------------------------------------- lifecycle ----------- */

onMounted(() => {
  isReducedMotion.value = detectReducedMotion()

  if (isReducedMotion.value) {
    // Short-circuit: no timer work, no phase transitions. Hold dried frame
    // (initial render is already dried) and emit done after the 200ms fade.
    setPhase('dried')
    return
  }

  if (props.phase === 'auto') {
    scheduleSequence()
  } else {
    setPhase(props.phase as Exclude<InkBleedPhase, 'auto'>)
  }
})

onBeforeUnmount(() => {
  clearTimers()
})

watch(
  () => props.phase,
  (next) => {
    if (isReducedMotion.value) return
    clearTimers()
    if (next === 'auto') {
      scheduleSequence()
    } else {
      setPhase(next as Exclude<InkBleedPhase, 'auto'>)
    }
  },
)

/* --------------------------------------------------- visual derivations -- */

const containerStyle = computed(() => {
  if (props.containerSize && props.containerSize > 0) {
    return {
      width: `${props.containerSize}px`,
      height: `${props.containerSize}px`,
    }
  }
  return { width: '100%', height: '100%' }
})

// During settle/dried/stamp the ink desaturates from ember to ink-deep.
const drying = computed(() =>
  ['settle', 'stamp', 'dried'].includes(currentPhase.value),
)

// Eyebrow pulses only when caller has set loop and the bleed is held dried
// past the scheduled end (composable signals this by leaving phase=dried while
// `loop` is true).
const pulseEyebrow = computed(
  () => props.loop === true && currentPhase.value === 'dried',
)

const containerClass = computed(() => [
  'ink-bleed',
  `ink-bleed--${currentPhase.value}`,
  isReducedMotion.value ? 'ink-bleed--reduced' : '',
  drying.value ? 'ink-bleed--drying' : '',
])

const eyebrowText = computed(() =>
  currentPhase.value === 'dried' || currentPhase.value === 'stamp'
    ? 'Proposal · ready'
    : 'haiku is composing…',
)

/* --------------------------------------------------- droplet helpers ----- */

interface VisibleDroplet extends Droplet {
  visible: boolean
}

const dropletData = computed<VisibleDroplet[]>(() =>
  DROPLETS.map((d) => ({
    ...d,
    visible: currentPhase.value !== 'drop' || d.delay === 0,
  })),
)
</script>

<template>
  <div
    :class="containerClass"
    :style="containerStyle"
    role="img"
    :aria-label="
      currentPhase === 'dried' || currentPhase === 'stamp'
        ? 'Proposal ready'
        : 'Composing proposal'
    "
    aria-live="polite"
  >
    <div class="ink-bleed__stage">
      <div
        v-for="(d, i) in dropletData"
        :key="i"
        class="ink-bleed__drop"
        :data-index="i"
        :style="{
          left: `${d.x}%`,
          top: `${d.y}%`,
          width: `${d.size}px`,
          height: `${d.size}px`,
          animationDelay: `${d.delay}ms`,
          opacity: d.visible || currentPhase === 'dried' ? undefined : 0,
        }"
      />

      <div class="ink-bleed__copy">
        <div
          class="tk-eyebrow ink-bleed__eyebrow"
          :class="{ 'ink-bleed__eyebrow--pulse': pulseEyebrow }"
        >
          {{ eyebrowText }}
        </div>
        <div v-if="headline" class="ink-bleed__headline">{{ headline }}</div>
      </div>

      <div
        v-if="currentPhase === 'stamp' || currentPhase === 'dried'"
        class="ink-bleed__stamp"
        :class="{ 'ink-bleed__stamp--pressed': currentPhase === 'stamp' }"
        aria-hidden="true"
      >
        <span class="stamp ember">
          <span>Proposed</span>
          <b>Ready</b>
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* Container is positioned so droplets can absolutely stack inside it.
   Overflow hidden prevents ember leak past the bleed boundary. */
.ink-bleed {
  position: relative;
  overflow: hidden;
  isolation: isolate;
}

.ink-bleed__stage {
  position: absolute;
  inset: 0;
}

/* Each droplet is a radial-gradient blob in seal-red, multiplied onto paper.
   The bloom-scale curve grows it 1000ms; opacity rises linearly across 1400ms.
   During the dry/settle window the filter blur grows from 6px to 10px and the
   color desaturates toward --ink-deep. Each droplet inherits its own delay so
   subsequent droplets land on rhythm with the compose phase. */
.ink-bleed__drop {
  position: absolute;
  transform: translate(-50%, -50%) scale(0.2);
  background: radial-gradient(
    circle,
    var(--ember, #a8421f) 0%,
    var(--ember, #a8421f) 28%,
    transparent 72%
  );
  filter: blur(6px);
  opacity: 0;
  mix-blend-mode: multiply;
  pointer-events: none;
  border-radius: 50%;
}

.ink-bleed--drop .ink-bleed__drop[data-index='0'],
.ink-bleed--bloom .ink-bleed__drop,
.ink-bleed--compose .ink-bleed__drop,
.ink-bleed--settle .ink-bleed__drop,
.ink-bleed--stamp .ink-bleed__drop,
.ink-bleed--dried .ink-bleed__drop {
  animation: ink-bleed-bloom 1400ms linear forwards,
    ink-bleed-grow 1000ms cubic-bezier(0.2, 0.65, 0.25, 1) forwards;
}

/* Settle: desaturate. Stamp / dried: hold the desaturated state with full blur. */
.ink-bleed--drying .ink-bleed__drop {
  filter: blur(10px);
  background: radial-gradient(
    circle,
    var(--ink-deep, #0a0908) 0%,
    var(--ink-deep, #0a0908) 28%,
    transparent 72%
  );
  transition:
    filter 800ms cubic-bezier(0.3, 0.8, 0.3, 1),
    background 800ms linear;
}

.ink-bleed__copy {
  position: absolute;
  inset: auto 8% 12% 8%;
  font-family: var(--serif, Georgia, serif);
  color: var(--ink-deep, #0a0908);
}

.ink-bleed__eyebrow {
  margin-bottom: 6px;
}

.ink-bleed__eyebrow--pulse {
  animation: ink-bleed-eyebrow-pulse 1800ms ease-in-out infinite;
}

.ink-bleed__headline {
  font-family: var(--serif, Georgia, serif);
  font-style: italic;
  font-size: 28px;
  line-height: 1.06;
  letter-spacing: -0.014em;
  color: var(--ink-deep, #0a0908);
}

.ink-bleed--drop .ink-bleed__headline,
.ink-bleed--bloom .ink-bleed__headline {
  -webkit-mask-image: linear-gradient(
    90deg,
    #000 0%,
    transparent 12%
  );
  mask-image: linear-gradient(90deg, #000 0%, transparent 12%);
}

.ink-bleed--compose .ink-bleed__headline {
  -webkit-mask-image: linear-gradient(
    90deg,
    #000 60%,
    transparent 72%
  );
  mask-image: linear-gradient(90deg, #000 60%, transparent 72%);
  transition: -webkit-mask-image 2000ms cubic-bezier(0.3, 0.8, 0.3, 1),
    mask-image 2000ms cubic-bezier(0.3, 0.8, 0.3, 1);
}

.ink-bleed--settle .ink-bleed__headline,
.ink-bleed--stamp .ink-bleed__headline,
.ink-bleed--dried .ink-bleed__headline {
  -webkit-mask-image: none;
  mask-image: none;
}

.ink-bleed__stamp {
  position: absolute;
  right: 6%;
  top: 6%;
  transform: rotate(-7deg);
  transition: transform 320ms cubic-bezier(0.4, 0, 0.15, 1);
}

.ink-bleed__stamp--pressed {
  transform: rotate(-7deg) scale(0.96) translateY(1px);
}

/* Reduced-motion: keep the dried+stamped frame, fade in over 200ms. No other
   animations run because the script short-circuits and never advances phase. */
.ink-bleed--reduced .ink-bleed__drop {
  animation: none !important;
  opacity: 0.6;
  filter: blur(10px);
  transform: translate(-50%, -50%) scale(1);
  background: radial-gradient(
    circle,
    var(--ink-deep, #0a0908) 0%,
    var(--ink-deep, #0a0908) 28%,
    transparent 72%
  );
}

.ink-bleed--reduced {
  animation: ink-bleed-fade-in 200ms linear;
}

@keyframes ink-bleed-fade-in {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}

@keyframes ink-bleed-bloom {
  from {
    opacity: 0;
  }
  to {
    opacity: 0.78;
  }
}

@keyframes ink-bleed-grow {
  from {
    transform: translate(-50%, -50%) scale(0.2);
  }
  to {
    transform: translate(-50%, -50%) scale(1.4);
  }
}

@keyframes ink-bleed-eyebrow-pulse {
  0%,
  100% {
    opacity: 0.5;
  }
  50% {
    opacity: 1;
  }
}

/* Honour the OS-level setting at the CSS layer too — the script-level guard
   is the source of truth (so timers don't queue), this is just defence in
   depth for environments where matchMedia is mocked or stale. */
@media (prefers-reduced-motion: reduce) {
  .ink-bleed__drop,
  .ink-bleed__headline,
  .ink-bleed__eyebrow,
  .ink-bleed__stamp {
    animation: none !important;
    transition: none !important;
  }
}
</style>
