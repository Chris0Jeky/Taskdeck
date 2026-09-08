import http from './http'
import type { ThinkingDeck, ThinkingLayer } from '../types/thinking'
import type { Memory, MemoryStatus } from '../types/workspaceInsights'

export const thinkingApi = {
  async getAnswer(boardId: string, cardId: string, layerId: string): Promise<Memory | null> {
    const { data } = await http.get<Memory | null>(`/boards/${boardId}/cards/${cardId}/thinking/questions/${layerId}/answer`)
    return data || null
  },
  async answer(boardId: string, cardId: string, layerId: string, expectedRevision: number, text: string, status: MemoryStatus): Promise<Memory> {
    return (await http.post<Memory>(`/boards/${boardId}/cards/${cardId}/thinking/questions/${layerId}/answer`, { expectedRevision, text, status })).data
  },
  async get(boardId: string, cardId: string): Promise<ThinkingDeck> {
    return (await http.get<ThinkingDeck>(`/boards/${boardId}/cards/${cardId}/thinking`)).data
  },
  async save(boardId: string, cardId: string, expectedRevision: number, layers: ThinkingLayer[]): Promise<ThinkingDeck> {
    return (await http.put<ThinkingDeck>(`/boards/${boardId}/cards/${cardId}/thinking`, { expectedRevision, layers })).data
  },
}
