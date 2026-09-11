export interface ExportResult {
  boardId: string
  boardName: string
  exportedAt: string
  exportedBy: string
  data: unknown
}

export interface ImportValidation {
  isValid: boolean
  errors: ImportValidationError[]
  warnings: ImportValidationWarning[]
  entitySummary: ImportEntitySummary
}

export interface ImportValidationError {
  field: string
  message: string
  entityType: string
}

export interface ImportValidationWarning {
  field: string
  message: string
  entityType: string
}

export interface ImportEntitySummary {
  boards: number
  columns: number
  cards: number
  labels: number
}

export interface ImportResult {
  success: boolean
  boardId: string | null
  errorMessage: string | null
  columnsImported: number
  cardsImported: number
  labelsImported: number
}
export interface BoardImportPreview {
  board: { name: string; cards: Array<{ title: string; columnName: string; isArchived: boolean }>; [key: string]: unknown }
  cardCount: number
  columnCount: number
  sourceAssignees: Array<{ sourceKey: string; displayName: string; affectedCardCount: number }>
  me: { userId: string; displayName: string }
}
