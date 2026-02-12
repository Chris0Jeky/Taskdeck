import http from './http'
import type { ImportResult } from '../types/export-import'

export const exportImportApi = {
  async exportBoard(boardId: string, userId: string): Promise<unknown> {
    const queryUserId = encodeURIComponent(userId)
    const { data } = await http.get(`/export/boards/${boardId}?userId=${queryUserId}`)
    return data
  },

  async exportBoardJson(boardId: string, userId: string): Promise<unknown> {
    const queryUserId = encodeURIComponent(userId)
    const { data } = await http.get(`/export/boards/${boardId}/json?userId=${queryUserId}`)
    return data
  },

  async importBoard(payload: unknown, userId: string): Promise<ImportResult> {
    const queryUserId = encodeURIComponent(userId)
    const { data } = await http.post<ImportResult>(`/import/boards?userId=${queryUserId}`, payload)
    return data
  },

  async importBoardJson(json: string, userId: string): Promise<ImportResult> {
    const queryUserId = encodeURIComponent(userId)
    const parsed = JSON.parse(json)
    const { data } = await http.post<ImportResult>(`/import/boards/json?userId=${queryUserId}`, parsed, {
      headers: { 'Content-Type': 'application/json' },
    })
    return data
  },
}
