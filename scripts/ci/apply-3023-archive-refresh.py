from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


crud_path = Path("frontend/taskdeck-web/src/store/board/boardCrudStore.ts")
crud = crud_path.read_text(encoding="utf-8")

crud = replace_once(
    crud,
    """export interface BoardFetchOptions {
  intent?: BoardFetchIntent
  /** Report a failed refresh of an already committed mutation only while this read owns the context. */
  backgroundFailureMessage?: string
}
""",
    """export interface BoardFetchOptions {
  intent?: BoardFetchIntent
  /** Report a failed refresh of an already committed mutation only while this read owns the context. */
  backgroundFailureMessage?: string
  /**
   * Retain the current board's loaded comment cache while replacing
   * board/card/label detail. Honoured only for same-board background
   * reconciliation while an editor remains mounted.
   */
  preserveCardComments?: boolean
}
""",
    "BoardFetchOptions",
)

crud = replace_once(
    crud,
    """interface ActiveBoardFetch {
  boardId: string
  intent: BoardFetchIntent
  generation: number
  backgroundFailureMessage?: string
  controller: AbortController
  promise: Promise<boolean>
}
""",
    """interface ActiveBoardFetch {
  boardId: string
  intent: BoardFetchIntent
  generation: number
  backgroundFailureMessage?: string
  preserveCardComments: boolean
  controller: AbortController
  promise: Promise<boolean>
}
""",
    "ActiveBoardFetch",
)

crud = replace_once(
    crud,
    """interface QueuedBackgroundBoardFetch {
  boardId: string
  backgroundFailureMessage?: string
  promise: Promise<boolean>
  resolve: (committed: boolean) => void
}
""",
    """interface QueuedBackgroundBoardFetch {
  boardId: string
  backgroundFailureMessage?: string
  preserveCardComments: boolean
  promise: Promise<boolean>
  resolve: (committed: boolean) => void
}
""",
    "QueuedBackgroundBoardFetch",
)

crud = replace_once(
    crud,
    """  function queueBackgroundBoardFetch(id: string, backgroundFailureMessage?: string): Promise<boolean> {
    if (queuedBackgroundBoardFetch?.boardId === id) {
      if (backgroundFailureMessage) queuedBackgroundBoardFetch.backgroundFailureMessage = backgroundFailureMessage
      return queuedBackgroundBoardFetch.promise
    }

    settleQueuedBackgroundBoardFetch()
    let resolve!: (committed: boolean) => void
    const promise = new Promise<boolean>((innerResolve) => {
      resolve = innerResolve
    })
    queuedBackgroundBoardFetch = { boardId: id, promise, resolve, backgroundFailureMessage }
    return promise
  }
""",
    """  function queueBackgroundBoardFetch(
    id: string,
    backgroundFailureMessage?: string,
    preserveCardComments = false,
  ): Promise<boolean> {
    if (queuedBackgroundBoardFetch?.boardId === id) {
      if (backgroundFailureMessage) queuedBackgroundBoardFetch.backgroundFailureMessage = backgroundFailureMessage
      if (preserveCardComments) queuedBackgroundBoardFetch.preserveCardComments = true
      return queuedBackgroundBoardFetch.promise
    }

    settleQueuedBackgroundBoardFetch()
    let resolve!: (committed: boolean) => void
    const promise = new Promise<boolean>((innerResolve) => {
      resolve = innerResolve
    })
    queuedBackgroundBoardFetch = {
      boardId: id,
      promise,
      resolve,
      backgroundFailureMessage,
      preserveCardComments,
    }
    return promise
  }
""",
    "queueBackgroundBoardFetch",
)

crud = replace_once(
    crud,
    """    queuedBackgroundBoardFetch = null
    void startBoardFetch(queued.boardId, 'background', queued.backgroundFailureMessage).then(queued.resolve, () => {
      queued.resolve(false)
    })
""",
    """    queuedBackgroundBoardFetch = null
    void startBoardFetch(
      queued.boardId,
      'background',
      queued.backgroundFailureMessage,
      queued.preserveCardComments,
    ).then(queued.resolve, () => {
      queued.resolve(false)
    })
""",
    "drainQueuedBackgroundBoardFetch",
)

crud = replace_once(
    crud,
    """  function fetchBoard(id: string, options: BoardFetchOptions = {}): Promise<boolean> {
    const intent = options.intent ?? 'explicit'

    if (intent === 'background' && activeBoardFetch) {
      if (activeBoardFetch.boardId !== id) {
        return Promise.resolve(false)
      }

      if (activeBoardFetch.intent === 'explicit') {
        return queueBackgroundBoardFetch(id, options.backgroundFailureMessage)
      }

      if (options.backgroundFailureMessage) activeBoardFetch.backgroundFailureMessage = options.backgroundFailureMessage
      return activeBoardFetch.promise
    }

    if (intent === 'explicit') {
      // A route load or Retry includes all mutations observed before it began,
      // so it supersedes any older queued background refresh.
      settleQueuedBackgroundBoardFetch()
    }

    return startBoardFetch(id, intent, options.backgroundFailureMessage)
  }

  function startBoardFetch(id: string, intent: BoardFetchIntent, backgroundFailureMessage?: string): Promise<boolean> {
""",
    """  function fetchBoard(id: string, options: BoardFetchOptions = {}): Promise<boolean> {
    const intent = options.intent ?? 'explicit'
    const preserveCardComments = intent === 'background' && options.preserveCardComments === true

    if (intent === 'background' && activeBoardFetch) {
      if (activeBoardFetch.boardId !== id) {
        return Promise.resolve(false)
      }

      // The kept-open editor owns live comment state. If its reconciliation
      // arrives behind an explicit same-board load, upgrade that active read
      // before it can clear the cache, then retain the flag on the successor.
      if (preserveCardComments) activeBoardFetch.preserveCardComments = true

      if (activeBoardFetch.intent === 'explicit') {
        return queueBackgroundBoardFetch(
          id,
          options.backgroundFailureMessage,
          preserveCardComments,
        )
      }

      if (options.backgroundFailureMessage) activeBoardFetch.backgroundFailureMessage = options.backgroundFailureMessage
      return activeBoardFetch.promise
    }

    if (intent === 'explicit') {
      // A route load or Retry includes all mutations observed before it began,
      // so it supersedes any older queued background refresh.
      settleQueuedBackgroundBoardFetch()
    }

    return startBoardFetch(
      id,
      intent,
      options.backgroundFailureMessage,
      preserveCardComments,
    )
  }

  function startBoardFetch(
    id: string,
    intent: BoardFetchIntent,
    backgroundFailureMessage?: string,
    preserveCardComments = false,
  ): Promise<boolean> {
""",
    "fetchBoard/startBoardFetch",
)

crud = replace_once(
    crud,
    """      generation: requestGeneration,
      backgroundFailureMessage,
      controller,
""",
    """      generation: requestGeneration,
      backgroundFailureMessage,
      preserveCardComments,
      controller,
""",
    "active request preservation",
)

crud = replace_once(
    crud,
    """      void queueBackgroundBoardFetch(id, request.backgroundFailureMessage)
    }

    const performFetch = async (): Promise<boolean> => {
""",
    """      void queueBackgroundBoardFetch(
        id,
        request.backgroundFailureMessage,
        request.preserveCardComments,
      )
    }

    const shouldPreserveCurrentComments = () =>
      request.preserveCardComments && state.currentBoard.value?.id === id

    const performFetch = async (): Promise<boolean> => {
""",
    "invalidation successor preservation",
)

crud = replace_once(
    crud,
    """        state.currentBoard.value = demo.board
        state.currentBoardCards.value = demo.cards
        state.currentBoardLabels.value = []
        state.cardCommentsByCardId.value = {}
        if (intent === 'explicit') {
""",
    """        const preserveCurrentComments = shouldPreserveCurrentComments()
        state.currentBoard.value = demo.board
        state.currentBoardCards.value = demo.cards
        state.currentBoardLabels.value = []
        if (!preserveCurrentComments) state.cardCommentsByCardId.value = {}
        if (intent === 'explicit') {
""",
    "demo comment preservation",
)

crud = replace_once(
    crud,
    """        applyBoardCardCounts(board, cards)

        state.currentBoard.value = board
""",
    """        applyBoardCardCounts(board, cards)

        const preserveCurrentComments = shouldPreserveCurrentComments()
        state.currentBoard.value = board
""",
    "server comment preservation boundary",
)

crud = replace_once(
    crud,
    """        state.currentBoardCards.value = cards
        state.currentBoardLabels.value = labels
        state.cardCommentsByCardId.value = {}
        return true
""",
    """        state.currentBoardCards.value = cards
        state.currentBoardLabels.value = labels
        if (!preserveCurrentComments) state.cardCommentsByCardId.value = {}
        return true
""",
    "server comment preservation commit",
)

crud_path.write_text(crud, encoding="utf-8")

modal_path = Path("frontend/taskdeck-web/src/components/board/CardModal.vue")
modal = modal_path.read_text(encoding="utf-8")

modal = replace_once(
    modal,
    """import { useBoardStore } from '../../store/boardStore'
import {
""",
    """import { useBoardStore } from '../../store/boardStore'
import { refreshBoardAfterLifecycleChange } from '../../utils/boardLifecycleRefresh'
import {
""",
    "CardModal lifecycle refresh import",
)

modal = replace_once(
    modal,
    """    archiveStateAfterChange.value = !cardIsArchived.value
    archiveCompletedWithDraft.value = true
    void boardStore.fetchBoard(props.card.boardId)
    return
""",
    """    archiveStateAfterChange.value = !cardIsArchived.value
    archiveCompletedWithDraft.value = true
    void refreshBoardAfterLifecycleChange(boardStore, props.card.boardId, {
      preserveCardComments: true,
    })
    return
""",
    "kept-open archive refresh",
)

modal = replace_once(
    modal,
    """  if (destination) void router.push(destination)
  if (refreshBoard) void boardStore.fetchBoard(props.card.boardId)
}
""",
    """  if (destination) void router.push(destination)
  if (refreshBoard) {
    void refreshBoardAfterLifecycleChange(boardStore, props.card.boardId)
  }
}
""",
    "recovery-close archive refresh",
)

modal_path.write_text(modal, encoding="utf-8")
