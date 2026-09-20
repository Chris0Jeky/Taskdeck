/**
 * Card comment operations: fetch, create, update, delete comments.
 */
import { watch } from 'vue'
import { cardCommentsApi } from '../../api/cardCommentsApi'
import type { CardComment, CreateCardCommentDto, UpdateCardCommentDto } from '../../types/comments'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'

interface CommentCacheVisit {
  boardId: string
  cache: Record<string, CardComment[]>
  generation: number
}

class StaleBoardVisitError extends Error {
  constructor() {
    super('The board visit that queued this comment change has ended.')
    this.name = 'StaleBoardVisitError'
  }
}

export function createCardCommentActions(state: BoardState, helpers: BoardHelpers) {
  // Reads and writes share one per-card cache. Cache-container identity protects
  // ordinary reads, while a synchronous board-id generation distinguishes one
  // visit/session from A→B→A or logout→login. Same-board detail refreshes keep
  // that generation and may therefore receive a confirmed write into their new
  // cache container.
  const readVersionByCardId = new Map<string, number>()
  const mutationVersionByCardId = new Map<string, number>()
  const mutationTailByCommentKey = new Map<string, Promise<void>>()
  let boardVisitGeneration = 0

  watch(
    () => state.currentBoard?.value?.id ?? null,
    (nextBoardId, previousBoardId) => {
      if (nextBoardId !== previousBoardId) boardVisitGeneration++
    },
    { flush: 'sync' },
  )

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
      generation: boardVisitGeneration,
    }
  }

  function isCurrentBoardVisit(visit: CommentCacheVisit) {
    const currentBoard = state.currentBoard?.value
    return (
      (currentBoard == null || currentBoard.id === visit.boardId) &&
      boardVisitGeneration === visit.generation
    )
  }

  function ownsExactCommentCache(visit: CommentCacheVisit) {
    return isCurrentBoardVisit(visit) && state.cardCommentsByCardId.value === visit.cache
  }

  async function runCommentMutation<T>(
    cardId: string,
    commentId: string,
    visit: CommentCacheVisit,
    mutation: () => Promise<T>,
  ): Promise<T> {
    const key = `${cardId}:${commentId}`
    const previous = mutationTailByCommentKey.get(key) ?? Promise.resolve()
    // A failed predecessor must not cancel a later user intent. It still settles
    // through its own caller/error path; the next request starts afterward.
    const operation = previous.catch(() => undefined).then(() => {
      // The HTTP interceptor reads the token when transport starts. Reject a
      // queued pre-logout intent before the API callback can run under another
      // session's credentials.
      if (!isCurrentBoardVisit(visit)) throw new StaleBoardVisitError()
      return mutation()
    })
    const tail = operation.then(
      () => undefined,
      () => undefined,
    )
    mutationTailByCommentKey.set(key, tail)

    try {
      return await operation
    } finally {
      if (mutationTailByCommentKey.get(key) === tail) {
        mutationTailByCommentKey.delete(key)
      }
    }
  }

  async function reconcileCurrentCommentsAfterStaleVisit(boardId: string, cardId: string) {
    if (state.currentBoard?.value?.id !== boardId) return

    const visit = captureCommentCacheVisit(boardId)
    const readVersion = nextReadVersion(cardId)
    const mutationVersion = currentMutationVersion(cardId)
    try {
      const comments = await cardCommentsApi.getComments(boardId, cardId)
      if (
        isCurrentBoardVisit(visit) &&
        readVersionByCardId.get(cardId) === readVersion &&
        currentMutationVersion(cardId) === mutationVersion
      ) {
        // This read begins only after the write succeeded. It is authoritative
        // for the currently reopened visit, while any older read is rejected by
        // the version/mutation guards above.
        state.cardCommentsByCardId.value[cardId] = comments
      }
    } catch {
      if (isCurrentBoardVisit(visit)) {
        helpers.toast.warning(
          'Comment saved, but comments could not be refreshed. Reopen the card before editing again.',
        )
      }
    }
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
        ownsExactCommentCache(visit) &&
        readVersionByCardId.get(cardId) === readVersion &&
        currentMutationVersion(cardId) === mutationVersion
      ) {
        visit.cache[cardId] = comments
      }
      return comments
    } catch (e: unknown) {
      if (ownsExactCommentCache(visit)) {
        helpers.handleApiError(e, 'Failed to fetch card comments')
      }
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
      markCommentMutation(cardId)

      if (isCurrentBoardVisit(visit)) {
        const currentCache = state.cardCommentsByCardId.value
        const existingComments = currentCache[cardId] ?? []
        // A same-board refresh can commit the stable id before this response
        // arrives. Preserve that fresher object instead of appending a duplicate.
        if (!existingComments.some(comment => comment.id === createdComment.id)) {
          currentCache[cardId] = [...existingComments, createdComment].sort(
            (left, right) =>
              new Date(left.createdAt).getTime() - new Date(right.createdAt).getTime(),
          )
        }
        helpers.toast.success('Comment added')
      } else if (state.currentBoard?.value?.id === boardId) {
        await reconcileCurrentCommentsAfterStaleVisit(boardId, cardId)
      }
      return createdComment
    } catch (e: unknown) {
      if (isCurrentBoardVisit(visit)) {
        helpers.handleApiError(e, 'Failed to create card comment')
      }
      throw e
    } finally {
      if (isCurrentBoardVisit(visit)) state.loading.value = false
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
    try {
      state.loading.value = true
      state.error.value = null
      // The API has no revision/If-Match field. Serialize same-comment writes so
      // server commit order follows user intent order; filtering a late success
      // client-side would otherwise let the server silently keep the older edit.
      const updatedComment = await runCommentMutation(
        cardId,
        commentId,
        visit,
        () => cardCommentsApi.updateComment(boardId, cardId, commentId, dto),
      )
      markCommentMutation(cardId)

      if (isCurrentBoardVisit(visit)) {
        const currentCache = state.cardCommentsByCardId.value
        const existingComments = currentCache[cardId] ?? []
        currentCache[cardId] = existingComments.map((comment) =>
          comment.id === commentId ? updatedComment : comment,
        )
        helpers.toast.success('Comment updated')
      } else if (state.currentBoard?.value?.id === boardId) {
        await reconcileCurrentCommentsAfterStaleVisit(boardId, cardId)
      }
      return updatedComment
    } catch (e: unknown) {
      if (!(e instanceof StaleBoardVisitError) && isCurrentBoardVisit(visit)) {
        helpers.handleApiError(e, 'Failed to update card comment')
      }
      throw e
    } finally {
      if (isCurrentBoardVisit(visit)) state.loading.value = false
    }
  }

  async function deleteCardComment(boardId: string, cardId: string, commentId: string) {
    helpers.guardDemoMutation()
    const visit = captureCommentCacheVisit(boardId)
    try {
      state.loading.value = true
      state.error.value = null
      await runCommentMutation(
        cardId,
        commentId,
        visit,
        () => cardCommentsApi.deleteComment(boardId, cardId, commentId),
      )
      markCommentMutation(cardId)

      if (isCurrentBoardVisit(visit)) {
        const currentCache = state.cardCommentsByCardId.value
        const existingComments = currentCache[cardId] ?? []
        currentCache[cardId] = existingComments.filter(
          (comment) => comment.id !== commentId,
        )
        helpers.toast.success('Comment deleted')
      } else if (state.currentBoard?.value?.id === boardId) {
        await reconcileCurrentCommentsAfterStaleVisit(boardId, cardId)
      }
    } catch (e: unknown) {
      if (!(e instanceof StaleBoardVisitError) && isCurrentBoardVisit(visit)) {
        helpers.handleApiError(e, 'Failed to delete card comment')
      }
      throw e
    } finally {
      if (isCurrentBoardVisit(visit)) state.loading.value = false
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
