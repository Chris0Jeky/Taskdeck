export type QueryValue = string | number | boolean | null | undefined

export function buildQueryParams(params?: Record<string, QueryValue>): URLSearchParams {
  const searchParams = new URLSearchParams()
  if (!params) {
    return searchParams
  }

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') {
      continue
    }
    searchParams.append(key, String(value))
  }

  return searchParams
}
