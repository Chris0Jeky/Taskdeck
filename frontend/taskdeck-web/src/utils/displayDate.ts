import { formatCalendarDate, isCalendarDateKey } from './dueDates'

export type DisplayDateInput = string | Date | null | undefined

const CALENDAR_DATE_KEY = /^\d{4}-\d{2}-\d{2}$/

const DISPLAY_DATE_DEFAULTS: Intl.DateTimeFormatOptions = {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
}

const DISPLAY_TIME_DEFAULTS: Intl.DateTimeFormatOptions = {
  hour: 'numeric',
  minute: '2-digit',
}

const DISPLAY_DATE_TIME_DEFAULTS: Intl.DateTimeFormatOptions = {
  ...DISPLAY_DATE_DEFAULTS,
  ...DISPLAY_TIME_DEFAULTS,
}

function toInstantDate(value: DisplayDateInput): Date | null {
  if (value instanceof Date) {
    return Number.isNaN(value.getTime()) ? null : value
  }

  if (typeof value !== 'string') return null

  const normalized = value.trim()
  if (!normalized || CALENDAR_DATE_KEY.test(normalized)) return null

  const date = new Date(normalized)
  return Number.isNaN(date.getTime()) ? null : date
}

function withDefaults(
  defaults: Intl.DateTimeFormatOptions,
  options: Intl.DateTimeFormatOptions,
): Intl.DateTimeFormatOptions {
  if (options.dateStyle || options.timeStyle) return options
  return { ...defaults, ...options }
}

function formatInstant(
  value: DisplayDateInput,
  locale: string,
  defaults: Intl.DateTimeFormatOptions,
  options: Intl.DateTimeFormatOptions,
): string | null {
  const date = toInstantDate(value)
  if (!date) return null

  try {
    return new Intl.DateTimeFormat(locale, withDefaults(defaults, options)).format(date)
  } catch {
    return null
  }
}

/**
 * Format an instant with the active UI locale and caller-owned timezone policy.
 * Plain YYYY-MM-DD values are rejected so calendar dates use the UTC-safe path.
 */
export function formatDisplayDate(
  value: DisplayDateInput,
  locale: string,
  options: Intl.DateTimeFormatOptions = {},
): string | null {
  return formatInstant(value, locale, DISPLAY_DATE_DEFAULTS, options)
}

/** Format an instant's date and time without retaining a module-scope locale. */
export function formatDisplayDateTime(
  value: DisplayDateInput,
  locale: string,
  options: Intl.DateTimeFormatOptions = {},
): string | null {
  return formatInstant(value, locale, DISPLAY_DATE_TIME_DEFAULTS, options)
}

/** Format only the time portion of an instant. */
export function formatDisplayTime(
  value: DisplayDateInput,
  locale: string,
  options: Intl.DateTimeFormatOptions = {},
): string | null {
  return formatInstant(value, locale, DISPLAY_TIME_DEFAULTS, options)
}

/**
 * Format a persisted calendar-only YYYY-MM-DD key without projecting it through
 * the browser timezone. The existing due-date utility owns the UTC semantics.
 */
export function formatDisplayCalendarDate(
  value: string | null | undefined,
  locale: string,
  options: Intl.DateTimeFormatOptions = {},
): string | null {
  if (typeof value !== 'string' || !CALENDAR_DATE_KEY.test(value.trim()) || !isCalendarDateKey(value.trim())) return null

  try {
    return formatCalendarDate(value, withDefaults(DISPLAY_DATE_DEFAULTS, options), locale) || null
  } catch {
    return null
  }
}
