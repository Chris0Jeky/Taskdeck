import type { CaptureSourceValue, CaptureStatusValue } from '../../types/capture'

export function statusLabel(status: CaptureStatusValue): string {
  if (status === 0 || status === 'New') return 'New'
  if (status === 1 || status === 'Triaging') return 'Triaging'
  if (status === 2 || status === 'Triaged') return 'Triaged'
  if (status === 3 || status === 'ProposalCreated') return 'Ready for review'
  if (status === 4 || status === 'Converted') return 'Applied to board'
  if (status === 5 || status === 'Ignored') return 'Ignored'
  if (status === 6 || status === 'Failed') return 'Failed'
  return String(status)
}

export function statusBadgeVariant(status: CaptureStatusValue): 'primary' | 'warning' | 'success' | 'error' | 'default' {
  if (status === 0 || status === 'New') return 'primary'
  if (status === 1 || status === 'Triaging') return 'warning'
  if (status === 2 || status === 'Triaged') return 'warning'
  if (status === 3 || status === 'ProposalCreated') return 'warning'
  if (status === 4 || status === 'Converted') return 'success'
  if (status === 5 || status === 'Ignored') return 'default'
  if (status === 6 || status === 'Failed') return 'error'
  return 'default'
}

export function sourceLabel(source: CaptureSourceValue): string {
  if (source === 0 || source === 'Typed') return 'Typed'
  if (source === 1 || source === 'Paste') return 'Paste'
  if (source === 2 || source === 'TranscriptPaste') return 'Transcript'
  if (source === 3 || source === 'Import') return 'Import'
  if (source === 4 || source === 'Voice') return 'Voice'
  if (source === 5 || source === 'MeetingIntegration') return 'Meeting'
  if (source === 6 || source === 'TranscriptFile') return 'Transcript (File)'
  if (source === 7 || source === 'MarkdownImport') return 'Markdown'
  if (source === 8 || source === 'WebClip') return 'Web Clip'
  if (source === 9 || source === 'ShareTarget') return 'Share Target'
  if (source === 10 || source === 'BrowserExtension') return 'Browser Extension'
  if (source === 11 || source === 'VsCodeExtension') return 'VS Code'
  return String(source)
}

/**
 * Where a capture stands from the USER's point of view (#1944).
 *
 * `statusLabel` names the server status; this names what the user's decision
 * did and what happens next, so a row can narrate itself. `undecided` is
 * reserved for a capture still waiting on a decision — that is the invariant
 * behind "a decided row can never render identically to an undecided one".
 *
 * `unknown` is deliberate rather than a fallback to `undecided`: an
 * out-of-contract status from the server is NOT a fresh capture, and claiming
 * it is would be the same class of lie this issue is about.
 *
 * `Triaged` and `ProposalCreated` are two different endings and must not share
 * a state. The server decides between them on whether triage produced anything:
 * `CaptureStatusPolicy` (backend `Domain/Enums/CaptureStatus.cs`) maps a
 * completed triage to `ProposalCreated` when a proposal was linked and to
 * `Triaged` when none was — the "triaged, nothing to propose" verdict, which is
 * a SUCCESS, not a failure. Sending a `Triaged` row to Review would send the
 * user after something that was never created, and neither Accept nor Reject is
 * live on that row to walk it back.
 */
export type CaptureRowState =
  | 'undecided'
  | 'sending'
  | 'nothingToPropose'
  | 'inReview'
  | 'applied'
  | 'rejected'
  | 'failed'
  | 'unknown'

export function captureRowState(status: CaptureStatusValue | undefined): CaptureRowState {
  if (status === undefined) return 'unknown'
  if (status === 0 || status === 'New') return 'undecided'
  if (status === 1 || status === 'Triaging') return 'sending'
  if (status === 2 || status === 'Triaged') return 'nothingToPropose'
  if (status === 3 || status === 'ProposalCreated') return 'inReview'
  if (status === 4 || status === 'Converted') return 'applied'
  if (status === 5 || status === 'Ignored') return 'rejected'
  if (status === 6 || status === 'Failed') return 'failed'
  return 'unknown'
}

export function canMutateSelection(status: CaptureStatusValue | undefined): boolean {
  if (status === undefined) {
    return false
  }

  return status === 0 ||
    status === 'New' ||
    status === 6 ||
    status === 'Failed'
}

export function triageButtonLabel(
  status: CaptureStatusValue | undefined,
  triagePollingItemId: string | null,
  selectedItemId: string | null,
): string {
  if (status === undefined) {
    return 'Start Triage'
  }

  const label = statusLabel(status)
  if (label === 'Triaging') {
    return triagePollingItemId === selectedItemId
      ? 'Triaging (checking...)'
      : 'Triaging...'
  }

  if (label === 'Ready for review' || label === 'Triaged') {
    return 'Triage Complete'
  }

  if (label === 'Applied to board') {
    return 'Converted'
  }

  if (label === 'Failed') {
    return 'Retry Triage'
  }

  return 'Start Triage'
}
