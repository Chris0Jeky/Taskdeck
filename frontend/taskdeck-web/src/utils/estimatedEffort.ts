/** Estimates are optional whole minutes; zero is an explicit estimate. */
export const MAX_ESTIMATED_EFFORT_MINUTES = 1_000_000

export function formatEstimatedEffort(minutes: number | null | undefined): string {
  if (minutes == null) return 'Not estimated'
  const hours = Math.floor(minutes / 60)
  const remainder = minutes % 60
  return hours > 0 ? `${hours}h${remainder ? ` ${remainder}m` : ''}` : `${remainder}m`
}

export function estimatedEffortInputs(minutes: number | null | undefined) {
  return minutes == null
    ? { hours: '', minutes: '' }
    : { hours: String(Math.floor(minutes / 60)), minutes: String(minutes % 60) }
}

export function parseEstimatedEffort(hours: string, minutes: string): {
  value: number | null
  error: string | null
} {
  const hourText = hours.trim()
  const minuteText = minutes.trim()
  if (!hourText && !minuteText) return { value: null, error: null }
  if ((hourText && !/^\d+$/.test(hourText)) || (minuteText && !/^\d+$/.test(minuteText))) {
    return { value: null, error: 'Use whole, non-negative hours and minutes.' }
  }
  const hourValue = Number(hourText)
  const minuteValue = Number(minuteText)
  if (minuteValue > 59) return { value: null, error: 'Minutes must be between 0 and 59.' }
  const value = hourValue * 60 + minuteValue
  if (!Number.isSafeInteger(value) || value > MAX_ESTIMATED_EFFORT_MINUTES) {
    return { value: null, error: 'Estimated effort cannot exceed 16666h 40m.' }
  }
  return { value, error: null }
}
