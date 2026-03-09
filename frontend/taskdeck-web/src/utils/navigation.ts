export function sanitizeInternalRedirect(
  redirect: string | null | undefined,
  fallback = '/workspace/home'
): string {
  if (!redirect) return fallback
  if (!redirect.startsWith('/')) return fallback
  if (redirect.startsWith('//')) return fallback
  if (redirect.includes('\r') || redirect.includes('\n')) return fallback
  return redirect
}

export function normalizePathname(pathname: string): string {
  const normalized = pathname.replace(/\/+$/, '')
  return normalized.length > 0 ? normalized : '/'
}

export function isAuthRoutePath(pathname: string): boolean {
  const normalized = normalizePathname(pathname)
  return normalized === '/login' || normalized === '/register'
}

export function normalizeBoardIdQueryParam(
  candidate: string | null | undefined | ReadonlyArray<string | null | undefined>,
): string {
  if (Array.isArray(candidate)) {
    return normalizeBoardIdQueryParam(candidate[0])
  }

  return typeof candidate === 'string' ? candidate.trim() : ''
}
