import http from './http'
import type { ImportResult } from '../types/export-import'

function encodePathSegment(value: string): string {
  return encodeURIComponent(value)
}

export const exportImportApi = {
  async exportBoard(boardId: string): Promise<unknown> {
    const pathBoardId = encodePathSegment(boardId)
    const { data } = await http.get(`/export/boards/${pathBoardId}`)
    return data
  },

  async exportBoardJson(boardId: string): Promise<unknown> {
    const pathBoardId = encodePathSegment(boardId)
    const { data } = await http.get(`/export/boards/${pathBoardId}/json`)
    return data
  },

  async importBoard(payload: unknown): Promise<ImportResult> {
    const { data } = await http.post<ImportResult>('/import/boards', payload)
    return data
  },

  async importBoardJson(json: string): Promise<ImportResult> {
    const parsed = JSON.parse(json)
    const { data } = await http.post<ImportResult>('/import/boards/json', parsed, {
      headers: { 'Content-Type': 'application/json' },
    })
    return data
  },
}
