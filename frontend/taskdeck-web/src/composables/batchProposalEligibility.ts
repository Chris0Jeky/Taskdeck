import type { Proposal } from '../types/automation'
import { proposalIdsEqual } from '../utils/proposalIdentity'

/**
 * Eligibility helpers for Paper's batch approval and execution surfaces.
 *
 * Under #1307 D-4 (2026-09-06), batch approve remains own, Low-risk, and bounded to card
 * creations. Batch execute accepts live Approved proposals of any risk and operation shape;
 * board-less execution still requires ownership. Both retain live/deferred checks.
 *
 * Risk gates used for approval are stricter than display normalizers: an unrecognised value
 * is never read as Low. Eligibility here is presentation-only; the server
 * repeats board access, status, policy, and the approved-revision pin authoritatively for every
 * item in the request.
 */

/**
 * The most operations a single proposal may carry to be batch-approval eligible.
 * Batch execution has no per-proposal operation-count restriction.
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
 * reviewer never looked at, so this gate belongs to batch approval, not execution of
 * proposals that have already been individually approved.
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
