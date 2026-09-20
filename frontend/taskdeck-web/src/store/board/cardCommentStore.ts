/**
 * Card comment operations: fetch, create, update, delete comments.
 */
import { cardCommentsApi } from '../../api/cardCommentsApi'
import type { CardComment, CreateCardCommentDto, UpdateCardCommentDto } from '../../types/comments'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'

interface CommentCacheVisit {
  boardId: string
  cache: Record<string, CardComment[]>
}

interface CommentMutationRequest {
  key: string
  version: number
}

export function createCardCommentActions(state: BoardState, helpers: BoardHelpers) {
  // Reads and writes share one per-card cache. Keep their ordering metadata in
  // the store closure rather than exposing transport generations in UI callers.
  const readVersionByCardId = new Map<string, number>()
  const mutationVersionByCardId = new Map<string, number>()
  const mutationRequestVersionByCommentKey = new Map<string, number>()

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

  function captureCommentCacheVisit(boardId: string): CommentCacheVisit {
    return {
      boardId,
      cache: state.cardCommentsByCardId.value,
    }
  }

  function ownsCurrentCommentCache(visit: CommentCacheVisit) {
    const currentBoard = state.currentBoard?.value
    return (
      (currentBoard == null || currentBoard.id === visit.boardId) &&
      state.cardCommentsByCardId.value === visit.cache
    )
  }

  function beginCommentMutation(cardId: string, commentId: string): CommentMutationRequest {
    const key = `${cardId}:${commentId}`
    const version = (mutationRequestVersionByCommentKey.get(key) ?? 0) + 1
    mutationRequestVersionByCommentKey.set(key, version)
    return { key, version }
  }

  function isCurrentCommentMutation(request: CommentMutationRequest) {
    return mutationRequestVersionByCommentKey.get(request.key) === request.version
  }

  function getCardComments(cardId: string): CardComment[] {
    return state.cardCommentsByCardId.value[cardId] ?? []
  }

  async function fetchCardComments(boardId: string, cardId: string) {
    if (helpers.isDemoMode) return []
    const visit = captureCommentCacheVisit(boardId)
    const readVersion = nextReadVersion(cardId)
    const mutationVersion = currentMutationVersion(cardId)
    try {
      const comments = await cardCommentsApi.getComments(boardId, cardId)
      // A newer read owns the cache. A successful local mutation also
      // invalidates every snapshot that began before it, even when that older
      // request returns later. The payload is still returned to its caller.
      if (
        ownsCurrentCommentCache(visit) &&
        readVersionByCardId.get(cardId) === readVersion &&
        currentMutationVersion(cardId) === mutationVersion
      ) {
        // Mutate the per-card slot rather than replacing the cache container.
        // Board-detail commits and logout replace that container, so its identity
        // is the visit/session generation without invalidating same-visit writes.
        state.cardCommentsByCardId.value[cardId] = comments
      }
      return comments
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to fetch card comments')
      throw e
    }
  }

  async function createCardComment(boardId: string, cardId: string, dto: CreateCardCommentDto) {
    helpers.guardDemoMutation()
    const visit = captureCommentCacheVisit(boardId)
    try {
      state.loading.value = true
      state.error.value = null
      const createdComment = await cardCommentsApi.createComment(boardId, cardId, dto)
      if (ownsCurrentCommentCache(visit)) {
        markCommentMutation(cardId)
        const existingComments = state.cardCommentsByCardId.value[cardId] ?? []
        // A board refresh can commit the stable id before this response arrives.
        // Preserve that fresher object instead of appending a duplicate.
        if (!existingComments.some(comment => comment.id === createdComment.id)) {
          state.cardCommentsByCardId.value[cardId] = [...existingComments, createdComment].sort(
            (left, right) =>
              new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime(),
          )
        }
        helpers.toast.success('Comment added')
      }
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
    const visit = captureCommentCacheVisit(boardId)
    const mutationRequest = beginCommentMutation(cardId, commentId)
    try {
      state.loading.value = true
      state.error.value = null
      const updatedComment = await cardCommentsApi.updateComment(boardId, cardId, commentId, dto)
      if (ownsCurrentCommentCache(visit) && isCurrentCommentMutation(mutationRequest)) {
        markCommentMutation(cardId)
        const existingComments = state.cardCommentsByCardId.value[cardId] ?? []
        state.cardCommentsByCardId.value[cardId] = existingComments.map((comment) =>
          comment.id === commentId ? updatedComment : comment,
        )
        helpers.toast.success('Comment updated')
      }
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
    const visit = captureCommentCacheVisit(boardId)
    const mutationRequest = beginCommentMutation(cardId, commentId)
    try {
      state.loading.value = true
      state.error.value = null
      await cardCommentsApi.deleteComment(boardId, cardId, commentId)
      if (ownsCurrentCommentCache(visit) && isCurrentCommentMutation(mutationRequest)) {
        markCommentMutation(cardId)
        const existingComments = state.cardCommentsByCardId.value[cardId] ?? []
        state.cardCommentsByCardId.value[cardId] = existingComments.filter(
          (comment) => comment.id !== commentId,
        )
        helpers.toast.success('Comment deleted')
      }
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
