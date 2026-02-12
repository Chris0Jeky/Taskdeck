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
