import {
  calendarDateKeyToUtcDate,
  isCalendarDateKey,
  toCalendarDateKey,
} from './dueDates'

export type DisplayDateInput = string | Date | null | undefined

const CALENDAR_DATE_KEY = /^\d{4}-\d{2}-\d{2}$/
const ISO_CALENDAR_PREFIX = /^(\d{4}-\d{2}-\d{2})T/

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

function browserPreferredLocales(): readonly string[] {
  if (typeof navigator === 'undefined') return []
  if (navigator.languages?.length) return navigator.languages
  return navigator.language ? [navigator.language] : []
}

function canonicalLocale(locale: string): string | null {
  try {
    return Intl.getCanonicalLocales(locale.trim())[0] ?? null
  } catch {
    return null
  }
}

/**
 * Preserve a browser region only when it belongs to the active app language.
 * An explicitly regional app locale remains authoritative.
 */
export function resolveDisplayLocale(
  activeLocale: string,
  preferredLocales: readonly string[] = browserPreferredLocales(),
): string {
  const active = canonicalLocale(activeLocale) ?? activeLocale
  if (active.includes('-')) return active

  const activeLanguage = active.toLowerCase()
  for (const preferredLocale of preferredLocales) {
    const preferred = canonicalLocale(preferredLocale)
    if (preferred?.toLowerCase().split('-')[0] === activeLanguage) return preferred
  }

  return active
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
    return new Intl.DateTimeFormat(
      resolveDisplayLocale(locale),
      withDefaults(defaults, options),
    ).format(date)
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

function calendarKeyFromDisplayInput(value: string | null | undefined): string | null {
  if (typeof value !== 'string') return null
  const normalized = value.trim()
  if (!normalized) return null

  // Keep malformed calendar keys from being normalized by Date into another day.
  if (CALENDAR_DATE_KEY.test(normalized)) {
    return isCalendarDateKey(normalized) ? normalized : null
  }

  // V8 normalizes impossible ISO components (for example, February 30) into a
  // different valid instant. Validate the wire form's stated calendar day
  // before Date parsing so the adapter preserves its invalid-input contract.
  const isoCalendarPrefix = ISO_CALENDAR_PREFIX.exec(normalized)?.[1]
  if (isoCalendarPrefix && !isCalendarDateKey(isoCalendarPrefix)) return null

  return toCalendarDateKey(normalized)
}

/**
 * Format a persisted calendar date without projecting it through the browser
 * timezone. Both the canonical YYYY-MM-DD key and the API's ISO DateTimeOffset
 * compatibility form are accepted, then normalized back to a UTC calendar key.
 */
export function formatDisplayCalendarDate(
  value: string | null | undefined,
  locale: string,
  options: Intl.DateTimeFormatOptions = {},
): string | null {
  const key = calendarKeyFromDisplayInput(value)
  const date = key ? calendarDateKeyToUtcDate(key) : null
  if (!date) return null

  try {
    return new Intl.DateTimeFormat(resolveDisplayLocale(locale), {
      ...withDefaults(DISPLAY_DATE_DEFAULTS, options),
      // Calendar dates are days, not instants. Caller timezone options must not
      // move midnight UTC into the previous or next local day.
      timeZone: 'UTC',
    }).format(date)
  } catch {
    return null
  }
}