import http from './http'
import type { ImportResult } from '../types/export-import'

export const exportImportApi = {
  async exportBoard(boardId: string, userId: string): Promise<Blob> {
    const { data } = await http.get(`/export/boards/${boardId}?userId=${userId}`, {
      responseType: 'blob',
    })
    return data as Blob
  },

  async exportBoardJson(boardId: string, userId: string): Promise<unknown> {
    const { data } = await http.get(`/export/boards/${boardId}/json?userId=${userId}`)
    return data
  },

  async importBoard(payload: unknown, userId: string): Promise<ImportResult> {
    const { data } = await http.post<ImportResult>(`/import/boards?userId=${userId}`, payload)
    return data
  },

  async importBoardJson(json: string, userId: string): Promise<ImportResult> {
    const { data } = await http.post<ImportResult>(`/import/boards/json?userId=${userId}`, json, {
      headers: { 'Content-Type': 'application/json' },
    })
    return data
  },
}
