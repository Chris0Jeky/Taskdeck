import http from './http'
import type { CardComment, CreateCardCommentDto, UpdateCardCommentDto } from '../types/comments'

export const cardCommentsApi = {
  async getComments(boardId: string, cardId: string): Promise<CardComment[]> {
    const { data } = await http.get<CardComment[]>(`/boards/${boardId}/cards/${cardId}/comments`)
    return data
  },

  async createComment(boardId: string, cardId: string, comment: CreateCardCommentDto): Promise<CardComment> {
    const { data } = await http.post<CardComment>(`/boards/${boardId}/cards/${cardId}/comments`, comment)
    return data
  },

  async updateComment(
    boardId: string,
    cardId: string,
    commentId: string,
    comment: UpdateCardCommentDto
  ): Promise<CardComment> {
    const { data } = await http.patch<CardComment>(
      `/boards/${boardId}/cards/${cardId}/comments/${commentId}`,
      comment
    )
    return data
  },

  async deleteComment(boardId: string, cardId: string, commentId: string): Promise<void> {
    await http.delete(`/boards/${boardId}/cards/${cardId}/comments/${commentId}`)
  },
}
