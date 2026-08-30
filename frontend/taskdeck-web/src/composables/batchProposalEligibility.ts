import type { Proposal } from '../types/automation'
import { proposalIdsEqual } from '../utils/proposalIdentity'

/**
 * The shared, fail-closed gates behind Paper's two batch surfaces — batch approve
 * (`useBatchApproveProposals`) and batch execute (`useBatchExecuteProposals`).
 *
 * They live here rather than in either composable because #1307 AC3 scopes BOTH halves to the same
 * class of work ("eligible low-risk, create-card-only proposals … batch approve, then batch
 * execute"). Two private copies of that rule drift, and the drift is silent and one-directional:
 * whichever surface loosens first starts admitting proposals the other refuses, and a bulk apply is
 * the worse place to find out. The two composables differ ONLY on the status axis — PendingReview
 * for approve, Approved for execute — and each keeps its own status predicate for that reason.
 *
 * Every gate is deliberately stricter than the general display normalizers: an unknown or
 * unrecognised wire value is never read as Low. Eligibility here is presentation-only; the server
 * repeats board access, status, policy, and the approved-revision pin authoritatively for every
 * item in the request.
 */

/**
 * The most operations a single proposal may carry to be bulk-eligible. A batch is a decision made
 * without opening each proposal, so the per-proposal blast radius has to stay small enough to be
 * summarised in a row.
 */
export const MAX_BATCH_OPERATION_COUNT = 5

/** True only for an exact Low risk level. An unknown wire value is never Low. */
export function isExactLowRisk(risk: Proposal['riskLevel']): boolean {
  return risk === 0 || (
    typeof risk === 'string' && risk.toLowerCase() === 'low'
  )
}

/** True when the proposal belongs to this reviewer. A null viewer owns nothing. */
export function isOwnBatchProposal(proposal: Proposal, currentUserId: string | null): boolean {
  return !!currentUserId && proposalIdsEqual(proposal.requestedByUserId, currentUserId)
}

/**
 * True when the proposal is neither expired nor snoozed into the future. An unparseable expiry or
 * defer instant fails closed rather than being treated as absent.
 */
export function isLiveAndNotDeferred(proposal: Proposal, nowMs: number): boolean {
  if (proposal.isExpired === true) return false

  const expiresAt = new Date(proposal.expiresAt).getTime()
  if (!Number.isFinite(expiresAt) || expiresAt <= nowMs) return false

  if (proposal.deferredUntil) {
    const deferredUntil = new Date(proposal.deferredUntil).getTime()
    if (!Number.isFinite(deferredUntil) || deferredUntil > nowMs) return false
  }

  return true
}

/**
 * True when the proposal's whole operation set is a small number of card creations.
 *
 * Creation is the one action whose bulk blast radius is bounded by inspection: it touches nothing
 * that already exists. An archive, a move, or an update in a bulk action can change or hide work the
 * reviewer never looked at, which is exactly the decision a batch is unsuited to.
 */
export function isBoundedCreateCardOnly(operations: Proposal['operations']): boolean {
  return Array.isArray(operations) &&
    operations.length > 0 &&
    operations.length <= MAX_BATCH_OPERATION_COUNT &&
    operations.every(
      (operation) =>
        typeof operation.actionType === 'string' &&
        typeof operation.targetType === 'string' &&
        operation.actionType.toLowerCase() === 'create' &&
        operation.targetType.toLowerCase() === 'card',
    )
}
