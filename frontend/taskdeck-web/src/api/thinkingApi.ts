import http from './http'
import type { ThinkingDeck, ThinkingLayer } from '../types/thinking'

export const thinkingApi = {
  async get(boardId: string, cardId: string): Promise<ThinkingDeck> {
    return (await http.get<ThinkingDeck>(`/boards/${boardId}/cards/${cardId}/thinking`)).data
  },
  async save(boardId: string, cardId: string, expectedRevision: number, layers: ThinkingLayer[]): Promise<ThinkingDeck> {
    return (await http.put<ThinkingDeck>(`/boards/${boardId}/cards/${cardId}/thinking`, { expectedRevision, layers })).data
  },
}
