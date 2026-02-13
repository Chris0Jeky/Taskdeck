/**
 * Builds a URL query string from an object of optional filter parameters.
 * Ignores null, undefined, and blank string values.
 *
 * @param filters An object whose supported values will be serialised as query parameters.
 * @returns A query string prefixed with '?' or an empty string when there are no parameters.
 */
export function buildQueryString<T extends object>(filters?: T | null): string {
  if (!filters) {
    return ''
  }

  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(filters as Record<string, unknown>)) {
    if (value === undefined || value === null) {
      continue
    }

    if (typeof value === 'string') {
      const trimmedValue = value.trim()
      if (trimmedValue.length === 0) {
        continue
      }

      params.set(key, trimmedValue)
      continue
    }

    if (typeof value === 'number' || typeof value === 'boolean') {
      params.set(key, String(value))
    }
  }

  const query = params.toString()
  return query.length > 0 ? `?${query}` : ''
}
