import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  installTimeZone,
  instantAtZonedWallClock,
  offsetMinutesAt,
  zonedParts,
  type WallClock,
} from './timeZone'

/**
 * Regression for #2943.
 *
 * The frontend mutation lane failed in Stryker's Vitest *dry run* — before any
 * mutant executed — because `vi.stubEnv('TZ', …)` does not change the runtime
 * zone under `pool: 'threads'`, which `@stryker-mutator/vitest-runner` forces.
 * Zone-sensitive assertions silently measured the CI runner's own zone instead.
 *
 * Every assertion below is derived from explicit `Intl` zone arguments, so this
 * file is green under both pools and on a host in any zone. Run it under the
 * exact dry-run shape with:
 *
 *   npx vitest --run --pool=threads --maxWorkers=1 src/tests/utils/timeZone.spec.ts
 */
describe('timeZone test helper (#2943)', () => {
  let restore: (() => void) | null = null

  afterEach(() => {
    restore?.()
    restore = null
    vi.useRealTimers()
    vi.unstubAllEnvs()
  })

  describe('zonedParts / offsetMinutesAt', () => {
    it('reads a zone-local wall clock without touching process state', () => {
      const instant = new Date(Date.UTC(2026, 7, 18, 22, 0, 0))

      expect(zonedParts(instant, 'Pacific/Kiritimati')).toEqual({
        year: 2026,
        month: 7,
        day: 19,
        hour: 12,
        minute: 0,
        second: 0,
      })
      expect(zonedParts(instant, 'UTC').day).toBe(18)
    })

    it('reports offsets in getTimezoneOffset units', () => {
      const instant = new Date(Date.UTC(2026, 7, 19, 12, 0, 0))

      expect(offsetMinutesAt(instant, 'UTC')).toBe(0)
      expect(offsetMinutesAt(instant, 'Pacific/Kiritimati')).toBe(-840)
      expect(offsetMinutesAt(instant, 'Pacific/Midway')).toBe(660)
      expect(offsetMinutesAt(instant, 'Asia/Kolkata')).toBe(-330)
    })

    it('follows a zone across its own DST transition', () => {
      const summer = new Date(Date.UTC(2026, 6, 1, 12, 0, 0))
      const winter = new Date(Date.UTC(2026, 11, 1, 12, 0, 0))

      expect(offsetMinutesAt(summer, 'America/New_York')).toBe(240)
      expect(offsetMinutesAt(winter, 'America/New_York')).toBe(300)
    })

    it('is unaffected by a TZ env stub, in any pool', () => {
      const instant = new Date(Date.UTC(2026, 7, 18, 22, 0, 0))
      const before = zonedParts(instant, 'Pacific/Kiritimati')

      // The trap this module exists to remove: under `pool: 'threads'` this
      // write lands in the thread's env store and changes nothing else. These
      // helpers must not care either way.
      vi.stubEnv('TZ', 'Pacific/Midway')

      expect(zonedParts(instant, 'Pacific/Kiritimati')).toEqual(before)
      expect(offsetMinutesAt(instant, 'Pacific/Kiritimati')).toBe(-840)
    })
  })

  describe('instantAtZonedWallClock', () => {
    it('round-trips a wall clock through its zone', () => {
      const wall: WallClock = [2026, 7, 19, 12, 0, 0]
      const instant = instantAtZonedWallClock(wall, 'Pacific/Kiritimati')

      expect(instant.toISOString()).toBe('2026-08-18T22:00:00.000Z')
      expect(zonedParts(instant, 'Pacific/Kiritimati')).toEqual({
        year: 2026,
        month: 7,
        day: 19,
        hour: 12,
        minute: 0,
        second: 0,
      })
    })

    it('resolves a wall clock on the far side of a DST transition', () => {
      // 01:30 on 2026-11-01 is ambiguous in New York (it happens twice); the
      // helper must still land on an instant that shows exactly that clock.
      const instant = instantAtZonedWallClock([2026, 10, 1, 1, 30, 0], 'America/New_York')

      expect(zonedParts(instant, 'America/New_York')).toMatchObject({
        month: 10,
        day: 1,
        hour: 1,
        minute: 30,
      })
    })

    it('refuses a wall clock that does not exist in the zone', () => {
      // 2026-03-08 02:30 is skipped by the US spring-forward.
      expect(() => instantAtZonedWallClock([2026, 2, 8, 2, 30, 0], 'America/New_York')).toThrow(
        /does not exist in America\/New_York/,
      )
    })
  })

  describe('installTimeZone', () => {
    it('makes Date and Intl report the installed zone', () => {
      const hostZone = Intl.DateTimeFormat().resolvedOptions().timeZone
      const instant = instantAtZonedWallClock([2026, 7, 19, 12, 0, 0], 'Pacific/Kiritimati')

      restore = installTimeZone('Pacific/Kiritimati')

      expect(Intl.DateTimeFormat().resolvedOptions().timeZone).toBe('Pacific/Kiritimati')
      expect(instant.getTimezoneOffset()).toBe(-840)
      expect(instant.getFullYear()).toBe(2026)
      expect(instant.getMonth()).toBe(7)
      expect(instant.getDate()).toBe(19)
      expect(instant.getHours()).toBe(12)
      // UTC is still on the previous day — the #1768 dimension, asserted here
      // without any dependence on the host zone.
      expect(instant.getUTCDate()).toBe(18)

      restore()
      restore = null

      expect(Intl.DateTimeFormat().resolvedOptions().timeZone).toBe(hostZone)
      expect(instant.getUTCDate()).toBe(18)
    })

    it('keeps the local accessors mutually consistent', () => {
      const instant = instantAtZonedWallClock([2026, 7, 20, 0, 15, 30], 'Asia/Kolkata')
      restore = installTimeZone('Asia/Kolkata')

      expect([
        instant.getFullYear(),
        instant.getMonth(),
        instant.getDate(),
        instant.getHours(),
        instant.getMinutes(),
        instant.getSeconds(),
      ]).toEqual([2026, 7, 20, 0, 15, 30])
      // 2026-08-20 is a Thursday.
      expect(instant.getDay()).toBe(4)
      expect(instant.getTime() - instant.getTimezoneOffset() * 60_000).toBe(
        Date.UTC(2026, 7, 20, 0, 15, 30),
      )
    })

    it('defaults Intl formatters to the zone but never overrides an explicit one', () => {
      const instant = new Date(Date.UTC(2026, 7, 18, 22, 0, 0))
      restore = installTimeZone('Pacific/Kiritimati')

      expect(new Intl.DateTimeFormat('en-US', { day: 'numeric' }).format(instant)).toBe('19')
      expect(
        new Intl.DateTimeFormat('en-US', { day: 'numeric', timeZone: 'UTC' }).format(instant),
      ).toBe('18')
      expect(instant.toLocaleDateString('en-US', { timeZone: 'UTC' })).toContain('18')
    })

    it('cooperates with fake timers so `new Date()` reads the zone', () => {
      const instant = instantAtZonedWallClock([2026, 7, 19, 2, 43, 12], 'Pacific/Kiritimati')
      vi.useFakeTimers()
      restore = installTimeZone('Pacific/Kiritimati')
      vi.setSystemTime(instant)

      const now = new Date()

      expect(now.getHours()).toBe(2)
      expect(now.getDate()).toBe(19)
      expect(now.getUTCDate()).toBe(18)
      expect(Intl.DateTimeFormat().resolvedOptions().timeZone).toBe('Pacific/Kiritimati')
    })

    it('documents the ordering rule: fake timers replace the whole Intl global', () => {
      // Installed BEFORE useFakeTimers, the Intl default zone is discarded —
      // vitest swaps `Intl` itself for a clock-aware stand-in. `Date`'s local
      // accessors survive because those are patched on `Date.prototype`.
      restore = installTimeZone('Pacific/Kiritimati')
      expect(Intl.DateTimeFormat().resolvedOptions().timeZone).toBe('Pacific/Kiritimati')

      vi.useFakeTimers()

      expect(Intl.DateTimeFormat().resolvedOptions().timeZone).not.toBe('Pacific/Kiritimati')
      expect(new Date(Date.UTC(2026, 7, 18, 22, 0, 0)).getDate()).toBe(19)
    })

    it('restores exactly once, however many times it is called', () => {
      const hostOffset = new Date(Date.UTC(2026, 7, 19, 12, 0, 0)).getTimezoneOffset()
      const undo = installTimeZone('Pacific/Kiritimati')

      undo()
      undo()

      expect(new Date(Date.UTC(2026, 7, 19, 12, 0, 0)).getTimezoneOffset()).toBe(hostOffset)
    })

    it('rejects an unknown zone instead of silently formatting in UTC', () => {
      expect(() => installTimeZone('Mars/Olympus_Mons')).toThrow()
    })
  })
})
