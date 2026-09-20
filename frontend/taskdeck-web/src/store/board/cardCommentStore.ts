/**
 * Card comment operations: fetch, create, update, delete comments.
 */
import { cardCommentsApi } from '../../api/cardCommentsApi'
import type { CardComment, CreateCardCommentDto, UpdateCardCommentDto } from '../../types/comments'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'

export function createCardCommentActions(state: BoardState, helpers: BoardHelpers) {
  // Reads and writes share one per-card cache. Keep their ordering metadata in
  // the store closure rather than exposing transport generations in UI callers.
  const readVersionByCardId = new Map<string, number>()
  const mutationVersionByCardId = new Map<string, number>()

  function nextReadVersion(cardId: string) {
    const version = (readVersionByCardId.get(cardId) ?? 0) + 1
    readVersionByCardId.set(cardId, version)
    return version
  }

  function currentMutationVersion(cardId: string) {
    return mutationVersionByCardId.get(cardId) ?? 0
  }

  function markCommentMutation(cardId: string) {
    mutationVersionByCardId.set(cardId, currentMutationVersion(cardId) + 1)
  }

  function ownsCurrentCommentCache(boardId: string) {
    // Null preserves the existing pre-load/store-test convention. Optional
    // access keeps lightweight unit fixtures that predate currentBoard valid.
    const currentBoard = state.currentBoard?.value
    return currentBoard == null || currentBoard.id === boardId
  }

  function getCardComments(cardId: string): CardComment[] {
    return state.cardCommentsByCardId.value[cardId] ?? []
  }

  async function fetchCardComments(boardId: string, cardId: string) {
    if (helpers.isDemoMode) return []
    const readVersion = nextReadVersion(cardId)
    const mutationVersion = currentMutationVersion(cardId)
    try {
      const comments = await cardCommentsApi.getComments(boardId, cardId)
      // A newer read owns the cache. A successful local mutation also
      // invalidates every snapshot that began before it, even when that older
      // request returns later. The payload is still returned to its caller.
      if (
        ownsCurrentCommentCache(boardId) &&
        readVersionByCardId.get(cardId) === readVersion &&
        currentMutationVersion(cardId) === mutationVersion
      ) {
        state.cardCommentsByCardId.value = {
          ...state.cardCommentsByCardId.value,
          [cardId]: comments,
        }
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
      markCommentMutation(cardId)
      if (ownsCurrentCommentCache(boardId)) {
        const existingComments = state.cardCommentsByCardId.value[cardId] ?? []
        // A board refresh can commit the stable id before this response arrives.
        // Preserve that fresher object instead of appending a duplicate.
        if (!existingComments.some(comment => comment.id === createdComment.id)) {
          state.cardCommentsByCardId.value = {
            ...state.cardCommentsByCardId.value,
            [cardId]: [...existingComments, createdComment].sort(
              (left, right) =>
                new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime(),
            ),
          }
        }
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
      markCommentMutation(cardId)
      if (ownsCurrentCommentCache(boardId)) {
        const existingComments = state.cardCommentsByCardId.value[cardId] ?? []
        state.cardCommentsByCardId.value = {
          ...state.cardCommentsByCardId.value,
          [cardId]: existingComments.map((comment) =>
            comment.id === commentId ? updatedComment : comment,
          ),
        }
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
      markCommentMutation(cardId)
      if (ownsCurrentCommentCache(boardId)) {
        const existingComments = state.cardCommentsByCardId.value[cardId] ?? []
        state.cardCommentsByCardId.value = {
          ...state.cardCommentsByCardId.value,
          [cardId]: existingComments.filter((comment) => comment.id !== commentId),
        }
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
