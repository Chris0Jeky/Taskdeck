import { canMutateSelection } from '../components/inbox/inboxUtils'
import type { Board } from '../types/board'
import type { CaptureItemSummary, CaptureStatusValue } from '../types/capture'

/**
 * Statuses in which the server itself refuses a suggestion text edit.
 *
 * `CaptureService.IsSuggestionEditableStatus` allows New, Failed and Triaged,
 * and `Triaging` is a transient the capture is only passing through. These are
 * the settled refusals used when a held correction meets a refreshed row.
 */
export function isPastEditing(status: CaptureStatusValue): boolean {
  return status === 3 || status === 'ProposalCreated' ||
    status === 4 || status === 'Converted' ||
    status === 5 || status === 'Ignored'
}

/** Use the same text the row shows when a correction needs a durable label. */
export function captureLabel(item: CaptureItemSummary): string {
  const excerpt = typeof item.textExcerpt === 'string' ? item.textExcerpt.trim() : ''
  return excerpt || item.id
}

/** Only an explicit server-side false removes write capability from a board. */
export function isBoardWritable(board: Board): boolean {
  return board.canWrite !== false
}

/** A completed, proposal-less triage can be corrected and explicitly retried. */
export function isTriagedWithoutProposal(item: CaptureItemSummary): boolean {
  return item.status === 2 || item.status === 'Triaged'
}

/**
 * Edit capability is server-gated, with the explicit D-13 exception for a
 * completed triage that has no proposal yet.
 */
export function canEditCapture(item: CaptureItemSummary): boolean {
  if (item.canEditSuggestion === false) return false
  return canMutateSelection(item.status) ||
    (isTriagedWithoutProposal(item) && item.canEditSuggestion === true)
}

/** Accept is available for ordinary mutable captures and proposal-less triage. */
export function canTriageCapture(item: CaptureItemSummary): boolean {
  return canMutateSelection(item.status) || isTriagedWithoutProposal(item)
}

/** Keep/reject disposition is only available while the capture is mutable. */
export function canSetDisposition(item: CaptureItemSummary): boolean {
  return canMutateSelection(item.status)
}
