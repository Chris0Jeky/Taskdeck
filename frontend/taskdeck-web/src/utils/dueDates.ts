/**
 * Taskdeck due dates are calendar days, not moments in time.
 *
 * The existing API/persistence shape remains an ISO DateTimeOffset string. New
 * date-input writes use midnight UTC, and every reader derives the canonical
 * UTC YYYY-MM-DD key before formatting or comparing. This avoids projecting a
 * midnight-UTC value into the browser's timezone and changing the day.
 */

const CALENDAR_DATE_KEY = /^(\d{4})-(\d{2})-(\d{2})$/

export function isCalendarDateKey(value: string): boolean {
  const match = CALENDAR_DATE_KEY.exec(value)
  if (!match) return false

  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (year < 1) return false

  const candidate = new Date(0)
  candidate.setUTCHours(0, 0, 0, 0)
  candidate.setUTCFullYear(year, month - 1, day)
  return candidate.getUTCFullYear() === year
    && candidate.getUTCMonth() === month - 1
    && candidate.getUTCDate() === day
}

/** Return the persisted due date's canonical UTC calendar key. */
export function toCalendarDateKey(value: string | null | undefined): string | null {
  if (!value) return null
  if (isCalendarDateKey(value)) return value

  const instant = new Date(value)
  if (Number.isNaN(instant.getTime())) return null
  return instant.toISOString().slice(0, 10)
}

/** Return the calendar key for the browser's local day. */
export function localCalendarDateKey(date: Date = new Date()): string {
  const year = date.getFullYear().toString().padStart(4, '0')
  const month = (date.getMonth() + 1).toString().padStart(2, '0')
  const day = date.getDate().toString().padStart(2, '0')
  return `${year}-${month}-${day}`
}

/** Convert a valid calendar key to the compatibility DateTimeOffset wire form. */
export function calendarDateKeyToMidnightUtc(value: string): string | null {
  return isCalendarDateKey(value) ? `${value}T00:00:00.000Z` : null
}

/** Materialize a calendar key only for UTC calendar arithmetic/formatting. */
export function calendarDateKeyToUtcDate(value: string): Date | null {
  const iso = calendarDateKeyToMidnightUtc(value)
  return iso ? new Date(iso) : null
}

export function addCalendarDays(value: string, days: number): string | null {
  const date = calendarDateKeyToUtcDate(value)
  if (!date || !Number.isInteger(days)) return null
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

export function formatCalendarDate(
  value: string | null | undefined,
  options: Intl.DateTimeFormatOptions = {},
  locale?: string,
): string {
  const key = toCalendarDateKey(value)
  const date = key ? calendarDateKeyToUtcDate(key) : null
  if (!date) return ''

  return new Intl.DateTimeFormat(locale, {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    ...options,
    // Calendar keys must never be reinterpreted in the browser's zone.
    timeZone: 'UTC',
  }).format(date)
}

export function isCalendarDateOverdue(
  value: string | null | undefined,
  todayKey: string = localCalendarDateKey(),
): boolean {
  const dueKey = toCalendarDateKey(value)
  return dueKey !== null && isCalendarDateKey(todayKey) && dueKey < todayKey
}
