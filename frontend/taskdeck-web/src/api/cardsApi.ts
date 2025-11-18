import http from './http'
import type { Card, CreateCardDto, UpdateCardDto, MoveCardDto } from '../types/board'

export const cardsApi = {
  async getCards(boardId: string, params?: { search?: string; labelId?: string; columnId?: string }): Promise<Card[]> {
    const searchParams = new URLSearchParams()
    if (params?.search) searchParams.append('search', params.search)
    if (params?.labelId) searchParams.append('labelId', params.labelId)
    if (params?.columnId) searchParams.append('columnId', params.columnId)

    const { data } = await http.get<Card[]>(`/boards/${boardId}/cards?${searchParams}`)
    return data
  },

  async createCard(boardId: string, card: CreateCardDto): Promise<Card> {
    const { data } = await http.post<Card>(`/boards/${boardId}/cards`, card)
    return data
  },

  async updateCard(boardId: string, cardId: string, card: UpdateCardDto): Promise<Card> {
    const { data } = await http.patch<Card>(`/boards/${boardId}/cards/${cardId}`, card)
    return data
  },

  async moveCard(boardId: string, cardId: string, move: MoveCardDto): Promise<Card> {
    const { data } = await http.post<Card>(`/boards/${boardId}/cards/${cardId}/move`, move)
    return data
  },

  async deleteCard(boardId: string, cardId: string): Promise<void> {
    await http.delete(`/boards/${boardId}/cards/${cardId}`)
  },
}
