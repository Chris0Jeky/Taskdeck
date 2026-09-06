import { describe, expect, it } from 'vitest'
import {
  formatDisplayCalendarDate,
  formatDisplayDate,
  formatDisplayDateTime,
  formatDisplayTime,
} from '../../utils/displayDate'

const locales = ['en', 'it', 'es'] as const

describe('display date adapter', () => {
  it.each(locales)('formats an instant using the selected %s locale', (locale) => {
    const value = '2024-02-29T23:30:00Z'
    const options: Intl.DateTimeFormatOptions = {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      timeZone: 'UTC',
    }

    const result = formatDisplayDate(value, locale, options)
    const expected = new Intl.DateTimeFormat(locale, options).format(new Date(value))

    expect(result).toBe(expected)
    expect(result).not.toBeNull()
  })

  it('recomputes the label when the locale changes', () => {
    const value = '2024-02-29T23:30:00Z'
    const options: Intl.DateTimeFormatOptions = {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      timeZone: 'UTC',
    }

    const english = formatDisplayDate(value, 'en', options)
    const italian = formatDisplayDate(value, 'it', options)
    const spanish = formatDisplayDate(value, 'es', options)

    expect(new Set([english, italian, spanish]).size).toBe(3)
  })

  it('keeps an instant near midnight in the explicitly selected timezone', () => {
    const value = '2024-03-01T00:30:00Z'
    const utcOptions: Intl.DateTimeFormatOptions = {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      timeZone: 'UTC',
    }
    const pacificOptions: Intl.DateTimeFormatOptions = {
      ...utcOptions,
      timeZone: 'America/Los_Angeles',
    }

    expect(formatDisplayDate(value, 'en', utcOptions))
      .toBe(new Intl.DateTimeFormat('en', utcOptions).format(new Date(value)))
    expect(formatDisplayDate(value, 'en', pacificOptions))
      .toBe(new Intl.DateTimeFormat('en', pacificOptions).format(new Date(value)))
    expect(formatDisplayDate(value, 'en', utcOptions))
      .not.toBe(formatDisplayDate(value, 'en', pacificOptions))
  })

  it('keeps calendar-only values on the calendar path', () => {
    const options: Intl.DateTimeFormatOptions = {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    }

    expect(formatDisplayCalendarDate('2024-02-29', 'en', options))
      .toBe(new Intl.DateTimeFormat('en', { ...options, timeZone: 'UTC' }).format(new Date('2024-02-29T00:00:00Z')))
    expect(formatDisplayDate('2024-02-29', 'en', options)).toBeNull()
  })

  it.each([
    null,
    undefined,
    '',
    'not-a-date',
    new Date(Number.NaN),
  ])('returns null for absent or invalid instant input: %s', (value) => {
    expect(formatDisplayDateTime(value, 'en')).toBeNull()
    expect(formatDisplayDate(value, 'en')).toBeNull()
    expect(formatDisplayTime(value, 'en')).toBeNull()
  })

  it('returns null for invalid calendar-only values', () => {
    expect(formatDisplayCalendarDate('2024-02-30', 'en')).toBeNull()
    expect(formatDisplayCalendarDate('2024-02-29T00:00:00Z', 'en')).toBeNull()
  })
})

