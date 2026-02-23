export type CaptureStatus =
  | 'New'
  | 'Triaging'
  | 'Triaged'
  | 'ProposalCreated'
  | 'Converted'
  | 'Ignored'
  | 'Failed'

export type CaptureStatusValue = CaptureStatus | number

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

export interface CaptureItem extends CaptureItemSummary {
  rawText: string
  retryCount: number
}

export interface CreateCaptureItemDto {
  boardId: string | null
  text: string
  source?: string | null
  titleHint?: string | null
  externalRef?: string | null
}

export interface CaptureListQuery {
  status?: string
  boardId?: string
  limit?: number
}
