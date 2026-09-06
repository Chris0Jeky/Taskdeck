/**
 * Format an operation action for recorded-operation fallback copy.
 *
 * Backend action names are wire values, so presentation code must keep the
 * transformation pure and must not rewrite the stored operation itself.
 */
export function formatRecordedOperationActionLabel(actionType: string): string {
  return actionType
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim()
}
