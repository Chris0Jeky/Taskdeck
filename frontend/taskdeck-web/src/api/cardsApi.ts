import http from './http'
import type { Card, CardCaptureProvenance, CreateCardDto, UpdateCardDto, MoveCardDto } from '../types/board'

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

  async getCardProvenance(boardId: string, cardId: string): Promise<CardCaptureProvenance | null> {
    try {
      const { data } = await http.get<CardCaptureProvenance>(`/boards/${boardId}/cards/${cardId}/provenance`)
      return data
    } catch (e: unknown) {
      const candidate = e as { response?: { status?: number; data?: { message?: string } } } | null
      if (
        candidate?.response?.status === 404 &&
        typeof candidate.response.data?.message === 'string' &&
        candidate.response.data.message.startsWith('Capture provenance not found')
      ) {
        // Manual cards have no capture provenance — treat only that specific absence as
        // empty state, not an error. Other 404s (e.g. card not found in board) are rethrown
        // so callers can surface them as genuine errors.
        return null
      }
      throw e
    }
  },
}
