/**
 * Compare proposal identifiers without changing the value kept in state, sent
 * to the API, or rendered into DOM ids. Proposal ids are GUID-shaped and the
 * backend accepts either hex casing, so identity checks must do the same.
 *
 * Keep this proposal-specific: board, user, session, and operation identifiers
 * retain their existing contracts.
 */
export function proposalIdsEqual(
  left: string | null | undefined,
  right: string | null | undefined,
): boolean {
  if (typeof left !== 'string' || typeof right !== 'string') return false
  if (left.length === 0 || right.length === 0) return false
  return left.toLowerCase() === right.toLowerCase()
}
