import http, { type BoardReadOptions } from './http'
import type { CardDetachPreview, Card, CardCaptureProvenance, CreateCardDto, UpdateCardDto, MoveCardDto } from '../types/board'

export const cardsApi = {
  async getParticipants(boardId: string): Promise<import('../types/board').BoardParticipant[]> {
    const { data } = await http.get(`/boards/${boardId}/participants`, { skipRetry: true })
    return data
  },
  async replaceAssignments(boardId: string, cardId: string, userIds: string[], expectedUpdatedAt: string): Promise<Card> {
    const { data } = await http.put<Card>(`/boards/${boardId}/cards/${cardId}/assignments`,
      { userIds, expectedUpdatedAt }, { skipRetry: true })
    return data
  },
  async previewDetach(boardId: string, cardId: string): Promise<CardDetachPreview> {
    const { data } = await http.get<CardDetachPreview>(`/boards/${boardId}/cards/${cardId}/detach-preview`, { skipRetry: true })
    return data
  },
  async getCard(boardId: string, cardId: string): Promise<Card> {
    const { data } = await http.get<Card>(`/boards/${boardId}/cards/${cardId}`, { skipRetry: true })
    return data
  },
  async getArchivedCards(boardId: string): Promise<Card[]> {
    const { data } = await http.get<Card[]>(`/boards/${boardId}/cards/archived`, { skipRetry: true })
    return data
  },

  async setArchived(boardId: string, cardId: string, archived: boolean, expectedUpdatedAt: string, expectedChildrenFingerprint?: string): Promise<Card> {
    const { data } = await http.post<Card>(
      `/boards/${boardId}/cards/${cardId}/${archived ? 'archive' : 'restore'}`,
      { expectedUpdatedAt, expectedChildrenFingerprint }, { skipRetry: true },
    )
    return data
  },

  async getCards(
    boardId: string,
    params?: { search?: string; labelId?: string; columnId?: string },
    options?: BoardReadOptions,
  ): Promise<Card[]> {
    const searchParams = new URLSearchParams()
    if (params?.search) searchParams.append('search', params.search)
    if (params?.labelId) searchParams.append('labelId', params.labelId)
    if (params?.columnId) searchParams.append('columnId', params.columnId)

    const url = `/boards/${boardId}/cards?${searchParams}`
    const { data } = options ? await http.get<Card[]>(url, options) : await http.get<Card[]>(url)
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

  async deleteCard(boardId: string, cardId: string, confirmation?: CardDetachPreview): Promise<void> {
    await http.delete(`/boards/${boardId}/cards/${cardId}`, { params: confirmation ? { expectedUpdatedAt: confirmation.expectedUpdatedAt, expectedChildrenFingerprint: confirmation.expectedChildrenFingerprint } : undefined, skipRetry: true })
  },

  async getCardProvenance(boardId: string, cardId: string): Promise<CardCaptureProvenance | null> {
    try {
      // A 404 here is an expected part of the contract — manual cards have no
      // capture provenance. Mark it expected so the http interceptor does not
      // log it as an API error (issue #680 console/Sentry noise).
      const { data } = await http.get<CardCaptureProvenance>(
        `/boards/${boardId}/cards/${cardId}/provenance`,
        { expectedStatuses: [404] },
      )
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
