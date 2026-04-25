/**
 * useInkBleed — wraps an async LLM call with the Ink Bleed lifecycle.
 *
 * Behavior contract (issue #1006):
 * - `start()` begins the bleed. Phase advances drop → bloom → compose → settle
 *   → stamp → dried over 4.6s exactly.
 * - If the wrapped async work resolves *before* 4.6s, the bleed is held to its
 *   scheduled end before `done` fires (the user always sees the full sequence).
 * - If the wrapped async work runs *longer* than 4.6s, the bleed holds the
 *   `dried` phase (no looped bloom — that breaks the spec metaphor) and the
 *   composable signals the caller via `loop=true` so the eyebrow can pulse.
 * - Singleton guard: at most one active bleed per view. Calling `start()` while
 *   one is already running cancels the previous and begins a new sequence.
 * - Reduced-motion users skip the timer pipeline entirely. `start()` resolves
 *   immediately to `dried` and `done` fires after one tick.
 */
import { onBeforeUnmount, readonly, ref } from 'vue'

export type InkBleedPhase =
  | 'drop'
  | 'bloom'
  | 'compose'
  | 'settle'
  | 'stamp'
  | 'dried'

export interface UseInkBleedReturn {
  /** Begin (or restart) the bleed. Cancels any in-flight bleed. */
  start: () => void
  /**
   * Signal that the wrapped async call has completed. If called early the
   * bleed runs through the full 4.6s before `done` is emitted; if called
   * late (already in dried hold) it resolves immediately.
   */
  finish: () => void
  /** Cancel and discard the current bleed without firing `done`. */
  cancel: () => void
  /** Current phase (readonly Vue ref). */
  phase: Readonly<ReturnType<typeof ref<InkBleedPhase>>>
  /** Whether the user has prefers-reduced-motion enabled. */
  isReducedMotion: Readonly<ReturnType<typeof ref<boolean>>>
  /**
   * True while the bleed has overrun 4.6s waiting for the async call to
   * resolve. Bind to the InkBleed component's `loop` prop so the eyebrow can
   * pulse without looping the bloom (spec: don't loop the bloom).
   */
  loop: Readonly<ReturnType<typeof ref<boolean>>>
}

const PHASE_SCHEDULE: ReadonlyArray<{ at: number; phase: InkBleedPhase }> = [
  { at: 0, phase: 'drop' },
  { at: 400, phase: 'bloom' },
  { at: 1400, phase: 'compose' },
  { at: 3400, phase: 'settle' },
  { at: 4200, phase: 'stamp' },
  { at: 4600, phase: 'dried' },
]

const TOTAL_MS = 4600

function detectReducedMotion(): boolean {
  if (typeof globalThis === 'undefined') return false
  const mm = (globalThis as { matchMedia?: (q: string) => MediaQueryList })
    .matchMedia
  if (typeof mm !== 'function') return false
  try {
    return mm('(prefers-reduced-motion: reduce)').matches === true
  } catch {
    return false
  }
}

export interface UseInkBleedOptions {
  /** Optional callback fired when the bleed reaches the `dried` end-state. */
  onDone?: () => void
}

export function useInkBleed(
  options: UseInkBleedOptions = {},
): UseInkBleedReturn {
  const phase = ref<InkBleedPhase>('dried')
  const isReducedMotion = ref(detectReducedMotion())
  const loop = ref(false)

  // Active sequence state. We store all timer ids so we can cancel them when
  // a new `start()` arrives or the host unmounts.
  let timers: number[] = []
  let finishedEarly = false
  let scheduledEnd = 0
  let active = false

  function clearTimers(): void {
    for (const id of timers) {
      clearTimeout(id)
    }
    timers = []
  }

  function fireDone(): void {
    active = false
    options.onDone?.()
  }

  function start(): void {
    // Singleton: cancel previous (no done fired — last write wins).
    clearTimers()
    finishedEarly = false
    loop.value = false
    active = true

    if (isReducedMotion.value) {
      // Short-circuit: skip the timer pipeline entirely.
      phase.value = 'dried'
      // Defer done to next tick so callers can subscribe before it fires.
      const id = (globalThis.setTimeout as typeof setTimeout)(() => {
        if (!active) return
        fireDone()
      }, 0) as unknown as number
      timers.push(id)
      return
    }

    scheduledEnd = Date.now() + TOTAL_MS
    phase.value = 'drop'

    for (const step of PHASE_SCHEDULE) {
      if (step.at === 0) continue
      const id = (globalThis.setTimeout as typeof setTimeout)(() => {
        phase.value = step.phase
        if (step.phase === 'dried') {
          if (finishedEarly) {
            fireDone()
          } else {
            // Hold dried while waiting for finish(); pulse eyebrow via loop.
            loop.value = true
          }
        }
      }, step.at) as unknown as number
      timers.push(id)
    }
  }

  function finish(): void {
    if (!active) return

    const remaining = scheduledEnd - Date.now()

    if (isReducedMotion.value || remaining <= 0) {
      // Already past scheduled end (or reduced-motion path).
      loop.value = false
      phase.value = 'dried'
      clearTimers()
      fireDone()
      return
    }

    // Resolved early: let the bleed run to its scheduled end, then fire done.
    finishedEarly = true
  }

  function cancel(): void {
    clearTimers()
    active = false
    finishedEarly = false
    loop.value = false
    phase.value = 'dried'
  }

  onBeforeUnmount(() => {
    clearTimers()
    active = false
  })

  return {
    start,
    finish,
    cancel,
    phase: readonly(phase),
    isReducedMotion: readonly(isReducedMotion),
    loop: readonly(loop),
  }
}
