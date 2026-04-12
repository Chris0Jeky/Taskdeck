export interface MarkdownImportRequest {
  fileName: string
  content: string
  boardId?: string | null
}

export interface WebClipImportRequest {
  url: string
  content: string
  title?: string | null
  boardId?: string | null
}

export interface NoteImportItemResult {
  captureItemId: string
  textExcerpt: string
  sourceType: string
  sourceRef: string | null
}

export interface NoteImportResult {
  itemsCreated: number
  items: NoteImportItemResult[]
}
