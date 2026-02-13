/**
 * Builds a URL query string from an object of optional filter parameters.
 * Ignores null and undefined values.
 *
 * @param filters An object whose non-nullish values will be serialised as query parameters.
 * @returns A query string prefixed with '?' or an empty string when there are no parameters.
 */
export function buildQueryString(filters?: Record<string, string | number | boolean | undefined | null>): string {
  if (!filters) {
    return ''
  }

  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(filters)) {
    if (value !== undefined && value !== null) {
      params.set(key, String(value))
    }
  }

  const query = params.toString()
  return query.length > 0 ? `?${query}` : ''
}
