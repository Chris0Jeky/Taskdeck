export function sanitizeInternalRedirect(
  redirect: string | null | undefined,
  fallback = '/workspace/boards'
): string {
  if (!redirect) return fallback
  if (!redirect.startsWith('/')) return fallback
  if (redirect.startsWith('//')) return fallback
  if (redirect.includes('\r') || redirect.includes('\n')) return fallback
  return redirect
}
