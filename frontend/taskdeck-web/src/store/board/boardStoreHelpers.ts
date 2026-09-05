/**
 * Shared helper utilities used across board sub-stores.
 */
import axios from 'axios'
import { useToastStore } from '../toastStore'
import { getErrorMessage } from '../../utils/errorMessage'
import { i18n } from '../../i18n'
import { isDemoMode, DemoModeError } from '../../utils/demoMode'
import type { BoardState } from './boardState'

// The codes axios puts on a client-side timeout: `ECONNABORTED` by default and
// `ETIMEDOUT` when `transitional.clarifyTimeoutError` is on. Both mean the same
// thing to a reader, so both map to the same copy.
const TIMEOUT_CODES = new Set(['ECONNABORTED', 'ETIMEDOUT'])

/**
 * Whether this failure is a client-side timeout — a routine outcome on every
 * board read since #2685 bounded them (`timeout: BOARD_REQUEST_TIMEOUT_MS`,
 * `skipRetry: true`), not an exotic one.
 *
 * The code check is the real test. The message check behind it is the
 * belt-and-braces arm for an adapter that reports a timeout without setting a
 * code, and it is gated on `isAxiosError` AND on the absence of a response so
 * that an ordinary 500 whose SERVER message happens to contain the word
 * "timeout" keeps the server's own wording instead of being overwritten.
 */
function isTimeoutError(err: unknown): boolean {
  if (typeof err !== 'object' || err === null) {
    return false
  }
  const code = (err as { code?: unknown }).code
  if (typeof code === 'string' && TIMEOUT_CODES.has(code)) {
    return true
  }
  return axios.isAxiosError(err) && !err.response && /timeout/i.test(err.message)
}

/**
 * The message a board-store failure shows the user, in the active language.
 *
 * `getErrorMessage` prefers `response.data.message` and then `err.message`,
 * which is right for an API error and wrong for a transport one: a transport
 * failure carries no server message, so `err.message` is axios' own
 * untranslated English — "timeout of 10000ms exceeded" — rendered verbatim
 * inside a localized alert and a localized toast (#2689 item 3). The two
 * transport shapes are answered from the catalog first; every other error keeps
 * exactly the behaviour it had.
 *
 * A cancel is mapped rather than dropped even though it should not reach here
 * (both read paths in `boardCrudStore` return on `axios.isCancel` before
 * calling this, and the only aborter today — the logout reset — bumps the read
 * generation first). Dropping it would leave `state.error` null with `boards`
 * still empty, and `BoardsListView`'s `v-else-if` chain renders that as the
 * EMPTY state: telling the user they have no boards when no read ever confirmed
 * it, which is the #1961 class of lie the shared read exists to prevent. A
 * short, honest line plus the Retry control is the smaller failure.
 *
 * `i18n.global.t` rather than `useI18n()` for the reason recorded in
 * `locales/en/review.ts` §1: this factory is called from stores and from specs
 * that never mount a component, and `i18n.global.t` still reads the live
 * `i18n.global.locale`, so the copy follows a language switch.
 */
function resolveErrorMessage(err: unknown, fallback: string): string {
  if (isTimeoutError(err)) {
    return i18n.global.t('boards.error.timeout')
  }
  if (axios.isCancel(err)) {
    return i18n.global.t('boards.error.cancelled')
  }
  return getErrorMessage(err, fallback)
}

export function createBoardHelpers(state: BoardState) {
  const toast = useToastStore()
  const boardDetailMutationEpochs = new Map<string, number>()

  /**
   * Writes the failure to both surfaces and RETURNS the message it wrote.
   *
   * The return exists so a caller can tell later whether `state.error` is still
   * the message it raised or has since been replaced by another surface — the
   * board state carries one shared `error` ref, so "clear the error on success"
   * is only safe when the success owns that error (#2689 round-2 finding 2).
   * Every existing caller ignores the return and is unaffected.
   */
  const handleApiError = (err: unknown, fallback: string): string => {
    const message = resolveErrorMessage(err, fallback)
    state.error.value = message
    toast.error(message)
    return message
  }

  function guardDemoMutation(): never | void {
    if (isDemoMode) {
      toast.info('This action is view-only in demo mode.')
      throw new DemoModeError()
    }
  }

  const isHttpNotFound = (err: unknown): boolean => {
    const candidate = err as { response?: { status?: number } } | null
    return candidate?.response?.status === 404
  }

  const isHttpConflict = (err: unknown): boolean => {
    const candidate = err as { response?: { status?: number } } | null
    return candidate?.response?.status === 409
  }

  const updateColumnCardCount = (columnId: string, delta: number) => {
    if (!state.currentBoard.value) return

    const column = state.currentBoard.value.columns.find((c) => c.id === columnId)
    if (!column) return

    const nextCount = (column.cardCount ?? 0) + delta
    column.cardCount = Math.max(0, nextCount)
  }

  const getBoardDetailMutationEpoch = (boardId: string) =>
    boardDetailMutationEpochs.get(boardId) ?? 0

  const markBoardDetailMutation = (boardId: string) => {
    boardDetailMutationEpochs.set(boardId, getBoardDetailMutationEpoch(boardId) + 1)
  }

  return {
    toast,
    handleApiError,
    guardDemoMutation,
    isHttpNotFound,
    isHttpConflict,
    updateColumnCardCount,
    getBoardDetailMutationEpoch,
    markBoardDetailMutation,
    isDemoMode,
  }
}

export type BoardHelpers = ReturnType<typeof createBoardHelpers>
