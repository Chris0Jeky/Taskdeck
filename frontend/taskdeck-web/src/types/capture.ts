export type CaptureStatus =
  | 'New'
  | 'Triaging'
  | 'Triaged'
  | 'ProposalCreated'
  | 'Converted'
  | 'Ignored'
  | 'Failed'

export type CaptureStatusValue = CaptureStatus | number

export const TRIAGE_TERMINAL_STATUSES: readonly CaptureStatusValue[] = [
  'Triaged',
  2,
  'ProposalCreated',
  3,
  'Converted',
  4,
  'Ignored',
  5,
  'Failed',
  6,
]

export function isTriageTerminalStatus(status: CaptureStatusValue): boolean {
  return TRIAGE_TERMINAL_STATUSES.includes(status)
}

export type CaptureSource =
  | 'Typed'
  | 'Paste'
  | 'TranscriptPaste'
  | 'Import'
  | 'Voice'
  | 'MeetingIntegration'

export type CaptureSourceValue = CaptureSource | number

export interface CaptureItemSummary {
  id: string
  userId: string
  boardId: string | null
  status: CaptureStatusValue
  source: CaptureSourceValue
  textExcerpt: string
  createdAt: string
  processedAt: string | null
}

export interface CaptureProvenance {
  captureItemId: string
  triageRunId: string | null
  proposalId: string | null
  promptVersion: string | null
}

export interface CaptureItem extends CaptureItemSummary {
  rawText: string
  retryCount: number
  provenance?: CaptureProvenance | null
}

export interface CreateCaptureItemDto {
  boardId: string | null
  text: string
  source?: CaptureSource | null
  titleHint?: string | null
  externalRef?: string | null
}

export interface CaptureListQuery {
  status?: CaptureStatus
  boardId?: string
  limit?: number
}

export interface CaptureTriageEnqueueResult {
  id: string
  status: CaptureStatusValue
  alreadyTriaging: boolean
}

export type BatchTriageAction = 'triage' | 'ignore' | 'cancel'

export interface BatchTriageItemAction {
  itemId: string
  action: BatchTriageAction
}

export interface BatchTriageItemResult {
  itemId: string
  success: boolean
  errorCode?: string | null
  errorMessage?: string | null
}

export interface BatchTriageResult {
  total: number
  succeeded: number
  failed: number
  results: BatchTriageItemResult[]
}

export interface UpdateCaptureSuggestionDto {
  text: string
  titleHint?: string | null
}
