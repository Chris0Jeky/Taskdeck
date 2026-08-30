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

/**
 * A proposal's structural revision fields, so this module stays dependency-free
 * and usable from composables, views and specs alike.
 */
interface RevisionIdentityFields {
  latestRevisionId?: string | null
  approvedRevisionId?: string | null
}

/**
 * The revision a review surface is effectively rendering — the identity its
 * cached diff and revision state are keyed on (#2215 B, review round 2).
 *
 * `latestRevisionId` is a PendingReview-ONLY value on the wire:
 * `AutomationProposalService.BuildEffectiveProposalDto` sets it to
 * `Status == PendingReview ? effectiveRevision?.Id : null`. So approving a
 * revised proposal moves it from `rev-X` to `null` in the very next read, even
 * though that revision is still exactly what Apply will execute — it has simply
 * been pinned into `approvedRevisionId` instead. Reading `latestRevisionId`
 * alone therefore reports a revision change on every approval.
 */
export function proposalRevisionIdentity(
  proposal: RevisionIdentityFields | null | undefined,
): string | null {
  if (!proposal) return null
  return proposal.latestRevisionId ?? proposal.approvedRevisionId ?? null
}

/**
 * Whether the revision under a rendered surface genuinely MOVED — that is,
 * whether a cached diff or revision state computed for `previousIdentity` is
 * now stale.
 *
 * The asymmetry is deliberate. A null identity becoming `rev-X` is a real
 * change (the first revision landing under an open pane). `rev-X` becoming null
 * never is: revisions are append-only, so an identity can only reach null by
 * the proposal leaving PendingReview — and of the exits only `Approve` pins a
 * replacement in `approvedRevisionId`, while `Reject` / `Expire` / `Dismiss`
 * pin nothing. Treating that as a revision change made a decision taken
 * elsewhere wipe an open diff, instead of letting it convert to the
 * decision-time stored presentation.
 */
export function proposalRevisionMoved(
  previousIdentity: string | null,
  nextIdentity: string | null,
): boolean {
  if (previousIdentity === null && nextIdentity === null) return false
  if (proposalIdsEqual(previousIdentity, nextIdentity)) return false
  if (nextIdentity === null) return false
  return true
}
