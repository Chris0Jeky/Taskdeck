import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  addCalendarDays,
  calendarDateKeyToMidnightUtc,
  formatCalendarDate,
  isCalendarDateKey,
  isCalendarDateOverdue,
  localCalendarDateKey,
  toCalendarDateKey,
} from '../../utils/dueDates'

describe('dueDates calendar-day contract', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
    vi.useRealTimers()
  })

  it.each([
    ['America/Los_Angeles', '2026-08-22'],
    ['UTC', '2026-08-23'],
    ['Asia/Kolkata', '2026-08-23'],
  ])('preserves the UTC calendar key when %s projects the instant as %s', (timeZone, projectedKey) => {
    vi.stubEnv('TZ', timeZone)
    const persisted = '2026-08-23T00:00:00.000Z'

    // Load-bearing timezone proof: this is the projection that caused the
    // west-of-UTC one-day regression. Due-date readers must not use it.
    expect(localCalendarDateKey(new Date(persisted))).toBe(projectedKey)
    expect(toCalendarDateKey(persisted)).toBe('2026-08-23')
    expect(formatCalendarDate(
      persisted,
      { year: 'numeric', month: 'long', day: 'numeric' },
      'en-US',
    )).toBe('August 23, 2026')
  })

  it.each([
    ['America/Los_Angeles', '2026-08-23'],
    ['UTC', '2026-08-23'],
    ['Pacific/Kiritimati', '2026-08-24'],
  ])('derives the caller localDate in %s', (timeZone, expectedKey) => {
    vi.stubEnv('TZ', timeZone)
    expect(localCalendarDateKey(new Date('2026-08-23T12:30:00.000Z'))).toBe(expectedKey)
  })

  it('validates real calendar days and serializes date-input writes as midnight UTC', () => {
    expect(isCalendarDateKey('2024-02-29')).toBe(true)
    expect(isCalendarDateKey('2026-02-29')).toBe(false)
    expect(isCalendarDateKey('2026-13-01')).toBe(false)
    expect(calendarDateKeyToMidnightUtc('2026-08-23')).toBe('2026-08-23T00:00:00.000Z')
    expect(calendarDateKeyToMidnightUtc('2026-02-29')).toBeNull()
  })

  it('compares and advances fixed-width calendar keys without instant arithmetic', () => {
    expect(isCalendarDateOverdue('2026-08-22T23:59:59-07:00', '2026-08-23')).toBe(false)
    expect(isCalendarDateOverdue('2026-08-22T00:00:00.000Z', '2026-08-23')).toBe(true)
    expect(isCalendarDateOverdue('2026-08-23T00:00:00.000Z', '2026-08-23')).toBe(false)
    expect(addCalendarDays('2024-02-28', 1)).toBe('2024-02-29')
    expect(addCalendarDays('2024-12-31', 1)).toBe('2025-01-01')
  })

  it('fails closed for malformed persisted values', () => {
    expect(toCalendarDateKey('not-a-date')).toBeNull()
    expect(formatCalendarDate('not-a-date')).toBe('')
    expect(isCalendarDateOverdue('not-a-date', '2026-08-23')).toBe(false)
  })
})
