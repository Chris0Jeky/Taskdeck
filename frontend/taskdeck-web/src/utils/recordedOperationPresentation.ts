/**
 * Format an operation action for recorded-operation fallback copy.
 *
 * Backend action names are wire values, so presentation code must keep the
 * transformation pure and must not rewrite the stored operation itself.
 */
export function formatRecordedOperationActionLabel(actionType: unknown): string {
  if (typeof actionType !== 'string') return ''
  const normalized = actionType.trim()
  if (!normalized) return ''

  const lowered = normalized.toLowerCase()
  if (lowered === 'archive-lifecycle') return 'archive'
  if (lowered === 'restore-lifecycle') return 'restore'
  return normalized
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim()
}
