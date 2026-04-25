export type InkBleedRuntimePhase =
  | 'drop'
  | 'bloom'
  | 'compose'
  | 'settle'
  | 'stamp'
  | 'dried'

export const INK_BLEED_PHASE_SCHEDULE: ReadonlyArray<{
  at: number
  phase: InkBleedRuntimePhase
}> = [
  { at: 0, phase: 'drop' },
  { at: 400, phase: 'bloom' },
  { at: 1400, phase: 'compose' },
  { at: 3400, phase: 'settle' },
  { at: 4200, phase: 'stamp' },
  { at: 4600, phase: 'dried' },
]

export const INK_BLEED_TOTAL_MS = 4600

export function detectInkBleedReducedMotion(): boolean {
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
