import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  formatDisplayCalendarDate,
  formatDisplayDate,
  formatDisplayDateTime,
  formatDisplayTime,
  resolveDisplayLocale,
} from '../../utils/displayDate'

const locales = ['en', 'it', 'es'] as const

afterEach(() => {
  vi.unstubAllGlobals()
})

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

  it('accepts the persisted due-date wire form without projecting it through the caller timezone', () => {
    const value = '2024-02-29T00:00:00.000Z'
    const options: Intl.DateTimeFormatOptions = {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      timeZone: 'America/Los_Angeles',
    }

    const result = formatDisplayCalendarDate(value, 'en-GB', options)
    const expected = new Intl.DateTimeFormat('en-GB', {
      ...options,
      timeZone: 'UTC',
    }).format(new Date('2024-02-29T00:00:00.000Z'))
    const projectedAsInstant = new Intl.DateTimeFormat('en-GB', options)
      .format(new Date(value))

    expect(result).toBe(expected)
    expect(result).not.toBe(projectedAsInstant)
  })

  it('supports dateStyle without mixing it with component date fields', () => {
    const options: Intl.DateTimeFormatOptions = { dateStyle: 'long' }

    expect(formatDisplayCalendarDate('2024-02-29', 'en-GB', options)).toBe(
      new Intl.DateTimeFormat('en-GB', {
        ...options,
        timeZone: 'UTC',
      }).format(new Date('2024-02-29T00:00:00.000Z')),
    )
  })

  it('preserves a matching browser region but never borrows one from another language', () => {
    expect(resolveDisplayLocale('en', ['fr-FR', 'en-GB', 'en-US'])).toBe('en-GB')
    expect(resolveDisplayLocale('it', ['en-GB', 'es-ES'])).toBe('it')
    expect(resolveDisplayLocale('en-GB', ['en-US'])).toBe('en-GB')
  })

  it('uses the matching browser region when formatting a bare app locale', () => {
    vi.stubGlobal('navigator', {
      language: 'en-GB',
      languages: ['en-GB', 'en'],
    })
    const value = '2024-03-01T12:00:00Z'
    const options: Intl.DateTimeFormatOptions = {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      timeZone: 'UTC',
    }

    expect(formatDisplayDate(value, 'en', options)).toBe(
      new Intl.DateTimeFormat('en-GB', options).format(new Date(value)),
    )
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

  it.each([
    '2024-02-30',
    '2024-02-30T00:00:00Z',
    '2023-02-29T00:00:00.000Z',
    '2024-13-01T00:00:00+00:00',
    '2024-02-30t00:00:00Z',
    '2024-02-30 00:00:00Z',
    '2024-02-29T00:00:00',
    '2024-02-29T24:00:00Z',
    '2024-02-29T00:60:00Z',
    '2024-02-29T00:00:00+14:01',
    '2024-02-29T00:00:00+15:00',
    'not-a-date',
  ])('returns null for invalid calendar input: %s', (value) => {
    expect(formatDisplayCalendarDate(value, 'en')).toBeNull()
  })
})