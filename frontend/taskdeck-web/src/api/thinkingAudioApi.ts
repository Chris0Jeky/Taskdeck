import http from './http'
import type { MemoryStatus } from '../types/workspaceInsights'

export interface ThinkingAudio {
  id: string; revision: number; captureId: string; sourceAssetId: string; questionHash: string
  fileName: string; mediaType: string; byteSize: number; contentHash: string; originalEvidence: string
  representationId: string | null; confirmedMemoryId: string | null
  writtenVersions: { id: string; text: string; quality: string; supersededById: string | null }[]
}
const questionPath = (board: string, card: string, layer: string) => `/thinking-audio/questions/${board}/${card}/${layer}`
export const thinkingAudioApi = {
  async get(board: string, card: string, layer: string): Promise<ThinkingAudio | null> {
    return (await http.get<ThinkingAudio | null>(questionPath(board, card, layer), { skipRetry: true, timeout: 15000 })).data || null
  },
  async upload(board: string, card: string, layer: string, expectedDeckRevision: number, uploadId: string, file: File): Promise<ThinkingAudio> {
    return (await http.post<ThinkingAudio>(questionPath(board, card, layer), file, {
      params: { uploadId, expectedDeckRevision, byteSize: file.size, fileName: file.name },
      headers: { 'Content-Type': file.type }, skipRetry: true, timeout: 60000,
    })).data
  },
  async write(id: string, expectedRevision: number, text: string): Promise<ThinkingAudio> {
    return (await http.put<ThinkingAudio>(`/thinking-audio/${id}/written-version`, { expectedRevision, text }, { skipRetry: true, timeout: 30000 })).data
  },
  async confirm(id: string, expectedRevision: number, expectedDeckRevision: number, representationId: string, status: MemoryStatus): Promise<ThinkingAudio> {
    return (await http.post<ThinkingAudio>(`/thinking-audio/${id}/confirm`, { expectedRevision, expectedDeckRevision, representationId, status }, { skipRetry: true, timeout: 30000 })).data
  },
  async original(id: string): Promise<Blob> {
    return (await http.get<Blob>(`/thinking-audio/${id}/original`, { responseType: 'blob', skipRetry: true, timeout: 30000 })).data
  },
}
