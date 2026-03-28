/**
 * Card comment operations: fetch, create, update, delete comments.
 */
import { cardCommentsApi } from '../../api/cardCommentsApi'
import type { CardComment, CreateCardCommentDto, UpdateCardCommentDto } from '../../types/comments'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'

export function createCardCommentActions(state: BoardState, helpers: BoardHelpers) {
  function getCardComments(cardId: string): CardComment[] {
    return state.cardCommentsByCardId.value[cardId] ?? []
  }

  async function fetchCardComments(boardId: string, cardId: string) {
    if (helpers.isDemoMode) return []
    try {
      const comments = await cardCommentsApi.getComments(boardId, cardId)
      state.cardCommentsByCardId.value = {
        ...state.cardCommentsByCardId.value,
        [cardId]: comments,
      }
      return comments
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to fetch card comments')
      throw e
    }
  }

  async function createCardComment(boardId: string, cardId: string, dto: CreateCardCommentDto) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const createdComment = await cardCommentsApi.createComment(boardId, cardId, dto)
      const existingComments = state.cardCommentsByCardId.value[cardId] ?? []
      state.cardCommentsByCardId.value = {
        ...state.cardCommentsByCardId.value,
        [cardId]: [...existingComments, createdComment].sort(
          (left, right) =>
            new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime(),
        ),
      }

      helpers.toast.success('Comment added')
      return createdComment
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to create card comment')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function updateCardComment(
    boardId: string,
    cardId: string,
    commentId: string,
    dto: UpdateCardCommentDto,
  ) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const updatedComment = await cardCommentsApi.updateComment(boardId, cardId, commentId, dto)
      const existingComments = state.cardCommentsByCardId.value[cardId] ?? []
      state.cardCommentsByCardId.value = {
        ...state.cardCommentsByCardId.value,
        [cardId]: existingComments.map((comment) =>
          comment.id === commentId ? updatedComment : comment,
        ),
      }

      helpers.toast.success('Comment updated')
      return updatedComment
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to update card comment')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function deleteCardComment(boardId: string, cardId: string, commentId: string) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      await cardCommentsApi.deleteComment(boardId, cardId, commentId)
      const existingComments = state.cardCommentsByCardId.value[cardId] ?? []
      state.cardCommentsByCardId.value = {
        ...state.cardCommentsByCardId.value,
        [cardId]: existingComments.filter((comment) => comment.id !== commentId),
      }
      helpers.toast.success('Comment deleted')
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to delete card comment')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  return {
    getCardComments,
    fetchCardComments,
    createCardComment,
    updateCardComment,
    deleteCardComment,
  }
}
