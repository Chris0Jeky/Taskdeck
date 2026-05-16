import { describe, expect, it } from 'vitest'
import {
  INK_BLEED_PHASE_SCHEDULE,
  INK_BLEED_TOTAL_MS,
  detectInkBleedReducedMotion,
} from '../../composables/inkBleedMotion'

describe('inkBleedMotion', () => {
  describe('INK_BLEED_PHASE_SCHEDULE', () => {
    it('has 6 phases in correct order', () => {
      expect(INK_BLEED_PHASE_SCHEDULE).toHaveLength(6)
      expect(INK_BLEED_PHASE_SCHEDULE.map((p) => p.phase)).toEqual([
        'drop',
        'bloom',
        'compose',
        'settle',
        'stamp',
        'dried',
      ])
    })

    it('starts at time 0', () => {
      expect(INK_BLEED_PHASE_SCHEDULE[0].at).toBe(0)
    })

    it('has monotonically increasing timestamps', () => {
      for (let i = 1; i < INK_BLEED_PHASE_SCHEDULE.length; i++) {
        expect(INK_BLEED_PHASE_SCHEDULE[i].at).toBeGreaterThan(
          INK_BLEED_PHASE_SCHEDULE[i - 1].at,
        )
      }
    })

    it('last phase matches INK_BLEED_TOTAL_MS', () => {
      const last = INK_BLEED_PHASE_SCHEDULE[INK_BLEED_PHASE_SCHEDULE.length - 1]
      expect(last.at).toBe(INK_BLEED_TOTAL_MS)
    })
  })

  describe('INK_BLEED_TOTAL_MS', () => {
    it('is 4600ms as specified', () => {
      expect(INK_BLEED_TOTAL_MS).toBe(4600)
    })
  })

  describe('detectInkBleedReducedMotion', () => {
    it('returns false when matchMedia is not available', () => {
      const original = globalThis.matchMedia
      // @ts-expect-error removing matchMedia for test
      delete globalThis.matchMedia
      expect(detectInkBleedReducedMotion()).toBe(false)
      globalThis.matchMedia = original
    })

    it('returns false when prefers-reduced-motion does not match', () => {
      const original = globalThis.matchMedia
      globalThis.matchMedia = (() => ({ matches: false })) as unknown as typeof matchMedia
      expect(detectInkBleedReducedMotion()).toBe(false)
      globalThis.matchMedia = original
    })

    it('returns true when prefers-reduced-motion matches', () => {
      const original = globalThis.matchMedia
      globalThis.matchMedia = (() => ({ matches: true })) as unknown as typeof matchMedia
      expect(detectInkBleedReducedMotion()).toBe(true)
      globalThis.matchMedia = original
    })

    it('returns false when matchMedia throws', () => {
      const original = globalThis.matchMedia
      globalThis.matchMedia = (() => {
        throw new Error('matchMedia exploded')
      }) as unknown as typeof matchMedia
      expect(detectInkBleedReducedMotion()).toBe(false)
      globalThis.matchMedia = original
    })

    it('passes the correct media query string', () => {
      const original = globalThis.matchMedia
      const spy = vi.fn(() => ({ matches: false }))
      globalThis.matchMedia = spy as unknown as typeof matchMedia
      detectInkBleedReducedMotion()
      expect(spy).toHaveBeenCalledWith('(prefers-reduced-motion: reduce)')
      globalThis.matchMedia = original
    })
  })
})
