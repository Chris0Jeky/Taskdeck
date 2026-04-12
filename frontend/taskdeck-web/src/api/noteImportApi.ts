import http from './http'
import type {
  MarkdownImportRequest,
  WebClipImportRequest,
  NoteImportResult,
} from '../types/note-import'

export const noteImportApi = {
  async importMarkdown(request: MarkdownImportRequest): Promise<NoteImportResult> {
    const { data } = await http.post<NoteImportResult>('/import/notes/markdown', request)
    return data
  },

  async importWebClip(request: WebClipImportRequest): Promise<NoteImportResult> {
    const { data } = await http.post<NoteImportResult>('/import/notes/webclip', request)
    return data
  },
}
