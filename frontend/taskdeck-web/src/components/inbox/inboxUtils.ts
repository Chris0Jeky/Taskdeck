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
  return String(source)
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

export function canEditSuggestion(status: CaptureStatusValue | undefined): boolean {
  if (status === undefined) {
    return false
  }

  return status === 0 ||
    status === 'New' ||
    status === 2 ||
    status === 'Triaged' ||
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
