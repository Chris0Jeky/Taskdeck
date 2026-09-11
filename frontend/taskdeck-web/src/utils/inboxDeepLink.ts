/**
 * Inbox deep-link parsing and response classification.
 *
 * These rules stay independent of the Inbox composable so hash and not-found
 * behavior can be checked without mounting the whole orchestration surface.
 */

export function getCaptureIdFromHash(hash: string): string | null {
  if (!hash.startsWith('#capture-')) {
    return null
  }

  const rawId = hash.slice('#capture-'.length).trim()
  if (!rawId) {
    return null
  }

  try {
    return decodeURIComponent(rawId)
  } catch {
    return null
  }
}

export function isHttpNotFound(error: unknown): boolean {
  const candidate = error as { response?: { status?: number; data?: { errorCode?: string } } } | null
  return candidate?.response?.status === 404 || candidate?.response?.data?.errorCode === 'NotFound'
}
